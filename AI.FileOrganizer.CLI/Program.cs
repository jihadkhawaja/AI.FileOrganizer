using AI.FileOrganizer;
using AI.FileOrganizer.CLI;
using AI.FileOrganizer.CLI.Providers;
using AI.FileOrganizer.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Reflection;

var configFilePath = ResolveConfigFilePath();
EnsureConfigExists(configFilePath);

var config = new ConfigurationBuilder()
    .AddYamlFile(configFilePath, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

CommandLineOptions commandLine;
try
{
    commandLine = CommandLineOptions.Parse(args);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    PrintUsage();
    return;
}

var tools = CreateTools();
var instructions = BuildInstructions();

if (commandLine.ShowHelp)
{
    PrintUsage();
    return;
}

if (commandLine.ListJobs)
{
    ListConfiguredJobs(config);
    return;
}

if (commandLine.JobTemplateName is not null)
{
    PrintJobTemplate(commandLine.JobTemplateName, config);
    return;
}

if (commandLine.TaskCommandJobName is not null)
{
    PrintTaskSchedulerCommand(commandLine.TaskCommandJobName, config);
    return;
}

if (commandLine.CreateJobInteractively)
{
    CreateJobInteractively(configFilePath, config);
    return;
}

if (commandLine.JobName is not null)
{
    Environment.ExitCode = await RunScheduledJobAsync(commandLine.JobName, config, tools, instructions);
    return;
}

await RunInteractiveAsync(config, tools, instructions);

static async Task RunInteractiveAsync(IConfiguration config, IList<AITool> tools, string instructions)
{
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

    var persistMemory = !string.Equals(config["PersistMemory"], "false", StringComparison.OrdinalIgnoreCase);
    ChatHistoryProvider chatHistoryProvider = persistMemory
        ? new FileChatHistoryProvider()
        : new InMemoryChatHistoryProvider();

    var reasoningEffort = ParseReasoningEffort(config["ThinkingLevel"]);

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

    AIAgent CreateScopedAgent(IList<AITool> scopedTools) =>
        AgentFactory.CreateAgent(chatClient, scopedTools, instructions, chatHistoryProvider, reasoningEffort);

    var agent = CreateScopedAgent(tools);
    var session = await agent.CreateSessionAsync();

    AnsiConsole.MarkupLine("[yellow]Available File Organizer Tools:[/]");
    foreach (var tool in tools.OfType<AIFunction>())
    {
        AnsiConsole.MarkupLine($"[cyan]  - {tool.Name}: {tool.Description}[/]");
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[yellow]File Organizer CLI - Provider: {providerChoice}[/]");
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
        {
            break;
        }

        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (chatHistoryProvider is FileChatHistoryProvider fileHistory)
            {
                fileHistory.Clear();
            }

            session = await agent.CreateSessionAsync();
            AnsiConsole.MarkupLine("[yellow]Memory cleared.[/]");
            continue;
        }

        try
        {
            var (intent, selectedTools) = ToolSelector.SelectToolsForInput(input, tools);
            agent = CreateScopedAgent(selectedTools);

            if (intent != IntentType.General)
            {
                AnsiConsole.MarkupLine($"[dim]Intent: {intent} - {selectedTools.Count}/{tools.Count} tools selected[/]");
            }

            var response = await agent.RunAsync(input, session);
            response = await HandleApprovalRequestsAsync(agent, session, response, autoApprove: false, interactivePrompt: true);
            WriteFinalResponse(response);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

static async Task<int> RunScheduledJobAsync(string jobName, IConfiguration config, IList<AITool> tools, string instructions)
{
    var job = ScheduledJobDefinition.Find(config, jobName);
    if (job is null)
    {
        AnsiConsole.MarkupLine($"[red]Scheduled job '{Markup.Escape(jobName)}' was not found in config.yaml.[/]");
        AnsiConsole.MarkupLine("[dim]Use '--list-jobs' to inspect configured jobs.[/]");
        return 1;
    }

    if (!job.Enabled)
    {
        AnsiConsole.MarkupLine($"[yellow]Scheduled job '{Markup.Escape(job.Name)}' is disabled.[/]");
        return 0;
    }

    var providerType = job.Provider ?? ProviderType.OpenAI;
    var reasoningEffort = ParseReasoningEffort(job.ThinkingLevel ?? config["ThinkingLevel"]);
    ChatHistoryProvider chatHistoryProvider = job.PersistMemory
        ? new FileChatHistoryProvider(GetScheduledHistoryPath(job.Name))
        : new InMemoryChatHistoryProvider();

    IChatClient chatClient;
    try
    {
        chatClient = AgentFactory.CreateChatClient(providerType, config);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to create AI client: {Markup.Escape(ex.Message)}[/]");
        return 1;
    }

    try
    {
        var (intent, selectedTools) = ToolSelector.SelectToolsForInput(job.Prompt, tools);
        var agent = AgentFactory.CreateAgent(chatClient, selectedTools, instructions, chatHistoryProvider, reasoningEffort);
        var session = await agent.CreateSessionAsync();

        AnsiConsole.MarkupLine($"[yellow]Running scheduled job:[/] [cyan]{Markup.Escape(job.Name)}[/]");
        AnsiConsole.MarkupLine($"[dim]Provider: {providerType}[/]");
        if (intent != IntentType.General)
        {
            AnsiConsole.MarkupLine($"[dim]Intent: {intent} - {selectedTools.Count}/{tools.Count} tools selected[/]");
        }

        var response = await agent.RunAsync(job.Prompt, session);
        response = await HandleApprovalRequestsAsync(agent, session, response, job.AutoApprove, interactivePrompt: false);
        WriteFinalResponse(response);
        return 0;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Scheduled job failed: {Markup.Escape(ex.Message)}[/]");
        return 1;
    }
}

static async Task<AgentResponse> HandleApprovalRequestsAsync(AIAgent agent, AgentSession session, AgentResponse response, bool autoApprove, bool interactivePrompt)
{
    var approvalRequests = GetApprovalRequests(response);

    while (approvalRequests.Count > 0)
    {
        foreach (var request in approvalRequests)
        {
            var functionName = request.FunctionCall.Name;
            var arguments = request.FunctionCall.Arguments?.ToString() ?? "(none)";

            if (!autoApprove && !interactivePrompt)
            {
                throw new InvalidOperationException(
                    $"Scheduled job requires approval for '{functionName}'. Set AutoApprove: true for this job if unattended execution is intended.");
            }

            var approved = autoApprove;
            if (interactivePrompt)
            {
                AnsiConsole.MarkupLine($"[yellow]Approval required for tool:[/] [cyan]{Markup.Escape(functionName)}[/]");
                AnsiConsole.MarkupLine($"[dim]Arguments: {Markup.Escape(arguments)}[/]");
                approved = AnsiConsole.Confirm("[yellow]Approve?[/]");
            }

            var approvalMessage = new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);
            response = await agent.RunAsync(approvalMessage, session);
        }

        approvalRequests = GetApprovalRequests(response);
    }

    return response;
}

static List<FunctionApprovalRequestContent> GetApprovalRequests(AgentResponse response)
{
    return response.Messages
        .SelectMany(message => message.Contents)
        .OfType<FunctionApprovalRequestContent>()
        .ToList();
}

static void WriteFinalResponse(AgentResponse response)
{
    var text = response.Messages
        .SelectMany(message => message.Contents)
        .OfType<TextContent>()
        .Select(content => content.Text);

    AnsiConsole.MarkupLine($"[white]{Markup.Escape(string.Join(string.Empty, text))}[/]");
}

static IList<AITool> CreateTools()
{
    return
    [
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
    ];
}

static string BuildInstructions()
{
    return $"""
        You are a file organization assistant. Help users organize files and folders using the available tools.
        The user may refer to directories by common names such as 'Downloads', 'Documents', or 'Desktop'.
        Resolve these to their full paths on the current system:
        - Downloads: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")}
        - Documents: {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
        - Desktop: {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}
        Use the available tools to complete the user's requests.
        Always describe what you're about to do before executing file operations.
        """;
}

static void EnsureConfigExists(string filePath)
{
    if (File.Exists(filePath))
    {
        return;
    }

    File.WriteAllText(filePath, """
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

        Jobs: []
        """.Replace("        ", string.Empty));

    AnsiConsole.MarkupLine($"[yellow]Generated default {Path.GetFileName(filePath)} - fill in your API keys.[/]");
}

static ReasoningEffort? ParseReasoningEffort(string? value)
{
    return value?.ToLowerInvariant() switch
    {
        "low" => ReasoningEffort.Low,
        "high" => ReasoningEffort.High,
        _ => null
    };
}

static string GetScheduledHistoryPath(string jobName)
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AI.FileOrganizer",
        "jobs");

    Directory.CreateDirectory(directory);

    var safeName = string.Concat(jobName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    return Path.Combine(directory, $"{safeName}.json");
}

static void ListConfiguredJobs(IConfiguration config)
{
    var jobs = ScheduledJobDefinition.Load(config);
    if (jobs.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No scheduled jobs are configured.[/]");
        return;
    }

    var table = new Table().AddColumns("Name", "Provider", "Enabled", "AutoApprove", "Schedule");
    foreach (var job in jobs.OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase))
    {
        table.AddRow(
            job.Name,
            (job.Provider ?? ProviderType.OpenAI).ToString(),
            job.Enabled ? "Yes" : "No",
            job.AutoApprove ? "Yes" : "No",
            string.IsNullOrWhiteSpace(job.Schedule) ? "-" : job.Schedule);
    }

    AnsiConsole.Write(table);
}

