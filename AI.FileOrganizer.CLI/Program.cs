using AI.FileOrganizer;
using AI.FileOrganizer.CLI;
using LLama.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

Console.WriteLine("Starting AI File Organizer CLI...");

// Configure logging. Change this to `true` to see log messages from llama.cpp
var showLLamaCppLogs = false;
NativeLibraryConfig
   .All
   .WithLogCallback((level, message) =>
   {
       if (showLLamaCppLogs)
           Console.WriteLine($"[llama {level}]: {message.TrimEnd('\n')}");
   });

// Configure native library to use. This must be done before any other llama.cpp methods are called!
NativeLibraryConfig
   .All
   .WithCuda()
   .WithVulkan();

// Calling this method forces loading to occur now.
NativeApi.llama_empty_call();

// Load configuration from user secrets
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

string? modelPath = config["ModelPath"];
string? multiModalProj = config["MultiModalProj"];
const int maxImageTokens = 1024;

// Model and executor setup
var modelManager = new ModelManager(modelPath, multiModalProj, maxImageTokens);

// Register plugin and kernel
var organize = new Organize();
var plugins = new KernelPluginCollection();
plugins.AddFromObject(organize, "FileOrganizer");
var kernel = new Kernel(plugins: plugins);

// CLI loop
Console.Clear();
var cli = new CliLoop(modelManager, kernel);
cli.ListAvailableFunctions();
await cli.RunAsync();