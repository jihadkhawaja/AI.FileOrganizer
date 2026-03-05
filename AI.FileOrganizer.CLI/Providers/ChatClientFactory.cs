using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using System.ClientModel;

namespace AI.FileOrganizer.CLI.Providers;

public static class AgentFactory
{
    public static AIAgent Create(ProviderType provider, IConfiguration config, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider = null)
    {
        return provider switch
        {
            ProviderType.OpenAI => CreateOpenAIAgent(config, tools, instructions, chatHistoryProvider),
            ProviderType.Anthropic => CreateAnthropicAgent(config, tools, instructions, chatHistoryProvider),
            ProviderType.OpenAICompatible => CreateOpenAICompatibleAgent(config, tools, instructions, chatHistoryProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static AIAgent CreateOpenAIAgent(IConfiguration config, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider)
    {
        var apiKey = config["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OpenAI API key not configured. Set 'OpenAI:ApiKey' in config.yaml or OPENAI_API_KEY environment variable.");

        var model = config["OpenAI:Model"]
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        return client.GetChatClient(model).AsIChatClient().AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = instructions, Tools = tools },
            ChatHistoryProvider = chatHistoryProvider
        });
    }

    private static AIAgent CreateAnthropicAgent(IConfiguration config, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider)
    {
        var apiKey = config["Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("Anthropic API key not configured. Set 'Anthropic:ApiKey' in config.yaml or ANTHROPIC_API_KEY environment variable.");

        var model = config["Anthropic:Model"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_DEPLOYMENT_NAME")
            ?? "claude-haiku-4-5";

        var client = new Anthropic.AnthropicClient { ApiKey = apiKey };
        return client.AsIChatClient(model, 4096).AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = instructions, Tools = tools },
            ChatHistoryProvider = chatHistoryProvider
        });
    }

    private static AIAgent CreateOpenAICompatibleAgent(IConfiguration config, IList<AITool> tools, string instructions, ChatHistoryProvider? chatHistoryProvider)
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
        return client.GetChatClient(model).AsIChatClient().AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = instructions, Tools = tools },
            ChatHistoryProvider = chatHistoryProvider
        });
    }
}
