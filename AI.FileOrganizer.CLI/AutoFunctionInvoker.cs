using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using LLamaSharp.SemanticKernel;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace AI.FileOrganizer.CLI
{
    /// <summary>
    /// Function invoker that uses Semantic Kernel's auto function calling for models that support tooling
    /// </summary>
    public class AutoFunctionInvoker : BaseFunctionInvoker
    {
        public AutoFunctionInvoker(ModelManager modelManager) : base(modelManager)
        {
        }

        public override async Task<string> ProcessInputAsync(string userInput, Kernel kernel, IChatCompletionService chatService, CancellationToken cancellationToken = default)
        {
            var chatHistory = new ChatHistory();
            
            // Add system message for file organization context
            chatHistory.AddSystemMessage(
                """
                You are a file organization assistant. Help users organize files and folders using the available functions.
                The user may refer to directories by common names such as 'Downloads', 'Documents', or 'Desktop', or by explicit paths.
                Use the available file organization functions to complete the user's requests.
                Always ask for confirmation before performing destructive operations like moving or organizing files.
                """);

            chatHistory.AddUserMessage(userInput);

            // Configure execution settings - try auto function calling if supported
            var executionSettings = new LLamaSharpPromptExecutionSettings()
            {
                Temperature = 0.0f,
                MaxTokens = 2000
            };

            try
            {
                // Use kernel's function calling capability
                var result = await chatService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel,
                    cancellationToken);

                return result.Content ?? "No response generated.";
            }
            catch (Exception ex)
            {
                return HandleError(ex, "function calling");
            }
        }
    }
}