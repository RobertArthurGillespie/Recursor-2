using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

// ── User-Level Analytics Endpoints ───────────────────────────────────────────
// Provides longitudinal, per-user views over ADX-stored Recursor artifacts.
// These endpoints are observational only — read-only, no pipeline side effects.
//
// Supports the Longitudinal User Layer: cross-session behavioral analysis,
// per-user adaptation history, and future user-level baseline computation.
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/recursor/users")]
public class RecursorUserAnalyticsController : ControllerBase
{
    private readonly IAdxRecursorQueryService _queryService;

    public RecursorUserAnalyticsController(IAdxRecursorQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <summary>
    /// GET /api/recursor/users/{userId}/decisions/recent?count=10
    ///
    /// Returns the most recent adaptation decisions across all sessions for
    /// the given user, ordered by creation time descending.
    ///
    /// Useful for reviewing a user's cross-session adaptation history.
    /// Returns 503 if ADX is not configured (empty result).
    /// </summary>
    [HttpGet("{userId}/decisions/recent")]
    public async Task<IActionResult> GetRecentDecisionsByUser(
        string userId,
        [FromQuery] int count = 10)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId is required.");

        if (count < 1 || count > 100)
            return BadRequest("count must be between 1 and 100.");

        var rows = await _queryService.GetLatestAdaptationDecisionsByUserAsync(userId, count);
        return Ok(rows);
    }

    /// <summary>
    /// GET /api/recursor/users/{userId}/training-rows/count
    ///
    /// Returns the total count of behavior-state training rows stored in ADX
    /// for the given user, across all sessions.
    ///
    /// Useful for assessing how much behavioral data has accumulated per user
    /// before running per-user model evaluation or baseline computation.
    /// Returns 503 if ADX is not configured (count = 0).
    /// </summary>
    [HttpGet("{userId}/training-rows/count")]
    public async Task<IActionResult> GetTrainingRowCountByUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId is required.");

        var count = await _queryService.GetTrainingRowCountByUserAsync(userId);
        return Ok(new { UserId = userId, TrainingRowCount = count });
    }
}
