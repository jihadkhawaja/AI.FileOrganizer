using Microsoft.SemanticKernel;

namespace AI.FileOrganizer.CLI
{
    /// <summary>
    /// Base class containing common functionality for function invokers
    /// </summary>
    public abstract class BaseFunctionInvoker : IFunctionInvoker
    {
        protected readonly ModelManager ModelManager;

        protected BaseFunctionInvoker(ModelManager modelManager)
        {
            ModelManager = modelManager;
        }

        public abstract Task<string> ProcessInputAsync(string userInput, Kernel kernel, Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService chatService, CancellationToken cancellationToken = default);

        /// <summary>
        /// Common confirmation method
        /// </summary>
        protected static bool ConfirmAction(string actionDesc)
        {
            Console.Write($"Confirm action: {actionDesc}? (y/n): ");
            var confirm = Console.ReadLine();
            return confirm != null && confirm.Trim().ToLower() == "y";
        }

        /// <summary>
        /// Common error handling method
        /// </summary>
        protected static string HandleError(Exception ex, string context)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in {context}: {ex.Message}");
            Console.ResetColor();
            return $"Error: {ex.Message}";
        }
    }
}