static void PrintUsage()
{
    AnsiConsole.MarkupLine("[yellow]Usage:[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --job <name>[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --list-jobs[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --create-job[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --job-template <name>[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --task-command <name>[/]");
    AnsiConsole.MarkupLine("  [cyan]AI.FileOrganizer.CLI --help[/]");
}

static void CreateJobInteractively(string configFilePath, IConfiguration config)
{
    var existingJobs = ScheduledJobDefinition.Load(config);
    var defaultThinkingLevel = config["ThinkingLevel"] ?? "low";

    AnsiConsole.MarkupLine("[yellow]Create Scheduled Job[/]");
    AnsiConsole.MarkupLine($"[dim]Config file: {Markup.Escape(configFilePath)}[/]");

    var jobName = PromptForRequiredText(
        "[green]Job name[/]",
        value => existingJobs.Any(job => string.Equals(job.Name, value, StringComparison.OrdinalIgnoreCase))
            ? "A job with that name already exists."
            : null);

    var prompt = PromptForRequiredText("[green]Job prompt[/]");
    var provider = PromptForProvider();
    var autoApprove = PromptForConfirmation("[green]Auto-approve file operations?[/]", false);
    var persistMemory = PromptForConfirmation("[green]Persist job chat history?[/]", false);
    var thinkingLevel = PromptForThinkingLevel(defaultThinkingLevel);
    var schedule = PromptForOptionalText("[green]Schedule description[/] [dim](optional)[/]");
    var enabled = PromptForConfirmation("[green]Enable this job now?[/]", true);

    var job = new ScheduledJobDefinition(
        jobName,
        prompt,
        provider,
        thinkingLevel,
        autoApprove,
        persistMemory,
        string.IsNullOrWhiteSpace(schedule) ? null : schedule,
        enabled);

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]Job preview[/]");
    AnsiConsole.WriteLine(string.Join(Environment.NewLine, job.ToYamlLines()));
    AnsiConsole.WriteLine();

    if (!PromptForConfirmation("[green]Write this job to config.yaml?[/]", true))
    {
        AnsiConsole.MarkupLine("[yellow]Job creation cancelled.[/]");
        return;
    }

    SaveScheduledJob(configFilePath, existingJobs, job);
    AnsiConsole.MarkupLine($"[green]Saved scheduled job '{Markup.Escape(job.Name)}'.[/]");
}

