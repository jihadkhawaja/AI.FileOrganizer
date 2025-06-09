using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.SemanticKernel;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLamaSharp.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;
using System.Reflection;

namespace AI.FileOrganizer.CLI
{
    public class CliLoop
    {
        private readonly ModelManager _modelManager;
        private readonly Kernel _kernel;
        private string[]? _previousFiles;
        private Dictionary<string, string>? _lastImageContextMap;

        public CliLoop(ModelManager modelManager, Kernel kernel)
        {
            _modelManager = modelManager;
            _kernel = kernel;
        }

        public void ListAvailableFunctions()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Available File Organizer Functions:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (var method in typeof(Organize).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute(typeof(Microsoft.SemanticKernel.KernelFunctionAttribute)) is not null)
                {
                    var desc = method.GetCustomAttribute(typeof(System.ComponentModel.DescriptionAttribute)) as System.ComponentModel.DescriptionAttribute;
                    Console.WriteLine($"- {method.Name}{(desc != null ? $": {desc.Description}" : "")}");
                }
            }
            Console.WriteLine();
            Console.ResetColor();
        }

        public async Task RunAsync()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("File Organizer CLI (type 'exit' to quit)");
            Console.ResetColor();
            while (true)
            {
                Console.Write("\n> ");
                var input = Console.ReadLine();
                if (input is null || input.Trim().ToLower() == "exit")
                    break;

                // Format chat history with Gemma control tokens
                var systemPrompt =
                    """
                    <start_of_turn>user         
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
                    - [ORGANIZE IMAGES BY CONTEXT] {directory}
                    - [CATEGORIZE FOLDERS BY PATTERN] {directory} {pattern}
                    - [ORGANIZE FOLDERS BY PATTERN] {directory} {pattern}
                    - [CATEGORIZE FOLDERS BY SIZE] {directory}
                    - [ORGANIZE FOLDERS BY SIZE] {directory} {smallThreshold} {largeThreshold}
                    - [COUNT PREVIOUS FILES]
                    Only respond with the bracketed command and arguments.<end_of_turn>
                    <start_of_turn>model
                    Understood. I will help organize files by interpreting commands and responding with the appropriate bracketed format.<end_of_turn>
                    """;

                var userPrompt = $"<start_of_turn>user\n{input}<end_of_turn>\n<start_of_turn>model\n";

                var chatHistory = new ChatHistory
                {
                    new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System, systemPrompt),
                    new ChatMessageContent(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User, userPrompt)
                };

                var settings = new LLamaSharpPromptExecutionSettings
                {
                    Temperature = 0.0f,
                    TopP = 0.1f
                };

                var chatResult = await _modelManager.ChatService.GetChatMessageContentAsync(
                    chatHistory,
                    settings,
                    _kernel,
                    CancellationToken.None
                );

                var response = chatResult.Content?.Trim() ?? "";

                // Helper for confirmation
                bool Confirm(string actionDesc)
                {
                    Console.Write($"Confirm action: {actionDesc}? (y/n): ");
                    var confirm = Console.ReadLine();
                    return confirm != null && confirm.Trim().ToLower() == "y";
                }

                if (response.StartsWith("[LIST FILES]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[LIST FILES]", "").Trim());
                    var result = await _kernel.InvokeAsync("FileOrganizer", "ListFiles", new() { ["directory"] = dir });
                    _previousFiles = result.GetValue<string[]>() ?? Array.Empty<string>();
                    Console.WriteLine(_previousFiles.Length == 0 ? "No files found." : string.Join(Environment.NewLine, _previousFiles));
                }
                else if (response.StartsWith("[MOVE FILE]"))
                {
                    var commandargs = response.Replace("[MOVE FILE]", "").Trim().Split(' ', 2);
                    if (commandargs.Length == 2)
                    {
                        var src = commandargs[0];
                        var dest = _modelManager.ResolveDir(commandargs[1]);
                        if (Confirm($"Move file '{src}' to '{dest}'"))
                        {
                            var result = await _kernel.InvokeAsync("FileOrganizer", "MoveFile", new() { ["sourceFilePath"] = src, ["destinationDirectory"] = dest });
                            Console.WriteLine(result.GetValue<string>());
                        }
                        else
                        {
                            Console.WriteLine("Action cancelled.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid move command.");
                    }
                }
                else if (response.StartsWith("[ORGANIZE FILES]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[ORGANIZE FILES]", "").Trim());
                    if (Confirm($"Organize files in '{dir}' by extension"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "OrganizeByExtension", new() { ["directory"] = dir });
                        Console.WriteLine(result.GetValue<string>());
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else if (response.StartsWith("[CATEGORIZE FILES]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[CATEGORIZE FILES]", "").Trim());
                    if (Confirm($"Categorize files in '{dir}' by extension"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeByExtension", new() { ["directory"] = dir });
                        Console.WriteLine(result.GetValue<string>());
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else if (response.StartsWith("[CATEGORIZE BY NAME CONTEXT]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[CATEGORIZE BY NAME CONTEXT]", "").Trim());
                    if (Confirm($"Categorize files in '{dir}' by name context"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeByNameContext", new() { ["directory"] = dir });
                        Console.WriteLine(result.GetValue<string>());
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else if (response.StartsWith("[CATEGORIZE BY CONTENT CONTEXT]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[CATEGORIZE BY CONTENT CONTEXT]", "").Trim());
                    if (Confirm($"Categorize files in '{dir}' by content context"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeByContentContext", new() { ["directory"] = dir });
                        Console.WriteLine(result.GetValue<string>());
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else if (response.StartsWith("[CATEGORIZE IMAGES BY CONTEXT]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[CATEGORIZE IMAGES BY CONTEXT]", "").Trim());
                    _lastImageContextMap = null;
                    if (Confirm($"Categorize images in '{dir}' by context"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeImagesByContext", new() { ["directory"] = dir, ["isMultimodal"] = _modelManager.IsMultimodal });
                        var output = result.GetValue<string>();

                        Console.WriteLine(output);

                        var categorizeOutput = result.GetValue<string>();
                        Console.WriteLine(categorizeOutput);

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[DEBUG] IsMultimodal: {_modelManager.IsMultimodal}, MultimodalExecutor available: {_modelManager.MultimodalExecutor != null}");
                        Console.ResetColor();

                        if (_modelManager.IsMultimodal && _modelManager.MultimodalExecutor != null)
                        {
                            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff" };
                            List<string> imagePaths;
                            try
                            {
                                imagePaths = Directory.GetFiles(dir)
                                    .Where(f => imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                    .ToList();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[Error] Failed to list image files in directory '{dir}': {ex.Message}");
                                Console.ResetColor();
                                imagePaths = new List<string>();
                            }

                            if (imagePaths.Count == 0)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("[WARNING] No image files found in the directory for vision labeling.");
                                Console.ResetColor();
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"Found [yellow]{imagePaths.Count}[/] image(s) to process in '{dir}'.");
                                var imageContextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                                foreach (var imagePath in imagePaths)
                                {
                                    var label = await ProcessSingleImage(imagePath);
                                    if (!string.IsNullOrWhiteSpace(label) && !label.ToLower().Contains("error_") && !label.ToLower().Contains("unknown_"))
                                    {
                                        imageContextMap[imagePath] = label;
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine($"[Skipping] Image {Path.GetFileName(imagePath)} due to invalid or error label: '{label}'");
                                        Console.ResetColor();
                                    }
                                }

                                _lastImageContextMap = imageContextMap;

                                if (imageContextMap.Count > 0)
                                {
                                     AnsiConsole.MarkupLine($"[green]Successfully labeled {imageContextMap.Count} image(s).[/]");
                                    if (Confirm($"Organize these {imageContextMap.Count} images in '{dir}' by detected context labels"))
                                    {
                                        var organizeResult = await _kernel.InvokeAsync("FileOrganizer", "OrganizeImagesByContext", new() { ["directory"] = dir, ["imageContextMap"] = imageContextMap });
                                        Console.WriteLine(organizeResult.GetValue<string>());
                                    }
                                    else
                                    {
                                        Console.WriteLine("Organization action cancelled.");
                                    }
                                }
                                else
                                {
                                     AnsiConsole.MarkupLine("[yellow]No images were successfully labeled with context.[/]");
                                }
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] Multimodal vision model (MultimodalExecutor) is not available. Please check your model setup. Cannot categorize images by context.");
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Image categorization action cancelled.");
                    }
                }
                else if (response.StartsWith("[COUNT PREVIOUS FILES]"))
                {
                    if (_previousFiles != null)
                        Console.WriteLine($"There are {_previousFiles.Length} files.");
                    else
                        Console.WriteLine("No previous file list found.");
                }
                else if (response.StartsWith("[CATEGORIZE FOLDERS BY PATTERN]"))
                {
                    var args = response.Replace("[CATEGORIZE FOLDERS BY PATTERN]", "").Trim().Split(' ', 2);
                    if (args.Length == 2)
                    {
                        var dir = _modelManager.ResolveDir(args[0]);
                        var pattern = args[1];
                        if (Confirm($"Categorize folders in '{dir}' by pattern '{pattern}'"))
                        {
                            var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeFoldersByNamePattern", new() { ["directory"] = dir, ["pattern"] = pattern });
                            Console.WriteLine(result.GetValue<string>());
                        }
                        else
                        {
                            Console.WriteLine("Action cancelled.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid categorize folders by pattern command.");
                    }
                }
                else if (response.StartsWith("[ORGANIZE FOLDERS BY PATTERN]"))
                {
                    var args = response.Replace("[ORGANIZE FOLDERS BY PATTERN]", "").Trim().Split(' ', 2);
                    if (args.Length == 2)
                    {
                        var dir = _modelManager.ResolveDir(args[0]);
                        var pattern = args[1];
                        if (Confirm($"Organize folders in '{dir}' by pattern '{pattern}'"))
                        {
                            var result = await _kernel.InvokeAsync("FileOrganizer", "OrganizeFoldersByNamePattern", new() { ["directory"] = dir, ["pattern"] = pattern });
                            Console.WriteLine(result.GetValue<string>());
                        }
                        else
                        {
                            Console.WriteLine("Action cancelled.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid organize folders by pattern command.");
                    }
                }
                else if (response.StartsWith("[CATEGORIZE FOLDERS BY SIZE]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[CATEGORIZE FOLDERS BY SIZE]", "").Trim());
                    if (Confirm($"Categorize folders in '{dir}' by size"))
                    {
                        var result = await _kernel.InvokeAsync("FileOrganizer", "CategorizeFoldersBySize", new() { ["directory"] = dir });
                        Console.WriteLine(result.GetValue<string>());
                    }
                    else
                    {
                        Console.WriteLine("Action cancelled.");
                    }
                }
                else if (response.StartsWith("[ORGANIZE FOLDERS BY SIZE]"))
                {
                    var args = response.Replace("[ORGANIZE FOLDERS BY SIZE]", "").Trim().Split(' ', 3);
                    if (args.Length >= 1)
                    {
                        var dir = _modelManager.ResolveDir(args[0]);
                        int small = 5, large = 20;
                        if (args.Length >= 2) int.TryParse(args[1], out small);
                        if (args.Length == 3) int.TryParse(args[2], out large);
                        if (Confirm($"Organize folders in '{dir}' by size (small: {small}, large: {large})"))
                        {
                            var result = await _kernel.InvokeAsync("FileOrganizer", "OrganizeFoldersBySize", new() { ["directory"] = dir, ["smallThreshold"] = small, ["largeThreshold"] = large });
                            Console.WriteLine(result.GetValue<string>());
                        }
                        else
                        {
                            Console.WriteLine("Action cancelled.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid organize folders by size command.");
                    }
                }
                else if (response.StartsWith("[ORGANIZE IMAGES BY CONTEXT]"))
                {
                    var dir = _modelManager.ResolveDir(response.Replace("[ORGANIZE IMAGES BY CONTEXT]", "").Trim());
                    if (_modelManager.IsMultimodal && _modelManager.Executor != null && _lastImageContextMap != null && _lastImageContextMap.Count > 0)
                    {
                        if (Confirm($"Organize images in '{dir}' by last detected context labels"))
                        {
                            var organizeResult = await _kernel.InvokeAsync("FileOrganizer", "OrganizeImagesByContext", new() { ["directory"] = dir, ["imageContextMap"] = _lastImageContextMap });
                            Console.WriteLine(organizeResult.GetValue<string>());
                        }
                        else
                        {
                            Console.WriteLine("Action cancelled.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No image context map available. Please run image categorization first.");
                    }
                }
                else
                {
                    Console.WriteLine("Unrecognized command: " + response);
                }
            }
        }

    private async Task<string> ProcessSingleImage(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[Warning] Image file not found: {imagePath}");
            Console.ResetColor();
            return "error_file_not_found";
        }

        if (!_modelManager.IsMultimodal || _modelManager.MultimodalExecutor == null || _modelManager.ClipModel == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Multimodal executor or CLIP model is not available in ModelManager. Cannot process image.");
            Console.ResetColor();
            return "error_model_unavailable";
        }

        var multimodalExecutor = _modelManager.MultimodalExecutor;

        const int maxTokens = 16;

        // Ensure the prompt is a standard string literal or use verbatim if multi-line is intended here.
        // For this type of prompt, a standard string with \n is usually safer.
        var prompt = $"{{{imagePath}}}\nUSER:\nProvide a single short label (e.g. 'animal', 'invoice', 'text', 'nature', 'person', 'screenshot', 'art', 'other') describing the main content of the image. Only output the label.\nASSISTANT:\n";

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Processing image: {Path.GetFileName(imagePath)} with shared multimodal executor. Max inference tokens: {maxTokens}. Context size: {multimodalExecutor.Context.ContextSize}.");
        Console.ResetColor();

        var inferenceParams = new InferenceParams
        {
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.1f,
                TopP = 0.9f
            },
            // Corrected AntiPrompts list with a verbatim string for the newline
            AntiPrompts = new List<string> { @"
USER:" },
            MaxTokens = maxTokens
        };

        List<byte[]> imageBytesList;
        try
        {
            imageBytesList = new List<byte[]> { File.ReadAllBytes(imagePath) };
        }
        catch (IOException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Could not load image '{imagePath}': {ex.Message}");
            Console.ResetColor();
            return "error_image_load";
        }

        multimodalExecutor.Images.Clear();
        multimodalExecutor.Context.NativeHandle.KvCacheRemove(LLamaSeqId.Zero, -1, -1);

        prompt = prompt.Replace($"{{{imagePath}}}", "<image>");

        foreach (var bytes in imageBytesList)
        {
            multimodalExecutor.Images.Add(bytes);
            var consoleImage = new CanvasImage(bytes) { MaxWidth = 30 };
            AnsiConsole.Write(consoleImage);
            AnsiConsole.WriteLine();
        }
        Console.ForegroundColor = ConsoleColor.DarkGray;
        AnsiConsole.MarkupLineInterpolated($"[grey]Image loaded into shared executor. Prompt for model: {prompt.Replace("<image>", "[IMAGE_TOKEN]")}[/]");
        Console.ResetColor();

        string finalLabel = string.Empty;
        try
        {
            await foreach (var textChunk in multimodalExecutor.InferAsync(prompt, inferenceParams))
            {
                finalLabel += textChunk;
            }
        }
        catch(Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Inference failed for image {imagePath}: {ex.Message}");
            Console.ResetColor();
            return "error_inference_failed";
        }

        finalLabel = finalLabel.Trim();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Label for {Path.GetFileName(imagePath)}: '{finalLabel}'");
        Console.ResetColor();

        return string.IsNullOrWhiteSpace(finalLabel) ? "unknown_empty_label" : finalLabel;
    }
    }
}
