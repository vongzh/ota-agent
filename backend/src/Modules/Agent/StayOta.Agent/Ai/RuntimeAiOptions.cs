namespace StayOta.Agent.Ai;

/// <summary>Demo/runtime override for AI provider/model (singleton; next request picks it up).</summary>
public sealed class RuntimeAiOptions
{
    private readonly object _gate = new();
    private string? _provider;
    private string? _model;

    public string? Provider
    {
        get { lock (_gate) return _provider; }
    }

    public string? Model
    {
        get { lock (_gate) return _model; }
    }

    public void Set(string? provider, string? model)
    {
        lock (_gate)
        {
            _provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
            _model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        }
    }

    public object Snapshot(string configuredProvider, string? configuredModel) => new
    {
        configuredProvider,
        configuredModel,
        runtimeProvider = Provider,
        runtimeModel = Model,
        effectiveProvider = Provider ?? configuredProvider,
        effectiveModel = Model ?? configuredModel,
        allowed = new[] { "Deterministic", "OpenAI", "Ollama" }
    };
}