static void PrintJobTemplate(string jobName, IConfiguration config)
{
    var thinkingLevel = config["ThinkingLevel"] ?? "low";

    var lines = new[]
    {
        "Append this under Jobs:",
        "",
        $"- Name: \"{EscapeYaml(jobName)}\"",
        "  Prompt: \"Describe what this scheduled job should do\"",
        "  Provider: \"OpenAI\"",
        "  AutoApprove: false",
        "  PersistMemory: false",
        $"  ThinkingLevel: \"{EscapeYaml(thinkingLevel)}\"",
        "  Schedule: \"Daily at midnight\"",
        "  Enabled: true"
    };

    AnsiConsole.WriteLine(string.Join(Environment.NewLine, lines));
}

static void SaveScheduledJob(string configFilePath, IReadOnlyList<ScheduledJobDefinition> existingJobs, ScheduledJobDefinition newJob)
{
    var updatedJobs = existingJobs.ToList();
    updatedJobs.Add(newJob);

    var newJobsSection = BuildJobsSection(updatedJobs);
    var lines = File.ReadAllLines(configFilePath).ToList();
    var jobsIndex = lines.FindIndex(line => string.Equals(line.Trim(), "Jobs:") || string.Equals(line.Trim(), "Jobs: []"));

    if (jobsIndex < 0)
    {
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }

        lines.AddRange(newJobsSection);
    }
    else
    {
        var endIndex = jobsIndex + 1;
        while (endIndex < lines.Count)
        {
            var currentLine = lines[endIndex];
            if (!string.IsNullOrWhiteSpace(currentLine) && !char.IsWhiteSpace(currentLine[0]))
            {
                break;
            }

            endIndex++;
        }

        lines.RemoveRange(jobsIndex, endIndex - jobsIndex);
        lines.InsertRange(jobsIndex, newJobsSection);
    }

    File.WriteAllText(configFilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
}

