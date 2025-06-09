using LLama;
using LLama.Common;
using LLama.Native;
using LLamaSharp.SemanticKernel;
using LLamaSharp.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.ChatCompletion;
using System.IO;

namespace AI.FileOrganizer.CLI
{
    public class ModelManager
    {
        public bool IsMultimodal { get; private set; }
        public InteractiveExecutor Executor { get; private set; } = null!;
        public IChatCompletionService ChatService { get; private set; } = null!;
        public Dictionary<string, string> KnownDirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string ModelPath { get; }
        public string? MultiModalProj { get; }

        private LLavaWeights? _clipModel;
        private InteractiveExecutor? _multimodalExecutor;

        public LLavaWeights? ClipModel => _clipModel;
        public InteractiveExecutor? MultimodalExecutor => _multimodalExecutor;

        public ModelManager(string? modelPath, string? multiModalProj, int maxImageTokens)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentNullException(nameof(modelPath), "ModelPath cannot be null or empty.");

            ModelPath = modelPath;
            MultiModalProj = multiModalProj;

            IsMultimodal = !string.IsNullOrWhiteSpace(MultiModalProj);

            if (IsMultimodal)
            {
                if (string.IsNullOrWhiteSpace(MultiModalProj))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[Error] Multimodal project path (MultiModalProj) is null or empty, but multimodal mode was implied.");
                    Console.ResetColor();
                    IsMultimodal = false;
                }
                else if (!File.Exists(MultiModalProj))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] Multimodal project file not found at: {MultiModalProj}");
                    Console.ResetColor();
                    IsMultimodal = false;
                }
            }

            ModelParams parameters = new(ModelPath);
            var model = LLamaWeights.LoadFromFileAsync(parameters).Result;
            var context = model.CreateContext(parameters);

            Executor = new InteractiveExecutor(context);

            if (IsMultimodal)
            {
                try
                {
                    _clipModel = LLavaWeights.LoadFromFileAsync(MultiModalProj!).Result;
                    _multimodalExecutor = new InteractiveExecutor(context, _clipModel);

                    _multimodalExecutor.Images.Clear();
                    _multimodalExecutor.Context.NativeHandle.KvCacheRemove(LLamaSeqId.Zero, -1, -1);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Info] Multimodal executor initialized successfully using CLIP model: {MultiModalProj}");
                    Console.ResetColor();
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] Failed to initialize multimodal components (CLIP model or executor): {e.Message}");
                    Console.ResetColor();
                    IsMultimodal = false;
                    _clipModel = null;
                    _multimodalExecutor = null;
                }
            }

            if (!IsMultimodal && !string.IsNullOrWhiteSpace(MultiModalProj))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Warning] Multimodal processing was requested but could not be initialized. Operations will proceed in non-multimodal mode.");
                Console.ResetColor();
            }

            ChatService = new LLamaSharpChatCompletion(
                Executor,
                new LLamaSharpPromptExecutionSettings
                {
                    MaxTokens = IsMultimodal ? maxImageTokens : -1,
                    Temperature = 0,
                    TopP = 0.1,
                });

            KnownDirs["downloads"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            KnownDirs["documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            KnownDirs["desktop"] = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        public string ResolveDir(string dir)
        {
            var cleaned = dir.Trim('"', ' ', '\\', '/');
            return KnownDirs.TryGetValue(cleaned, out var mapped) ? mapped : dir.Trim();
        }
    }
}