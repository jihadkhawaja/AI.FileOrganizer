namespace AI.FileOrganizer.CLI
{
    /// <summary>
    /// Factory for creating the appropriate function invoker based on model capabilities
    /// </summary>
    public static class FunctionInvokerFactory
    {
        /// <summary>
        /// Creates the appropriate function invoker based on model capabilities
        /// </summary>
        /// <param name="modelManager">The model manager instance</param>
        /// <returns>The appropriate function invoker implementation</returns>
        public static IFunctionInvoker CreateInvoker(ModelManager modelManager)
        {
            if (modelManager.SupportsFunctionCalling)
            {
                Console.WriteLine("Model supports function calling - using auto function invoke approach.");
                return new AutoFunctionInvoker(modelManager);
            }
            else
            {
                Console.WriteLine("Model does not support function calling - using manual command approach.");
                return new ManualFunctionInvoker(modelManager);
            }
        }
    }
}