static List<string> BuildJobsSection(IReadOnlyList<ScheduledJobDefinition> jobs)
{
    var lines = new List<string>();
    if (jobs.Count == 0)
    {
        lines.Add("Jobs: []");
        return lines;
    }

    lines.Add("Jobs:");
    foreach (var job in jobs)
    {
        lines.AddRange(job.ToYamlLines(2));
    }

    return lines;
}

static void PrintTaskSchedulerCommand(string jobName, IConfiguration config)
{
    var configuredJob = ScheduledJobDefinition.Find(config, jobName);
    var action = BuildTaskSchedulerAction(jobName);

    if (configuredJob is null)
    {
        AnsiConsole.MarkupLine($"[yellow]Job '{Markup.Escape(jobName)}' is not currently defined in config.yaml.[/]");
        AnsiConsole.MarkupLine("[dim]You can still create the task now and add the job definition later.[/]");
    }

    AnsiConsole.MarkupLine("[yellow]Windows Task Scheduler action[/]");
    AnsiConsole.MarkupLine($"[cyan]Program/script:[/] {Markup.Escape(action.ProgramPath)}");
    AnsiConsole.MarkupLine($"[cyan]Add arguments:[/] {Markup.Escape(action.Arguments)}");
    AnsiConsole.MarkupLine($"[cyan]Start in:[/] {Markup.Escape(action.WorkingDirectory)}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]PowerShell equivalent[/]");
    AnsiConsole.WriteLine($"& '{action.ProgramPath}' {action.PowerShellArguments}");
}

