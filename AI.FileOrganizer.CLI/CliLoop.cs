using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;
using System.Reflection;

namespace AI.FileOrganizer.CLI
{
    public class CliLoop
    {
        private readonly ModelManager _modelManager;
        private readonly Kernel _kernel;
        private readonly IFunctionInvoker _functionInvoker;

        public CliLoop(ModelManager modelManager, Kernel kernel)
        {
            _modelManager = modelManager;
            _kernel = kernel;
            _functionInvoker = FunctionInvokerFactory.CreateInvoker(modelManager);
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
            Console.WriteLine($"Using {(_modelManager.SupportsFunctionCalling ? "auto function calling" : "manual command parsing")} approach.");
            Console.ResetColor();
            
            while (true)
            {
                Console.Write("\n> ");
                var input = Console.ReadLine();
                if (input is null || input.Trim().ToLower() == "exit")
                    break;

                try
                {
                    var result = await _functionInvoker.ProcessInputAsync(input, _kernel, _modelManager.ChatService);
                    Console.WriteLine(result);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

    }
}
