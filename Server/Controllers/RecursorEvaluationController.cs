using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

// ── Shadow ML Evaluation Endpoint ─────────────────────────────────────────────
// This controller provides observational evaluation of shadow ML models.
// It does NOT affect the live adaptation pipeline in any way.
// All queries are read-only against ADX BehaviorStateTrainingRows.
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/recursor")]
public class RecursorEvaluationController : ControllerBase
{
    private readonly IAdxModelEvaluationQueryService _evaluationService;

    public RecursorEvaluationController(IAdxModelEvaluationQueryService evaluationService)
    {
        _evaluationService = evaluationService;
    }

    /// <summary>
    /// POST /api/recursor/evaluation/shadow
    ///
    /// Evaluates a shadow ML model against its rule-derived weak labels in ADX.
    /// Returns aggregate metrics and top disagreement rows for manual inspection.
    ///
    /// This endpoint is observational only — it does not change session state,
    /// adaptation behavior, or any live pipeline component.
    /// </summary>
    [HttpPost("evaluation/shadow")]
    public async Task<ActionResult<ModelEvaluationResult>> EvaluateShadowModel(
        [FromBody] ModelEvaluationRequest request)
    {
        // Validate model type.
        if (!ShadowModelType.IsValid(request.Model))
        {
            return BadRequest(
                $"Unknown model '{request.Model}'. " +
                $"Supported values: {ShadowModelType.Confusion}, " +
                $"{ShadowModelType.HintDependence}, {ShadowModelType.StableMastery}.");
        }

        // Validate threshold.
        if (request.Threshold < 0.0 || request.Threshold > 1.0)
            return BadRequest("Threshold must be between 0.0 and 1.0.");

        // Clamp disagreement limit to a safe range.
        if (request.DisagreementLimit < 1)
            request.DisagreementLimit = 1;
        if (request.DisagreementLimit > 100)
            request.DisagreementLimit = 100;

        // Validate date range when both bounds are supplied.
        if (request.StartUtc.HasValue && request.EndUtc.HasValue
            && request.StartUtc.Value > request.EndUtc.Value)
        {
            return BadRequest("StartUtc must be before or equal to EndUtc.");
        }

        // Run both queries. They are independent — run sequentially to stay within
        // one request's ADX quota, but could be parallelized if latency becomes a concern.
        var summary = await _evaluationService.GetEvaluationSummaryAsync(request);
        var disagreements = await _evaluationService.GetDisagreementRowsAsync(request);

        return Ok(new ModelEvaluationResult
        {
            Summary       = summary,
            Disagreements = disagreements
        });
    }
}
