namespace StayOta.Agent.Abstractions.Options;

public sealed class ProductionOptions
{
    public const string SectionName = "Production";

    /// <summary>Mock | Http | Mcp</summary>
    public string Mode { get; set; } = "Mock";

    /// <summary>HTTP base URL for production-like order/payment APIs.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>External business MCP endpoint (SSE/HTTP). Agent is the client; business is the server.</summary>
    public string McpEndpoint { get; set; } = "";

    public string ApiKey { get; set; } = "";
}
