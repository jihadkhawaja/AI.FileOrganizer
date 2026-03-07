using AI.FileOrganizer.CLI.Providers;
using Microsoft.Extensions.Configuration;

namespace AI.FileOrganizer.CLI;

internal sealed record ScheduledJobDefinition(
    string Name,
    string Prompt,
    ProviderType? Provider,
    string? ThinkingLevel,
    bool AutoApprove,
    bool PersistMemory,
    string? Schedule,
    bool Enabled)
{
    public IEnumerable<string> ToYamlLines(int indentSpaces = 0)
    {
        var indent = new string(' ', indentSpaces);
        var childIndent = new string(' ', indentSpaces + 2);

        yield return $"{indent}- Name: \"{EscapeYaml(Name)}\"";
        yield return $"{childIndent}Prompt: \"{EscapeYaml(Prompt)}\"";
        yield return $"{childIndent}Provider: \"{FormatProvider(Provider ?? ProviderType.OpenAI)}\"";
        yield return $"{childIndent}AutoApprove: {FormatBool(AutoApprove)}";
        yield return $"{childIndent}PersistMemory: {FormatBool(PersistMemory)}";
        yield return $"{childIndent}ThinkingLevel: \"{EscapeYaml(ThinkingLevel ?? "low")}\"";
        if (!string.IsNullOrWhiteSpace(Schedule))
        {
            yield return $"{childIndent}Schedule: \"{EscapeYaml(Schedule)}\"";
        }

        yield return $"{childIndent}Enabled: {FormatBool(Enabled)}";
    }

    public static IReadOnlyList<ScheduledJobDefinition> Load(IConfiguration configuration)
    {
        var jobs = new List<ScheduledJobDefinition>();
        foreach (var section in configuration.GetSection("Jobs").GetChildren())
        {
            if (TryParse(section, out var job))
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    public static ScheduledJobDefinition? Find(IConfiguration configuration, string jobName)
    {
        return Load(configuration)
            .FirstOrDefault(job => string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParse(IConfigurationSection section, out ScheduledJobDefinition job)
    {
        var name = section["Name"]?.Trim();
        var prompt = section["Prompt"]?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
        {
            job = null!;
            return false;
        }

        job = new ScheduledJobDefinition(
            name,
            prompt,
            ParseProvider(section["Provider"]),
            section["ThinkingLevel"],
            ParseBool(section["AutoApprove"]),
            ParseBool(section["PersistMemory"]),
            section["Schedule"],
            !string.Equals(section["Enabled"], "false", StringComparison.OrdinalIgnoreCase));

        return true;
    }

    private static ProviderType? ParseProvider(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "openai" => ProviderType.OpenAI,
            "anthropic" => ProviderType.Anthropic,
            "openaicompatible" => ProviderType.OpenAICompatible,
            "openai-compatible" => ProviderType.OpenAICompatible,
            _ => throw new InvalidOperationException($"Unsupported provider '{value}' in Jobs configuration.")
        };
    }

    private static bool ParseBool(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProvider(ProviderType provider)
    {
        return provider switch
        {
            ProviderType.OpenAI => "OpenAI",
            ProviderType.Anthropic => "Anthropic",
            ProviderType.OpenAICompatible => "OpenAICompatible",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}