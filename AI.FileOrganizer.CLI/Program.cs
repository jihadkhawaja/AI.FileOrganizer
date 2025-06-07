using AI.FileOrganizer;
using LLama;
using LLama.Common;
using LLama.Native;
using LLamaSharp.SemanticKernel;
using LLamaSharp.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Reflection;
using System.Text.RegularExpressions;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

Console.WriteLine("Starting AI File Organizer CLI...");

// Configure logging. Change this to `true` to see log messages from llama.cpp
var showLLamaCppLogs = false;
NativeLibraryConfig
   .All
   .WithLogCallback((level, message) =>
   {
       if (showLLamaCppLogs)
           Console.WriteLine($"[llama {level}]: {message.TrimEnd('\n')}");
   });

// Configure native library to use. This must be done before any other llama.cpp methods are called!
NativeLibraryConfig
   .All
   .WithCuda()
   .WithVulkan();

// Calling this method forces loading to occur now.
NativeApi.llama_empty_call();

// Load configuration from user secrets
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

string? modelPath = config["ModelPath"];
string? multiModalProj = config["MultiModalProj"];
const int maxImageTokens = 1024;

// Detect if model is multimodal (simple heuristic: path contains "llava" or multimodal project path is set)
bool isMultimodal = !string.IsNullOrWhiteSpace(multiModalProj) &&
    (modelPath.Contains("llava", StringComparison.OrdinalIgnoreCase) ||
     multiModalProj.Contains("llava", StringComparison.OrdinalIgnoreCase));

// Model and executor setup
ModelParams parameters = new(modelPath);

LLamaWeights model = null!;
LLamaContext context = null!;
LLavaWeights? clipModel = null;
InteractiveExecutor? multimodalExecutor = null;
StatelessExecutor? slex = null;

if (isMultimodal)
{
    model = await LLamaWeights.LoadFromFileAsync(parameters);
    context = model.CreateContext(parameters);
    clipModel = await LLavaWeights.LoadFromFileAsync(multiModalProj);
    multimodalExecutor = new InteractiveExecutor(context, clipModel);
}
else
{
    model = await LLamaWeights.LoadFromFileAsync(parameters);
    slex = new StatelessExecutor(model, parameters);
}

// Set up chat completion service
IChatCompletionService chatService = isMultimodal
    ? new LLamaSharpChatCompletion(
        multimodalExecutor!,
        new LLamaSharpPromptExecutionSettings
        {
            MaxTokens = maxImageTokens,
            Temperature = 0,
            TopP = 0.1,
        })
    : new LLamaSharpChatCompletion(
        slex!,
        new LLamaSharpPromptExecutionSettings
        {
            MaxTokens = -1,
            Temperature = 0,
            TopP = 0.1,
        }
    );

// Register plugin and kernel
var organize = new Organize();
var plugins = new KernelPluginCollection();
plugins.AddFromObject(organize, "FileOrganizer");
var kernel = new Kernel(plugins: plugins);

var knownDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
    { "documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
    { "desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop) }
};

string ResolveDir(string dir)
{
    var cleaned = dir.Trim('"', ' ', '\\', '/');
    return knownDirs.TryGetValue(cleaned, out var mapped) ? mapped : dir.Trim();
}

// List all available kernel functions at startup
Console.WriteLine("Available File Organizer Functions:");
foreach (var method in typeof(Organize).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
{
    if (method.GetCustomAttribute(typeof(Microsoft.SemanticKernel.KernelFunctionAttribute)) is not null)
    {
        var desc = method.GetCustomAttribute(typeof(System.ComponentModel.DescriptionAttribute)) as System.ComponentModel.DescriptionAttribute;
        Console.WriteLine($"- {method.Name}{(desc != null ? $": {desc.Description}" : "")}");
    }
}
Console.WriteLine();

string[]? previousFiles = null;

Console.WriteLine("File Organizer CLI (type 'exit' to quit)");
while (true)
{
    Console.Write("\n> ");
    var input = Console.ReadLine();
    if (input is null || input.Trim().ToLower() == "exit")
        break;

    var chatHistory = new ChatHistory
    {
        new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System,
    """
            You are a file organization assistant.
            The user may refer to directories by common names such as 'Downloads', 'Documents', or 'Desktop', or by explicit paths.
            You can also refer to the previous file list using the word 'previous'.
            Interpret the user's intent and respond with:
            - [LIST FILES] {directory}
            - [MOVE FILE] {sourceFilePath} {destinationDirectory}
            - [CATEGORIZE FILES] {directory}
            - [ORGANIZE FILES] {directory}
            - [CATEGORIZE BY NAME CONTEXT] {directory}
            - [CATEGORIZE BY CONTENT CONTEXT] {directory}
            - [CATEGORIZE IMAGES BY CONTEXT] {directory}
            - [COUNT PREVIOUS FILES]
            Only respond with the bracketed command and arguments.
            """),
        new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User, input)
    };

    var chatResult = await chatService.GetChatMessageContentAsync(
        chatHistory,
        new LLamaSharpPromptExecutionSettings { Temperature = 0.0f, TopP = 0.1f },
        kernel,
        CancellationToken.None
    );

    var response = chatResult.Content?.Trim() ?? "";

    if (response.StartsWith("[LIST FILES]"))
    {
        var dir = ResolveDir(response.Replace("[LIST FILES]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "ListFiles", new() { ["directory"] = dir });
        previousFiles = result.GetValue<string[]>() ?? Array.Empty<string>();
        Console.WriteLine(previousFiles.Length == 0 ? "No files found." : string.Join(Environment.NewLine, previousFiles));
    }
    else if (response.StartsWith("[MOVE FILE]"))
    {
        var commandargs = response.Replace("[MOVE FILE]", "").Trim().Split(' ', 2);
        if (commandargs.Length == 2)
        {
            var src = commandargs[0];
            var dest = ResolveDir(commandargs[1]);
            var result = await kernel.InvokeAsync("FileOrganizer", "MoveFile", new() { ["sourceFilePath"] = src, ["destinationDirectory"] = dest });
            Console.WriteLine(result.GetValue<string>());
        }
        else
        {
            Console.WriteLine("Invalid move command.");
        }
    }
    else if (response.StartsWith("[ORGANIZE FILES]"))
    {
        var dir = ResolveDir(response.Replace("[ORGANIZE FILES]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "OrganizeByExtension", new() { ["directory"] = dir });
        Console.WriteLine(result.GetValue<string>());
    }
    else if (response.StartsWith("[CATEGORIZE FILES]"))
    {
        var dir = ResolveDir(response.Replace("[CATEGORIZE FILES]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByExtension", new() { ["directory"] = dir });
        Console.WriteLine(result.GetValue<string>());
    }
    else if (response.StartsWith("[CATEGORIZE BY NAME CONTEXT]"))
    {
        var dir = ResolveDir(response.Replace("[CATEGORIZE BY NAME CONTEXT]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByNameContext", new() { ["directory"] = dir });
        Console.WriteLine(result.GetValue<string>());
    }
    else if (response.StartsWith("[CATEGORIZE BY CONTENT CONTEXT]"))
    {
        var dir = ResolveDir(response.Replace("[CATEGORIZE BY CONTENT CONTEXT]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByContentContext", new() { ["directory"] = dir });
        Console.WriteLine(result.GetValue<string>());
    }
    else if (response.StartsWith("[CATEGORIZE IMAGES BY CONTEXT]"))
    {
        var dir = ResolveDir(response.Replace("[CATEGORIZE IMAGES BY CONTEXT]", "").Trim());
        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeImagesByContext", new() { ["directory"] = dir, ["isMultimodal"] = isMultimodal });
        var output = result.GetValue<string>();

        Console.WriteLine(output);

        if (isMultimodal)
        {
            var imagePaths = Regex.Matches(output ?? "", @"[^\s]+(\.jpg|\.jpeg|\.png|\.bmp|\.gif|\.webp|\.tiff)", RegexOptions.IgnoreCase)
                                  .Select(m => m.Value)
                                  .ToList();

            var imageContextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (imagePaths.Count > 0 && multimodalExecutor != null)
            {
                foreach (var imagePath in imagePaths)
                {
                    if (!File.Exists(imagePath))
                    {
                        Console.WriteLine($"Image not found: {imagePath}");
                        continue;
                    }

                    var prompt = $"{{{imagePath}}}\nUSER:\nProvide a single short label (e.g. 'cat', 'invoice', 'nature') describing the main content of the image. Only output the label.\nASSISTANT:\n";
                    var inferenceParams = new InferenceParams
                    {
                        SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline
                        {
                            Temperature = 0.1f
                        },
                        AntiPrompts = new List<string> { "\nUSER:" },
                        MaxTokens = 16
                    };

                    multimodalExecutor.Context.NativeHandle.KvCacheRemove(LLamaSeqId.Zero, -1, -1);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[Multimodal] Analyzing image: {imagePath}");
                    Console.ForegroundColor = ConsoleColor.White;

                    string label = "";
                    await foreach (var text in multimodalExecutor.InferAsync(prompt, inferenceParams))
                    {
                        label += text;
                    }
                    label = label.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
                    Console.WriteLine($"Label: {label}");
                    imageContextMap[imagePath] = label;
                }

                // Now organize images by context label
                var organizeResult = await kernel.InvokeAsync("FileOrganizer", "OrganizeImagesByContext", new() { ["directory"] = dir, ["imageContextMap"] = imageContextMap });
                Console.WriteLine(organizeResult.GetValue<string>());
            }
        }
    }
    else if (response.StartsWith("[COUNT PREVIOUS FILES]"))
    {
        if (previousFiles != null)
            Console.WriteLine($"There are {previousFiles.Length} files.");
        else
            Console.WriteLine("No previous file list found.");
    }
    else
    {
        Console.WriteLine("Unrecognized command: " + response);
    }
}