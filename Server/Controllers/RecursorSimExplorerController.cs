using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

/// <summary>
/// Phase 16A: Read-only Sim Explorer — browse Recursor data by sim, user, and session.
/// All endpoints are analytics-only and never influence the adaptive pipeline.
/// </summary>
[ApiController]
[Route("api/recursor/sim-explorer")]
public class RecursorSimExplorerController : ControllerBase
{
    private readonly IAdxSimExplorerQueryService _simExplorer;

    public RecursorSimExplorerController(IAdxSimExplorerQueryService simExplorer)
    {
        _simExplorer = simExplorer;
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/sims
    /// Returns all sims currently represented in ADX with aggregate counts.
    /// </summary>
    [HttpGet("sims")]
    public async Task<IActionResult> GetSims()
    {
        var sims = await _simExplorer.GetSimsAsync();
        return Ok(sims);
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/users
    /// Returns all unique users cross-simulation represented in ADX with aggregate counts.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _simExplorer.GetAllUsersAsync();
        return Ok(users);
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/sims/{simId}/users
    /// Returns all users that have sessions for the specified sim.
    /// </summary>
    [HttpGet("sims/{simId}/users")]
    public async Task<IActionResult> GetUsersForSim(string simId)
    {
        var users = await _simExplorer.GetUsersForSimAsync(simId);
        return Ok(users);
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/users/{userId}/sims
    /// Returns a list of distinct SimIds that the specified user has recorded telemetry for.
    /// </summary>
    [HttpGet("users/{userId}/sims")]
    public async Task<IActionResult> GetSimsForUser(string userId)
    {
        var sims = await _simExplorer.GetSimIdsForUserAsync(userId);
        return Ok(sims);
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/sims/{simId}/users/{userId}/sessions
    /// Returns all sessions for a user within the specified sim.
    /// </summary>
    [HttpGet("sims/{simId}/users/{userId}/sessions")]
    public async Task<IActionResult> GetSessionsForSimUser(string simId, string userId)
    {
        var sessions = await _simExplorer.GetSessionsForSimUserAsync(simId, userId);
        return Ok(sessions);
    }

    /// <summary>
    /// GET /api/recursor/sim-explorer/sims/{simId}/users/{userId}/sessions/{sessionId}?riskModelVersion=&amp;behaviorStateModelVersion=
    /// Returns detail for a single session: Recursor timeline, raw events, and event counts.
    /// Stage 10: optional model-version overrides — see RecursorDashboardController.GetSessionTimeline.
    /// </summary>
    [HttpGet("sims/{simId}/users/{userId}/sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionDetail(
        string simId, string userId, string sessionId,
        [FromQuery] string? riskModelVersion = null, [FromQuery] string? behaviorStateModelVersion = null)
    {
        var detail = await _simExplorer.GetSessionDetailAsync(simId, userId, sessionId, riskModelVersion, behaviorStateModelVersion);
        if (detail is null)
            return NotFound(new { Error = $"Session '{sessionId}' not found for sim '{simId}' user '{userId}'." });
        return Ok(detail);
    }
}
