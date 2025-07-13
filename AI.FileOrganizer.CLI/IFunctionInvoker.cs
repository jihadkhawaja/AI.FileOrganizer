using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AI.FileOrganizer.CLI
{
    /// <summary>
    /// Interface for function invocation strategies
    /// </summary>
    public interface IFunctionInvoker
    {
        /// <summary>
        /// Processes user input and executes appropriate functions
        /// </summary>
        /// <param name="userInput">The user's natural language input</param>
        /// <param name="kernel">The semantic kernel instance</param>
        /// <param name="chatService">The chat completion service</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The result of processing the input</returns>
        Task<string> ProcessInputAsync(string userInput, Kernel kernel, IChatCompletionService chatService, CancellationToken cancellationToken = default);
    }
}