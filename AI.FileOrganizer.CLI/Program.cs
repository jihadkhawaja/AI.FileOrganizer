using AI.FileOrganizer;
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

        ThinkingLevel: "low"

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

// Register all tools (wrap destructive/mutating tools with approval requirement)
var tools = new List<AITool>
{
    AIFunctionFactory.Create(FileTools.ListFiles),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FileTools.MoveFile)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FileTools.CopyFile)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FileTools.DeleteFile)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FileTools.OrganizeByExtension)),
    AIFunctionFactory.Create(FileTools.CategorizeByNameContext),
    AIFunctionFactory.Create(FileTools.CategorizeByContentContext),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FileTools.OrganizeAllByType)),
    AIFunctionFactory.Create(FolderTools.ListFolders),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FolderTools.MoveFolder)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FolderTools.CopyFolder)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FolderTools.DeleteFolder)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FolderTools.OrganizeFoldersByNamePattern)),
    new ApprovalRequiredAIFunction(AIFunctionFactory.Create(FolderTools.OrganizeFoldersBySize)),
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

// Parse thinking level
ReasoningEffort? reasoningEffort = config["ThinkingLevel"]?.ToLowerInvariant() switch
{
    "low" => ReasoningEffort.Low,
    "high" => ReasoningEffort.High,
    _ => null
};

// Create chat client once (reused across per-request agents)
IChatClient chatClient;
try
{
    chatClient = AgentFactory.CreateChatClient(providerType, config);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Failed to create AI client: {Markup.Escape(ex.Message)}[/]");
    return;
}

// Helper to create a scoped agent with the given tool set
AIAgent CreateScopedAgent(IList<AITool> scopedTools) =>
    AgentFactory.CreateAgent(chatClient, scopedTools, instructions, chatHistoryProvider, reasoningEffort);

// Create initial agent with all tools and a session
var agent = CreateScopedAgent(tools);
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
        // Optimize tool selection based on user intent
        var (intent, selectedTools) = ToolSelector.SelectToolsForInput(input, tools);
        agent = CreateScopedAgent(selectedTools);

        if (intent != IntentType.General)
        {
            AnsiConsole.MarkupLine($"[dim]Intent: {intent} — {selectedTools.Count}/{tools.Count} tools selected[/]");
        }

        var response = await agent.RunAsync(input, session);

        // Handle tool approval requests
        var approvalRequests = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionApprovalRequestContent>()
            .ToList();

        while (approvalRequests.Count > 0)
        {
            foreach (var request in approvalRequests)
            {
                AnsiConsole.MarkupLine($"[yellow]Approval required for tool:[/] [cyan]{Markup.Escape(request.FunctionCall.Name)}[/]");
                AnsiConsole.MarkupLine($"[dim]Arguments: {Markup.Escape(request.FunctionCall.Arguments?.ToString() ?? "(none)")}[/]");

                var approved = AnsiConsole.Confirm("[yellow]Approve?[/]");
                var approvalMessage = new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);
                response = await agent.RunAsync(approvalMessage, session);
            }

            approvalRequests = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionApprovalRequestContent>()
                .ToList();
        }

        // Print final response text
        var text = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .Select(t => t.Text);
        AnsiConsole.MarkupLine($"[white]{Markup.Escape(string.Join("", text))}[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
    }
}

internal partial class Program;
