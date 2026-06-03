using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

[ApiController]
[Route("api/recursor/dashboard")]
public class RecursorDashboardController : ControllerBase
{
    private readonly IAdxDashboardQueryService _dashboardQuery;

    public RecursorDashboardController(IAdxDashboardQueryService dashboardQuery)
    {
        _dashboardQuery = dashboardQuery;
    }

    /// <summary>
    /// GET /api/recursor/dashboard/recent-sessions
    ///
    /// Phase 14A: Returns up to 20 recent sessions summarized from ADX, ordered by most
    /// recently active. Per-session counts include windows, adaptation decisions, and
    /// temporal risk predictions. Reads ADX only — never influences live pipeline behavior.
    /// </summary>
    [HttpGet("recent-sessions")]
    public async Task<IActionResult> GetRecentSessions()
    {
        var sessions = await _dashboardQuery.GetRecentSessionsAsync();
        return Ok(sessions);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/session/{sessionId}
    ///
    /// Phase 14A: Returns a full per-window timeline for one session. For each window:
    /// behavior scores, guardrail state, sequence features, adaptation decision (if any),
    /// temporal risk predictions (H1/H2/H3), prediction targets, and correctness flags
    /// comparing predictions to observed outcomes. Reads ADX only — never influences
    /// live pipeline behavior.
    /// </summary>
    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetSessionTimeline(string sessionId)
    {
        var timeline = await _dashboardQuery.GetSessionTimelineAsync(sessionId);
        if (timeline is null)
            return NotFound(new { Error = $"Session '{sessionId}' not found in ADX." });
        return Ok(timeline);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/elevated-risk/model-comparison
    ///
    /// Phase 10D-4: Joins TemporalElevatedRiskPredictions to TemporalPredictionTargets and
    /// summarizes accuracy, precision, recall, and F1 per (ModelVersion, Horizon).
    /// Use this to compare model versions after retraining and running validation sessions.
    /// Analytics/debug only — never influences live adaptation decisions.
    /// </summary>
    [HttpGet("elevated-risk/model-comparison")]
    public async Task<IActionResult> GetElevatedRiskModelComparison()
    {
        var rows = await _dashboardQuery.GetElevatedRiskModelComparisonAsync();
        return Ok(rows);
    }
}
