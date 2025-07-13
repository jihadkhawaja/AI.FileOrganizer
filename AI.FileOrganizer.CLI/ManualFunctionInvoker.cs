using LLamaSharp.SemanticKernel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.RegularExpressions;
using LLama.Common;
using LLama;
using LLama.Sampling;
using LLama.Native;
using Spectre.Console;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace AI.FileOrganizer.CLI
{
    /// <summary>
    /// Function invoker that uses manual command parsing for models that don't support tooling
    /// </summary>
    public class ManualFunctionInvoker : BaseFunctionInvoker
    {
        private string[]? _previousFiles;
        private Dictionary<string, string>? _lastImageContextMap;

        public ManualFunctionInvoker(ModelManager modelManager) : base(modelManager)
        {
        }

        public override async Task<string> ProcessInputAsync(string userInput, Kernel kernel, IChatCompletionService chatService, CancellationToken cancellationToken = default)
        {
            // Format chat history with control tokens for command interpretation
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

            var userPrompt = $"<start_of_turn>user\n{userInput}<end_of_turn>\n<start_of_turn>model\n";

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

            var chatResult = await chatService.GetChatMessageContentAsync(
                chatHistory,
                settings,
                kernel,
                cancellationToken);

            var response = chatResult.Content?.Trim() ?? "";

            // Process the command manually
            return await ProcessManualCommandAsync(response, kernel);
        }

        private async Task<string> ProcessManualCommandAsync(string response, Kernel kernel)
        {
            try
            {
                if (response.StartsWith("[LIST FILES]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[LIST FILES]", "").Trim());
                    var result = await kernel.InvokeAsync("FileOrganizer", "ListFiles", new() { ["directory"] = dir });
                    _previousFiles = result.GetValue<string[]>() ?? Array.Empty<string>();
                    return _previousFiles.Length == 0 ? "No files found." : string.Join(Environment.NewLine, _previousFiles);
                }
                else if (response.StartsWith("[MOVE FILE]"))
                {
                    var commandargs = response.Replace("[MOVE FILE]", "").Trim().Split(' ', 2);
                    if (commandargs.Length == 2)
                    {
                        var src = commandargs[0];
                        var dest = ModelManager.ResolveDir(commandargs[1]);
                        if (ConfirmAction($"Move file '{src}' to '{dest}'"))
                        {
                            var result = await kernel.InvokeAsync("FileOrganizer", "MoveFile", new() { ["sourceFilePath"] = src, ["destinationDirectory"] = dest });
                            return result.GetValue<string>() ?? "File moved successfully.";
                        }
                        return "Action cancelled.";
                    }
                    return "Invalid move command.";
                }
                else if (response.StartsWith("[ORGANIZE FILES]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[ORGANIZE FILES]", "").Trim());
                    if (ConfirmAction($"Organize files in '{dir}' by extension"))
                    {
                        var result = await kernel.InvokeAsync("FileOrganizer", "OrganizeByExtension", new() { ["directory"] = dir });
                        return result.GetValue<string>() ?? "Files organized successfully.";
                    }
                    return "Action cancelled.";
                }
                else if (response.StartsWith("[CATEGORIZE FILES]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[CATEGORIZE FILES]", "").Trim());
                    if (ConfirmAction($"Categorize files in '{dir}' by extension"))
                    {
                        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByExtension", new() { ["directory"] = dir });
                        return result.GetValue<string>() ?? "Files categorized successfully.";
                    }
                    return "Action cancelled.";
                }
                else if (response.StartsWith("[CATEGORIZE BY NAME CONTEXT]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[CATEGORIZE BY NAME CONTEXT]", "").Trim());
                    if (ConfirmAction($"Categorize files in '{dir}' by name context"))
                    {
                        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByNameContext", new() { ["directory"] = dir });
                        return result.GetValue<string>() ?? "Files categorized by name context successfully.";
                    }
                    return "Action cancelled.";
                }
                else if (response.StartsWith("[CATEGORIZE BY CONTENT CONTEXT]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[CATEGORIZE BY CONTENT CONTEXT]", "").Trim());
                    if (ConfirmAction($"Categorize files in '{dir}' by content context"))
                    {
                        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeByContentContext", new() { ["directory"] = dir });
                        return result.GetValue<string>() ?? "Files categorized by content context successfully.";
                    }
                    return "Action cancelled.";
                }
                else if (response.StartsWith("[CATEGORIZE IMAGES BY CONTEXT]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[CATEGORIZE IMAGES BY CONTEXT]", "").Trim());
                    return await ProcessImageCategorizationAsync(dir, kernel);
                }
                else if (response.StartsWith("[COUNT PREVIOUS FILES]"))
                {
                    return _previousFiles != null ? $"There are {_previousFiles.Length} files." : "No previous file list found.";
                }
                else if (response.StartsWith("[CATEGORIZE FOLDERS BY PATTERN]"))
                {
                    return await ProcessFolderCategorizationByPatternAsync(response, kernel);
                }
                else if (response.StartsWith("[ORGANIZE FOLDERS BY PATTERN]"))
                {
                    return await ProcessFolderOrganizationByPatternAsync(response, kernel);
                }
                else if (response.StartsWith("[CATEGORIZE FOLDERS BY SIZE]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[CATEGORIZE FOLDERS BY SIZE]", "").Trim());
                    if (ConfirmAction($"Categorize folders in '{dir}' by size"))
                    {
                        var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeFoldersBySize", new() { ["directory"] = dir });
                        return result.GetValue<string>() ?? "Folders categorized by size successfully.";
                    }
                    return "Action cancelled.";
                }
                else if (response.StartsWith("[ORGANIZE FOLDERS BY SIZE]"))
                {
                    return await ProcessFolderOrganizationBySizeAsync(response, kernel);
                }
                else if (response.StartsWith("[ORGANIZE IMAGES BY CONTEXT]"))
                {
                    var dir = ModelManager.ResolveDir(response.Replace("[ORGANIZE IMAGES BY CONTEXT]", "").Trim());
                    if (ModelManager.IsMultimodal && _lastImageContextMap != null && _lastImageContextMap.Count > 0)
                    {
                        if (ConfirmAction($"Organize images in '{dir}' by last detected context labels"))
                        {
                            var organizeResult = await kernel.InvokeAsync("FileOrganizer", "OrganizeImagesByContext", 
                                new() { ["directory"] = dir, ["imageContextMap"] = _lastImageContextMap });
                            return organizeResult.GetValue<string>() ?? "Images organized successfully.";
                        }
                        return "Action cancelled.";
                    }
                    return "No image context map available. Please run image categorization first.";
                }
                else
                {
                    return "Unrecognized command: " + response;
                }
            }
            catch (Exception ex)
            {
                return HandleError(ex, "command processing");
            }
        }

        private async Task<string> ProcessImageCategorizationAsync(string dir, Kernel kernel)
        {
            _lastImageContextMap = null;
            if (!ConfirmAction($"Categorize images in '{dir}' by context"))
                return "Action cancelled.";

            var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeImagesByContext", 
                new() { ["directory"] = dir, ["isMultimodal"] = ModelManager.IsMultimodal });
            var output = result.GetValue<string>() ?? "";

            if (ModelManager.IsMultimodal && ModelManager.Executor != null)
            {
                var imagePaths = Regex.Matches(output, @"[^\s]+(\.jpg|\.jpeg|\.png|\.bmp|\.gif|\.webp|\.tiff)", RegexOptions.IgnoreCase)
                                      .Select(m => m.Value)
                                      .ToList();

                if (imagePaths.Count > 0)
                {
                    var imageContextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var imagePath in imagePaths)
                    {
                        var label = await ProcessSingleImageAsync(imagePath);
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            imageContextMap[imagePath] = label;
                        }
                    }

                    _lastImageContextMap = imageContextMap;

                    if (imageContextMap.Count > 0 && ConfirmAction($"Organize images in '{dir}' by detected context labels"))
                    {
                        var organizeResult = await kernel.InvokeAsync("FileOrganizer", "OrganizeImagesByContext", 
                            new() { ["directory"] = dir, ["imageContextMap"] = imageContextMap });
                        return organizeResult.GetValue<string>() ?? "Images organized successfully.";
                    }
                    return "No valid labels found or action cancelled.";
                }
            }

            return output;
        }

        private async Task<string> ProcessFolderCategorizationByPatternAsync(string response, Kernel kernel)
        {
            var args = response.Replace("[CATEGORIZE FOLDERS BY PATTERN]", "").Trim().Split(' ', 2);
            if (args.Length == 2)
            {
                var dir = ModelManager.ResolveDir(args[0]);
                var pattern = args[1];
                if (ConfirmAction($"Categorize folders in '{dir}' by pattern '{pattern}'"))
                {
                    var result = await kernel.InvokeAsync("FileOrganizer", "CategorizeFoldersByNamePattern", 
                        new() { ["directory"] = dir, ["pattern"] = pattern });
                    return result.GetValue<string>() ?? "Folders categorized successfully.";
                }
                return "Action cancelled.";
            }
            return "Invalid categorize folders by pattern command.";
        }

        private async Task<string> ProcessFolderOrganizationByPatternAsync(string response, Kernel kernel)
        {
            var args = response.Replace("[ORGANIZE FOLDERS BY PATTERN]", "").Trim().Split(' ', 2);
            if (args.Length == 2)
            {
                var dir = ModelManager.ResolveDir(args[0]);
                var pattern = args[1];
                if (ConfirmAction($"Organize folders in '{dir}' by pattern '{pattern}'"))
                {
                    var result = await kernel.InvokeAsync("FileOrganizer", "OrganizeFoldersByNamePattern", 
                        new() { ["directory"] = dir, ["pattern"] = pattern });
                    return result.GetValue<string>() ?? "Folders organized successfully.";
                }
                return "Action cancelled.";
            }
            return "Invalid organize folders by pattern command.";
        }

        private async Task<string> ProcessFolderOrganizationBySizeAsync(string response, Kernel kernel)
        {
            var args = response.Replace("[ORGANIZE FOLDERS BY SIZE]", "").Trim().Split(' ', 3);
            if (args.Length >= 1)
            {
                var dir = ModelManager.ResolveDir(args[0]);
                int small = 5, large = 20;
                if (args.Length >= 2) int.TryParse(args[1], out small);
                if (args.Length == 3) int.TryParse(args[2], out large);
                if (ConfirmAction($"Organize folders in '{dir}' by size (small: {small}, large: {large})"))
                {
                    var result = await kernel.InvokeAsync("FileOrganizer", "OrganizeFoldersBySize", 
                        new() { ["directory"] = dir, ["smallThreshold"] = small, ["largeThreshold"] = large });
                    return result.GetValue<string>() ?? "Folders organized successfully.";
                }
                return "Action cancelled.";
            }
            return "Invalid organize folders by size command.";
        }



        private async Task<string> ProcessSingleImageAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"Image not found: {imagePath}");
                return "unknown";
            }

            // If not multimodal, use simple pattern-based categorization
            if (!ModelManager.IsMultimodal || string.IsNullOrEmpty(ModelManager.MultiModalProj))
            {
                var fileName = Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant();
                
                if (fileName.Contains("screenshot") || fileName.Contains("screen"))
                    return "screenshot";
                else if (fileName.Contains("photo") || fileName.Contains("img"))
                    return "photo";
                else if (fileName.Contains("document") || fileName.Contains("doc"))
                    return "document";
                else
                    return "image";
            }

            // Use multimodal processing for sophisticated image analysis
            const int maxTokens = 16;
            var prompt = $"{{{imagePath}}}\nUSER:\nProvide a single short label (e.g. 'animal', 'invoice', 'text', 'nature', 'person', 'screenshot', 'art', 'other') describing the main content of the image. Only output the label.\nASSISTANT:\n";

            try
            {
                // Llava Init
                using var clipModel = await LLavaWeights.LoadFromFileAsync(ModelManager.MultiModalProj!);
                var ex = new InteractiveExecutor(ModelManager.Executor.Context, clipModel);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Processing image with multimodal model. Max tokens: {0}, Context size: {1}.", maxTokens, ex.Context.ContextSize);

                var inferenceParams = new InferenceParams
                {
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = 0.1f
                    },
                    AntiPrompts = new List<string> { "\nUSER:" },
                    MaxTokens = maxTokens
                };

                var imageMatches = Regex.Matches(prompt, "{([^}]*)}").Select(m => m.Value);
                var imageCount = imageMatches.Count();
                var hasImages = imageCount > 0;

                if (hasImages)
                {
                    var imagePathsWithCurlyBraces = Regex.Matches(prompt, "{([^}]*)}").Select(m => m.Value);
                    var imagePaths = Regex.Matches(prompt, "{([^}]*)}").Select(m => m.Groups[1].Value).ToList();

                    List<byte[]> imageBytes;
                    try
                    {
                        imageBytes = imagePaths.Select(File.ReadAllBytes).ToList();
                    }
                    catch (IOException exception)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Could not load image: {exception.Message}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        return "unknown";
                    }

                    ex.Context.NativeHandle.KvCacheRemove(LLamaSeqId.Zero, -1, -1);

                    int index = 0;
                    foreach (var path in imagePathsWithCurlyBraces)
                    {
                        // First image replace to tag <image, the rest of the images delete the tag
                        prompt = prompt.Replace(path, index++ == 0 ? "<image>" : "");
                    }

                    foreach (var consoleImage in imageBytes?.Select(bytes => new CanvasImage(bytes)) ?? Array.Empty<CanvasImage>())
                    {
                        consoleImage.MaxWidth = 50;
                        AnsiConsole.Write(consoleImage);
                    }

                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("The images were scaled down for the console only, the model gets full versions.");
                    Console.WriteLine();

                    ex.Images.Clear();
                    foreach (var image in imagePaths)
                    {
                        ex.Images.Add(await File.ReadAllBytesAsync(image));
                    }
                }

                Console.ForegroundColor = ConsoleColor.White;
                string finalLabel = string.Empty;
                await foreach (var text in ex.InferAsync(prompt, inferenceParams))
                {
                    finalLabel += text;
                    Console.Write(text);
                }

                Console.WriteLine($"Label: {finalLabel}");
                return finalLabel.Trim();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error processing image: {ex.Message}");
                Console.ResetColor();
                return "unknown";
            }
        }
    }
}