using Microsoft.AspNetCore.Mvc;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Host.Services;

namespace StayOta.Agent.Host.Controllers;

[ApiController]
[Route("api/ops")]
public sealed class OpsController(OpsSummaryService ops) : ControllerBase
{
    /// <summary>
    /// Agent process metrics for the ops dashboard (sessions, audits, workflows, eval, plugins).
    /// Business north-stars are returned as placeholders only.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<OpsSummaryDto>> Summary(CancellationToken ct) =>
        Ok(await ops.BuildAsync(ct));
}
