using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Plugins.Refund.Production;

/// <summary>
/// Reads orders/policies from local mock store (demo default).
/// </summary>
public sealed class MockProductionOrderClient(IRefundDataStore store) : IProductionOrderClient
{
    public string Mode => "Mock";

    public async Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
        => await store.GetOrderAsync(orderId, ct);

    public async Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
        => new { orders = await store.ListOrdersAsync(userId, ct), source = "mock" };

    public async Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
        => await store.GetPolicyAsync(policyId, ct);

    public Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
        => Task.FromResult<object?>(new
        {
            refund_id = refundId,
            status = "CHANNEL_PROCESSING",
            source = "mock",
            waiting_for = "PAYMENT_CHANNEL"
        });
}

/// <summary>
/// Calls an external HTTP production API when Production:Mode=Http.
/// Does <b>not</b> fall back to mock — failures surface as exceptions.
/// </summary>
public sealed class HttpProductionOrderClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ProductionOptions> options,
    ILogger<HttpProductionOrderClient> logger) : IProductionOrderClient
{
    public string Mode => "Http";

    public Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
        => GetRequiredAsync($"orders/{Uri.EscapeDataString(orderId)}?userId={Uri.EscapeDataString(userId)}", ct);

    public Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
        => GetRequiredAsync($"users/{Uri.EscapeDataString(userId)}/orders", ct);

    public Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
        => GetRequiredAsync(
            $"policies/{Uri.EscapeDataString(policyId)}?orderId={Uri.EscapeDataString(orderId)}", ct);

    public Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
        => GetRequiredAsync($"refunds/{Uri.EscapeDataString(refundId)}", ct);

    private async Task<object?> GetRequiredAsync(string path, CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
            throw new InvalidOperationException("Production:BaseUrl is required when Mode=Http");

        var client = httpClientFactory.CreateClient("production");
        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(path, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;
                if ((int)response.StatusCode is >= 500 or 408 or 429)
                {
                    logger.LogWarning("Production HTTP {Status} for {Path} attempt {Attempt}",
                        (int)response.StatusCode, path, attempt + 1);
                    last = new HttpRequestException($"production HTTP {(int)response.StatusCode} for {path}");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"production HTTP {(int)response.StatusCode} for {path}");

                return await response.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                logger.LogWarning(ex, "Production HTTP timeout for {Path} attempt {Attempt}", path, attempt + 1);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                logger.LogWarning(ex, "Production HTTP transport error for {Path} attempt {Attempt}", path, attempt + 1);
            }
        }

        throw new InvalidOperationException($"Production HTTP failed for {path}", last);
    }
}

/// <summary>
/// Order reads via external business MCP when Production:Mode=Mcp.
/// Prefers canonical names from contracts/business-mcp-protocol.json; aliases are fallback only.
/// Requires Production:McpEndpoint; does not fall back to mock.
/// </summary>
public sealed class McpProductionOrderClient(
    IOptions<ProductionOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<McpProductionOrderClient> logger) : IProductionOrderClient
{
    public string Mode => "Mcp";

    public Task<object?> GetOrderDetailAsync(string orderId, string userId, CancellationToken ct = default)
        => InvokeAsync(
            preferred: "stayota_get_order_detail",
            aliases: ["get_order_detail", "order_detail"],
            args: new Dictionary<string, object?> { ["orderId"] = orderId, ["userId"] = userId },
            purpose: "order-detail",
            ct);

    public Task<object?> ListUserOrdersAsync(string userId, CancellationToken ct = default)
        => InvokeAsync(
            preferred: "stayota_list_user_orders",
            aliases: ["list_user_orders", "list_orders"],
            args: new Dictionary<string, object?> { ["userId"] = userId },
            purpose: "list-orders",
            ct);

    public Task<object?> GetPolicySnapshotAsync(string policyId, string orderId, CancellationToken ct = default)
        => InvokeAsync(
            preferred: "stayota_get_policy_snapshot",
            aliases: ["get_policy_snapshot", "policy_snapshot"],
            args: new Dictionary<string, object?> { ["policyId"] = policyId, ["orderId"] = orderId },
            purpose: "policy",
            ct);

    public Task<object?> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
        => InvokeAsync(
            preferred: "stayota_get_refund_status",
            aliases: ["get_refund_status", "refund_status"],
            args: new Dictionary<string, object?> { ["refundId"] = refundId },
            purpose: "refund-status",
            ct);

    private async Task<object?> InvokeAsync(
        string preferred,
        IReadOnlyList<string> aliases,
        Dictionary<string, object?> args,
        string purpose,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.McpEndpoint))
            throw new InvalidOperationException("Production:McpEndpoint is required when Mode=Mcp");

        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(opts.McpEndpoint),
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = string.IsNullOrWhiteSpace(opts.ApiKey)
                ? null
                : new Dictionary<string, string> { ["Authorization"] = $"Bearer {opts.ApiKey}" }
        }, loggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        var tool = tools.FirstOrDefault(t => string.Equals(t.Name, preferred, StringComparison.OrdinalIgnoreCase))
                   ?? tools.FirstOrDefault(t => aliases.Any(a =>
                       string.Equals(t.Name, a, StringComparison.OrdinalIgnoreCase)
                       || t.Name.Contains(a, StringComparison.OrdinalIgnoreCase)))
                   ?? throw new InvalidOperationException(
                       $"No {purpose} tool found on production MCP endpoint (expected '{preferred}')");

        logger.LogInformation("Invoking MCP tool {Tool} for {Purpose} (preferred {Preferred})",
            tool.Name, purpose, preferred);
        var result = await tool.InvokeAsync(new AIFunctionArguments(args), ct);
        return result;
    }
}