static TaskSchedulerAction BuildTaskSchedulerAction(string jobName)
{
    var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
    if (string.IsNullOrWhiteSpace(entryAssemblyPath))
    {
        throw new InvalidOperationException("Unable to resolve the CLI assembly path for Task Scheduler output.");
    }

    var workingDirectory = AppContext.BaseDirectory;
    var windowsExecutablePath = Path.ChangeExtension(entryAssemblyPath, ".exe");
    if (!string.IsNullOrWhiteSpace(windowsExecutablePath) && File.Exists(windowsExecutablePath))
    {
        return new TaskSchedulerAction(
            windowsExecutablePath,
            $"--job \"{jobName}\"",
            workingDirectory,
            $"--job '{jobName.Replace("'", "''")}'");
    }

    return new TaskSchedulerAction(
        "dotnet",
        $"\"{entryAssemblyPath}\" --job \"{jobName}\"",
        workingDirectory,
        $"'{entryAssemblyPath.Replace("'", "''")}' --job '{jobName.Replace("'", "''")}'");
}

static string EscapeYaml(string value)
{
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

static string ResolveConfigFilePath()
{
    var appBaseConfigPath = Path.Combine(AppContext.BaseDirectory, "config.yaml");
    var projectConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config.yaml"));
    var projectFilePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AI.FileOrganizer.CLI.csproj"));

    return File.Exists(projectConfigPath) && File.Exists(projectFilePath)
        ? projectConfigPath
        : appBaseConfigPath;
}

static string PromptForRequiredText(string prompt, Func<string, string?>? validation = null)
{
    while (true)
    {
        var value = PromptForText(prompt).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            AnsiConsole.MarkupLine("[red]A value is required.[/]");
            continue;
        }

        var error = validation?.Invoke(value);
        if (error is null)
        {
            return value;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
    }
}

static string PromptForOptionalText(string prompt)
{
    return PromptForText(prompt).Trim();
}

static string PromptForText(string prompt)
{
    if (!Console.IsInputRedirected)
    {
        return AnsiConsole.Prompt(new TextPrompt<string>($"{prompt}: ").AllowEmpty());
    }

    AnsiConsole.Markup($"{prompt}: ");
    return Console.ReadLine() ?? string.Empty;
}

static bool PromptForConfirmation(string prompt, bool defaultValue)
{
    if (!Console.IsInputRedirected)
    {
        return AnsiConsole.Confirm(prompt, defaultValue);
    }

    while (true)
    {
        var defaultLabel = defaultValue ? "Y/n" : "y/N";
        AnsiConsole.Markup($"{prompt} [dim]({defaultLabel})[/]: ");
        var input = (Console.ReadLine() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        switch (input.ToLowerInvariant())
        {
            case "y":
            case "yes":
                return true;
            case "n":
            case "no":
                return false;
            default:
                AnsiConsole.MarkupLine("[red]Enter y or n.[/]");
                break;
        }
    }
}

static ProviderType PromptForProvider()
{
    while (true)
    {
        var value = PromptForOptionalText("[green]Provider[/] [dim](OpenAI, Anthropic, OpenAICompatible; default: OpenAI)[/]");
        if (string.IsNullOrWhiteSpace(value))
        {
            return ProviderType.OpenAI;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "openai":
                return ProviderType.OpenAI;
            case "anthropic":
                return ProviderType.Anthropic;
            case "openaicompatible":
            case "openai-compatible":
            case "openai compatible":
                return ProviderType.OpenAICompatible;
            default:
                AnsiConsole.MarkupLine("[red]Enter OpenAI, Anthropic, or OpenAICompatible.[/]");
                break;
        }
    }
}

static string PromptForThinkingLevel(string defaultThinkingLevel)
{
    while (true)
    {
        var value = PromptForOptionalText($"[green]Thinking level[/] [dim](low/high; default: {defaultThinkingLevel})[/]");
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultThinkingLevel;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "low":
            case "high":
                return value.Trim().ToLowerInvariant();
            default:
                AnsiConsole.MarkupLine("[red]Enter low or high.[/]");
                break;
        }
    }
}

internal sealed record CommandLineOptions(string? JobName, bool ListJobs, bool CreateJobInteractively, string? JobTemplateName, string? TaskCommandJobName, bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? jobName = null;
        var listJobs = false;
        var createJobInteractively = false;
        string? jobTemplateName = null;
        string? taskCommandJobName = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--job":
                    if (index + 1 >= args.Length)
                    {
                        throw new InvalidOperationException("Missing job name after '--job'.");
                    }

                    jobName = args[++index];
                    break;
                case "--list-jobs":
                    listJobs = true;
                    break;
                case "--create-job":
                    createJobInteractively = true;
                    break;
                case "--job-template":
                    if (index + 1 >= args.Length)
                    {
                        throw new InvalidOperationException("Missing job name after '--job-template'.");
                    }

                    jobTemplateName = args[++index];
                    break;
                case "--task-command":
                    if (index + 1 >= args.Length)
                    {
                        throw new InvalidOperationException("Missing job name after '--task-command'.");
                    }

                    taskCommandJobName = args[++index];
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument '{args[index]}'. Use '--help' for supported options.");
            }
        }

        return new CommandLineOptions(jobName, listJobs, createJobInteractively, jobTemplateName, taskCommandJobName, showHelp);
    }
}

    internal sealed record TaskSchedulerAction(string ProgramPath, string Arguments, string WorkingDirectory, string PowerShellArguments);

internal partial class Program;
