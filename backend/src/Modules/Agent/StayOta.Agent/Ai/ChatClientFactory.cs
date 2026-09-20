using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Ai;

public interface IChatClientFactory
{
    /// <summary>Normalized provider id: openai | ollama | deterministic.</summary>
    string ProviderName { get; }

    /// <summary>Creates a remote LLM client. Deterministic is resolved from DI instead.</summary>
    IChatClient CreateRemote();
}

public sealed class ChatClientFactory(
    IOptions<AiOptions> options,
    RuntimeAiOptions runtime,
    ILogger<ChatClientFactory> logger) : IChatClientFactory
{
    public string ProviderName => ResolveProvider();

    public IChatClient CreateRemote()
    {
        var opts = options.Value;
        var provider = ResolveProvider();
        logger.LogInformation("Creating remote IChatClient provider={Provider}", provider);

        return provider switch
        {
            "openai" => CreateOpenAi(opts.OpenAI, runtime.Model),
            "ollama" => CreateOllama(opts.Ollama, runtime.Model),
            _ => throw new InvalidOperationException(
                $"Provider '{provider}' is not a remote LLM; resolve DeterministicChatClient from DI")
        };
    }

    private string ResolveProvider()
    {
        if (!string.IsNullOrWhiteSpace(runtime.Provider))
            return runtime.Provider!.Trim().ToLowerInvariant();

        var configured = options.Value.Provider?.Trim() ?? "Deterministic";
        var env = Environment.GetEnvironmentVariable("AI_PROVIDER");
        if (!string.IsNullOrWhiteSpace(env))
            configured = env;
        return configured.Trim().ToLowerInvariant();
    }

    private static IChatClient CreateOpenAi(OpenAiOptions cfg, string? modelOverride)
    {
        var apiKey = FirstNonEmpty(cfg.ApiKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Ai:Provider=OpenAI 需要 Ai:OpenAI:ApiKey 或环境变量 OPENAI_API_KEY");

        OpenAIClient client;
        var endpoint = FirstNonEmpty(cfg.Endpoint, Environment.GetEnvironmentVariable("OPENAI_ENDPOINT"));
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint!)
            });
        }
        else
        {
            client = new OpenAIClient(apiKey);
        }

        var model = FirstNonEmpty(modelOverride, cfg.Model, Environment.GetEnvironmentVariable("OPENAI_MODEL"))
                    ?? "gpt-4o-mini";
        return client.GetChatClient(model).AsIChatClient();
    }

    private static IChatClient CreateOllama(OllamaOptions cfg, string? modelOverride)
    {
        var endpoint = FirstNonEmpty(cfg.Endpoint, Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT"))
                       ?? "http://127.0.0.1:11434";
        var model = FirstNonEmpty(modelOverride, cfg.Model, Environment.GetEnvironmentVariable("OLLAMA_MODEL"))
                    ?? "llama3.2";
        return new OllamaApiClient(new Uri(endpoint), model);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
