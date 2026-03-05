using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using System.ClientModel;

namespace AI.FileOrganizer.CLI.Providers;

public static class AgentFactory
{
    /// <summary>
    /// Creates an IChatClient for the given provider. The client is reusable across multiple agent instances.
    /// </summary>
    public static IChatClient CreateChatClient(ProviderType provider, IConfiguration config)
    {
        return provider switch
        {
            ProviderType.OpenAI => CreateOpenAIChatClient(config),
            ProviderType.Anthropic => CreateAnthropicChatClient(config),
            ProviderType.OpenAICompatible => CreateOpenAICompatibleChatClient(config),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    /// <summary>
    /// Creates an AIAgent from an existing chat client with the given tools and options.
    /// Lightweight — can be called per-request to scope tools by intent.
    /// </summary>
    public static AIAgent CreateAgent(IChatClient chatClient, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider = null, ReasoningEffort? reasoningEffort = null)
    {
        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = instructions, Tools = tools, Reasoning = BuildReasoningOptions(reasoningEffort) },
            ChatHistoryProvider = chatHistoryProvider
        });
    }

    /// <summary>
    /// Creates a fully configured agent in one step (convenience overload).
    /// </summary>
    public static AIAgent Create(ProviderType provider, IConfiguration config, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider = null, ReasoningEffort? reasoningEffort = null)
    {
        var chatClient = CreateChatClient(provider, config);
        return CreateAgent(chatClient, tools, instructions, chatHistoryProvider, reasoningEffort);
    }

    private static IChatClient CreateOpenAIChatClient(IConfiguration config)
    {
        var apiKey = config["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set 'OpenAI:ApiKey' in config.yaml or OPENAI_API_KEY environment variable.");

        var model = config["OpenAI:Model"]
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        return client.GetChatClient(model).AsIChatClient();
    }

    private static IChatClient CreateAnthropicChatClient(IConfiguration config)
    {
        var apiKey = config["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("Anthropic API key not configured. Set 'Anthropic:ApiKey' in config.yaml or ANTHROPIC_API_KEY environment variable.");

        var model = config["Anthropic:Model"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_DEPLOYMENT_NAME")
            ?? "claude-haiku-4-5";

        var client = new Anthropic.AnthropicClient { ApiKey = apiKey };
        return client.AsIChatClient(model, 4096);
    }

    private static IChatClient CreateOpenAICompatibleChatClient(IConfiguration config)
    {
        var endpoint = config["OpenAICompatible:Endpoint"]
            ?? Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_ENDPOINT")
            ?? "http://localhost:1234/v1";

        var apiKey = config["OpenAICompatible:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY")
            ?? "lm-studio";

        var model = config["OpenAICompatible:Model"]
            ?? Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_MODEL")
            ?? "default";

        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return client.GetChatClient(model).AsIChatClient();
    }

    private static ReasoningOptions? BuildReasoningOptions(ReasoningEffort? effort)
    {
        if (effort is null)
            return null;

        return new ReasoningOptions { Effort = effort };
    }
}
