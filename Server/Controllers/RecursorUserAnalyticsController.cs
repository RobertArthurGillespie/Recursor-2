using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

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
    private readonly IAdxUserBaselineQueryService _baselineService;
    private readonly IUserRelativeSignalService _relativeSignalService;
    private readonly IUserRelativePolicyAdvisorService _policyAdvisorService;

    public RecursorUserAnalyticsController(
        IAdxRecursorQueryService queryService,
        IAdxUserBaselineQueryService baselineService,
        IUserRelativeSignalService relativeSignalService,
        IUserRelativePolicyAdvisorService policyAdvisorService)
    {
        _queryService          = queryService;
        _baselineService       = baselineService;
        _relativeSignalService = relativeSignalService;
        _policyAdvisorService  = policyAdvisorService;
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

    /// <summary>
    /// GET /api/recursor/users/{userId}/baseline?recentWindowCount=10
    ///
    /// Returns a compact longitudinal behavioral snapshot for the given user,
    /// computed from BehaviorStateTrainingRows across all of their sessions.
    ///
    /// Includes lifetime averages for key dimension and behavior scores,
    /// weak-label rates (confusion, hint-dependence, stable mastery),
    /// and recent-window averages over the latest <c>recentWindowCount</c> windows.
    ///
    /// Returns 404 when no data exists for the user.
    /// Returns 503 if ADX is not configured (null result from service).
    /// </summary>
    [HttpGet("{userId}/baseline")]
    public async Task<IActionResult> GetUserBaseline(
        string userId,
        [FromQuery] int recentWindowCount = 10)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId is required.");

        if (recentWindowCount < 1 || recentWindowCount > 500)
            return BadRequest("recentWindowCount must be between 1 and 500.");

        var snapshot = await _baselineService.GetUserBaselineAsync(userId, recentWindowCount);

        if (snapshot is null)
            return NotFound($"No behavioral data found for user '{userId}'.");

        return Ok(snapshot);
    }

    /// <summary>
    /// GET /api/recursor/users/{userId}/relative-signals
    ///
    /// Returns a UserRelativeSignals snapshot comparing the user's most recent
    /// behavior-state window against their lifetime baseline averages.
    ///
    /// Includes current values, baseline values, deltas, and simple flags
    /// for confusion, hint dependence, goal understanding, and attention.
    ///
    /// Read-only — does not affect the live adaptation pipeline.
    /// Returns 404 when no data exists for the user.
    /// Returns 503 if ADX is not configured (null result from service).
    /// </summary>
    [HttpGet("{userId}/relative-signals")]
    public async Task<IActionResult> GetUserRelativeSignals(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId is required.");

        var signals = await _relativeSignalService.GetUserRelativeSignalsAsync(userId);

        if (signals is null)
            return NotFound($"No behavioral data found for user '{userId}'.");

        return Ok(signals);
    }

    /// <summary>
    /// GET /api/recursor/users/{userId}/shadow-policy-decision
    ///
    /// Returns a PersonalizedPolicyShadowDecision comparing the user's latest
    /// actual adaptation decision against a baseline-aware shadow recommendation
    /// derived from their user-relative signals.
    ///
    /// The shadow recommendation is OBSERVATIONAL ONLY — it does not affect
    /// the live adaptation pipeline or Unity responses.
    ///
    /// Useful for evaluating whether baseline-aware policy would diverge from
    /// the current live policy before deciding to enable personalized adaptation.
    ///
    /// Returns 404 when no behavioral data or no actual decisions exist for the user.
    /// Returns 503 if ADX is not configured.
    /// </summary>
    [HttpGet("{userId}/shadow-policy-decision")]
    public async Task<IActionResult> GetShadowPolicyDecision(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId is required.");

        var shadow = await _policyAdvisorService.GetShadowDecisionAsync(userId);

        if (shadow is null)
            return NotFound($"No behavioral data or adaptation decisions found for user '{userId}'.");

        return Ok(shadow);
    }
}
