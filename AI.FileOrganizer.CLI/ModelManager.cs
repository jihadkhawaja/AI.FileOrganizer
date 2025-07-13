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
        public bool SupportsFunctionCalling { get; private set; }
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

            // Detect if model supports function calling based on model name/path
            SupportsFunctionCalling = DetectFunctionCallingSupport(modelPath);

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

        /// <summary>
        /// Detects if the model supports function calling based on model characteristics
        /// </summary>
        /// <param name="modelPath">Path to the model file</param>
        /// <returns>True if the model likely supports function calling, false otherwise</returns>
        private bool DetectFunctionCallingSupport(string modelPath)
        {
            var modelFileName = Path.GetFileName(modelPath).ToLowerInvariant();
            
            // For now, most local GGUF models don't support OpenAI-style function calling
            // This could be enhanced to check model metadata or test capabilities
            
            // Models known to support function calling (extend this list as needed)
            var functionCallingSupportedModels = new[]
            {
                "gpt-3.5-turbo",
                "gpt-4",
                "mistral-7b-instruct-v0.3", // Some versions support function calling
                "hermes-2", // Some variants support function calling
                "openhermes"
            };

            // Check if model name contains any known function calling model identifiers
            foreach (var supportedModel in functionCallingSupportedModels)
            {
                if (modelFileName.Contains(supportedModel.Replace("-", "").Replace(".", "")))
                {
                    return true;
                }
            }

            // For local GGUF models, default to false for now
            // This could be enhanced with actual capability testing
            return false;
        }
    }
}