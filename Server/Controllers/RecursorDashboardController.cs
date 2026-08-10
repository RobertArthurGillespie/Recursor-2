using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;

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
    ///
    /// Stage 7: the response's Status distinguishes a genuinely empty result (Success, zero
    /// Rows — "no sessions yet") from a query that could not run at all
    /// (ServiceUnavailable/AuthorizationFailed/SchemaMissing/QueryFailed) — always check Status
    /// before treating an empty Rows list as "no data."
    /// </summary>
    [HttpGet("recent-sessions")]
    public async Task<IActionResult> GetRecentSessions()
    {
        var result = await _dashboardQuery.GetRecentSessionsAsync();
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/session/{sessionId}?riskModelVersion=&amp;behaviorStateModelVersion=
    ///
    /// Phase 14A: Returns a full per-window timeline for one session. For each window:
    /// behavior scores, guardrail state, sequence features, adaptation decision (if any),
    /// temporal risk predictions (H1/H2/H3), prediction targets, and correctness flags
    /// comparing predictions to observed outcomes. Reads ADX only — never influences
    /// live pipeline behavior.
    ///
    /// Stage 10: riskModelVersion / behaviorStateModelVersion optionally select exactly one
    /// model version's predictions to evaluate against — omit either to use the configured
    /// default version. The response's SelectedRiskModelVersion / SelectedBehaviorStateModelVersion
    /// always report which version was actually used, so a caller never has to guess.
    ///
    /// Stage 7: returns 404 only when the query succeeded and genuinely found no such session
    /// (Status == Success, Value == null). Any other Status (the query itself failed) is
    /// returned as 200 with the failure Status/ErrorMessage in the body — same convention as
    /// the other Stage-11 typed-result endpoints below — so a caller can never mistake an ADX
    /// outage for "this session doesn't exist."
    /// </summary>
    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetSessionTimeline(
        string sessionId, [FromQuery] string? riskModelVersion = null, [FromQuery] string? behaviorStateModelVersion = null)
    {
        var result = await _dashboardQuery.GetSessionTimelineAsync(sessionId, riskModelVersion, behaviorStateModelVersion);
        if (result.Status == DashboardQueryStatus.Success && result.Value is null)
            return NotFound(new { Error = $"Session '{sessionId}' not found in ADX." });
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/session/{sessionId}/behavior-state-model-versions
    ///
    /// Stage 10: distinct ModelVersion values seen for this session's behavior-state
    /// predictions, for populating a model-version selector in the dashboard UI.
    /// </summary>
    [HttpGet("session/{sessionId}/behavior-state-model-versions")]
    public async Task<IActionResult> GetAvailableBehaviorStateModelVersions(string sessionId)
    {
        var result = await _dashboardQuery.GetAvailableBehaviorStateModelVersionsAsync(sessionId);
        return Ok(result);
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
        var result = await _dashboardQuery.GetElevatedRiskModelComparisonAsync();
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/behavior-state/model-comparison?simId=&amp;scenarioId=&amp;modelVersion=&amp;horizon=
    ///
    /// Phase 10E: Joins TemporalBehaviorStatePredictions to TemporalPredictionTargets and
    /// summarizes per-class support/precision/recall/F1 per (ModelVersion, Horizon, ClassLabel)
    /// for the shadow-only multiclass behavior-state predictor. All five canonical classes are
    /// always reported, even with zero support/predictions. Missing/unknown/invalid targets are
    /// excluded from correctness (pending/no-signal, not incorrect).
    ///
    /// Stage 11: simId/scenarioId/modelVersion/horizon optionally narrow the evaluation to one
    /// simulation/scenario/version/horizon — omit simId (or pass "all") for all simulations
    /// combined. The response's Status distinguishes a genuinely empty result (Success, zero
    /// Rows) from a query that could not run (ServiceUnavailable/AuthorizationFailed/
    /// SchemaMissing/QueryFailed) — always check Status before treating an empty Rows list as
    /// "no data."
    ///
    /// Shadow-only behavior-state prediction: analytics/debug only — never influences live
    /// adaptation decisions, guardrails, or policy selection.
    /// </summary>
    [HttpGet("behavior-state/model-comparison")]
    public async Task<IActionResult> GetBehaviorStateModelComparison(
        [FromQuery] string? simId = null, [FromQuery] string? scenarioId = null,
        [FromQuery] string? modelVersion = null, [FromQuery] int? horizon = null)
    {
        var effectiveSimId = string.Equals(simId, "all", StringComparison.OrdinalIgnoreCase) ? null : simId;
        var result = await _dashboardQuery.GetBehaviorStateModelComparisonAsync(effectiveSimId, scenarioId, modelVersion, horizon);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/behavior-state/coverage?simId=&amp;scenarioId=&amp;modelVersion=&amp;horizon=
    ///
    /// Phase 10E: Breaks down behavior-state predictions by coverage state per
    /// (ModelVersion, Horizon): "resolved" (target available and valid), "pending" (no target
    /// yet), "missing-target" (target row never stamped), "unknown-target" (guardrail found no
    /// signal), "invalid-target" (unrecognized literal) — none of the last three are ever
    /// counted as an incorrect prediction. Stage 11 filters/Status semantics match
    /// GetBehaviorStateModelComparison above.
    /// Shadow-only behavior-state prediction: analytics/debug only.
    /// </summary>
    [HttpGet("behavior-state/coverage")]
    public async Task<IActionResult> GetBehaviorStateCoverage(
        [FromQuery] string? simId = null, [FromQuery] string? scenarioId = null,
        [FromQuery] string? modelVersion = null, [FromQuery] int? horizon = null)
    {
        var effectiveSimId = string.Equals(simId, "all", StringComparison.OrdinalIgnoreCase) ? null : simId;
        var result = await _dashboardQuery.GetBehaviorStateCoverageAsync(effectiveSimId, scenarioId, modelVersion, horizon);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/dashboard/behavior-state/confusion-matrix?simId=&amp;scenarioId=&amp;modelVersion=&amp;horizon=
    ///
    /// Stage 7 (corrective pass): actual-vs-predicted counts for every (ActualClass,
    /// PredictedClass) combination among the five canonical behavior-state classes, per
    /// (ModelVersion, Horizon) — the confusion matrix underlying the per-class precision/
    /// recall/F1 already reported by behavior-state/model-comparison. Same filters and Status
    /// semantics as that endpoint. Shadow-only: analytics/debug only, never influences live
    /// adaptation decisions.
    /// </summary>
    [HttpGet("behavior-state/confusion-matrix")]
    public async Task<IActionResult> GetBehaviorStateConfusionMatrix(
        [FromQuery] string? simId = null, [FromQuery] string? scenarioId = null,
        [FromQuery] string? modelVersion = null, [FromQuery] int? horizon = null)
    {
        var effectiveSimId = string.Equals(simId, "all", StringComparison.OrdinalIgnoreCase) ? null : simId;
        var result = await _dashboardQuery.GetBehaviorStateConfusionMatrixAsync(effectiveSimId, scenarioId, modelVersion, horizon);
        return Ok(result);
    }
}
