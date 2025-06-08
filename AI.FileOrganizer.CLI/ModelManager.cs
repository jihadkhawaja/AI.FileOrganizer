using LLama;
using LLama.Common;
using LLamaSharp.SemanticKernel;
using LLamaSharp.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.ChatCompletion;

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

        public ModelManager(string? modelPath, string? multiModalProj, int maxImageTokens)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentNullException(nameof(modelPath), "ModelPath cannot be null or empty.");

            ModelPath = modelPath;

            IsMultimodal = !string.IsNullOrWhiteSpace(multiModalProj);
            MultiModalProj = multiModalProj;

            ModelParams parameters = new(modelPath);
            var model = LLamaWeights.LoadFromFileAsync(parameters).Result;
            var context = model.CreateContext(parameters);

            Executor = new InteractiveExecutor(context);

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