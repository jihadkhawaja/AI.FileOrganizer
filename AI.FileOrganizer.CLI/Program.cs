using AI.FileOrganizer.CLI.Providers;
using AI.FileOrganizer.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

// Load configuration
const string configFileName = "config.yaml";
if (!File.Exists(configFileName))
{
    File.WriteAllText(configFileName, """
        OpenAI:
          ApiKey: ""
          Model: "gpt-4o-mini"

        Anthropic:
          ApiKey: ""
          Model: "claude-haiku-4-5"

        OpenAICompatible:
          Endpoint: "http://localhost:1234/v1"
          ApiKey: "lm-studio"
          Model: "default"

        PersistMemory: true
        """.Replace("        ", ""));

    AnsiConsole.MarkupLine($"[yellow]Generated default {configFileName} — fill in your API keys.[/]");
}

var config = new ConfigurationBuilder()
    .AddYamlFile(configFileName, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Provider selection
var providerChoice = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("[yellow]Select AI Provider:[/]")
        .AddChoices("OpenAI", "Anthropic", "OpenAI-Compatible (LM Studio, etc.)"));

var providerType = providerChoice switch
{
    "OpenAI" => ProviderType.OpenAI,
    "Anthropic" => ProviderType.Anthropic,
    "OpenAI-Compatible (LM Studio, etc.)" => ProviderType.OpenAICompatible,
    _ => throw new InvalidOperationException("Unknown provider")
};

// Register all tools
var tools = new List<AITool>
{
    AIFunctionFactory.Create(FileTools.ListFiles),
    AIFunctionFactory.Create(FileTools.MoveFile),
    AIFunctionFactory.Create(FileTools.OrganizeByExtension),
    AIFunctionFactory.Create(FileTools.CategorizeByNameContext),
    AIFunctionFactory.Create(FileTools.CategorizeByContentContext),
    AIFunctionFactory.Create(FileTools.OrganizeAllByType),
    AIFunctionFactory.Create(FolderTools.ListFolders),
    AIFunctionFactory.Create(FolderTools.MoveFolder),
    AIFunctionFactory.Create(FolderTools.CopyFolder),
    AIFunctionFactory.Create(FolderTools.DeleteFolder),
    AIFunctionFactory.Create(FolderTools.OrganizeFoldersByNamePattern),
    AIFunctionFactory.Create(FolderTools.OrganizeFoldersBySize),
};

var instructions = $"""
    You are a file organization assistant. Help users organize files and folders using the available tools.
    The user may refer to directories by common names such as 'Downloads', 'Documents', or 'Desktop'.
    Resolve these to their full paths on the current system:
    - Downloads: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")}
    - Documents: {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
    - Desktop: {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}
    Use the available tools to complete the user's requests.
    Always describe what you're about to do before executing file operations.
    """;

// Create chat history provider based on config
var persistMemory = !string.Equals(config["PersistMemory"], "false", StringComparison.OrdinalIgnoreCase);
ChatHistoryProvider chatHistoryProvider = persistMemory
    ? new FileChatHistoryProvider()
    : new InMemoryChatHistoryProvider();

// Create agent
AIAgent agent;
try
{
    agent = AgentFactory.Create(providerType, config, tools, instructions, chatHistoryProvider);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Failed to create AI agent: {Markup.Escape(ex.Message)}[/]");
    return;
}

// Create a session for persistent context across runs
var session = await agent.CreateSessionAsync();

// Display available tools
AnsiConsole.MarkupLine("[yellow]Available File Organizer Tools:[/]");
foreach (var tool in tools.OfType<AIFunction>())
{
    AnsiConsole.MarkupLine($"[cyan]  - {tool.Name}: {tool.Description}[/]");
}
AnsiConsole.WriteLine();

// Chat loop
AnsiConsole.MarkupLine($"[yellow]File Organizer CLI — Provider: {providerChoice}[/]");
if (chatHistoryProvider is FileChatHistoryProvider fileChatHistory)
{
    AnsiConsole.MarkupLine($"[dim]Chat history: {fileChatHistory.FilePath}[/]");
}
else
{
    AnsiConsole.MarkupLine("[dim]Chat history: in-memory (session only)[/]");
}
AnsiConsole.MarkupLine("[dim]Type 'exit' to quit, 'clear' to clear memory[/]");

while (true)
{
    var input = AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/]").AllowEmpty());
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        if (chatHistoryProvider is FileChatHistoryProvider fileHistory)
            fileHistory.Clear();
        session = await agent.CreateSessionAsync();
        AnsiConsole.MarkupLine("[yellow]Memory cleared.[/]");
        continue;
    }

    try
    {
        await foreach (var update in agent.RunStreamingAsync(input, session))
        {
            AnsiConsole.Markup($"[white]{Markup.Escape(update.ToString())}[/]");
        }
        AnsiConsole.WriteLine();
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
    }
}

internal partial class Program;
