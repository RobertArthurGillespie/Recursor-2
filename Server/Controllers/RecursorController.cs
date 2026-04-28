using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Api;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

[ApiController]
[Route("api/recursor")]
public class RecursorController : ControllerBase
{
    private readonly IRecursorSessionService _sessionService;
    private readonly IPolicyRecommendationService _policyRecommendationService;
    private readonly IConfiguration _config;

    public RecursorController(
        IRecursorSessionService sessionService,
        IPolicyRecommendationService policyRecommendationService,
        IConfiguration config)
    {
        _sessionService = sessionService;
        _policyRecommendationService = policyRecommendationService;
        _config = config;
    }

    /// <summary>POST /api/recursor/sessions/start</summary>
    [HttpPost("sessions/start")]
    public async Task<IActionResult> StartSession([FromBody] StartSessionApiRequest request)
    {
        var result = await _sessionService.StartSessionAsync(new StartSessionRequest
        {
            SimId = request.SimId,
            SimVersion = request.SimVersion,
            UserId = request.UserId,
            ScenarioId = request.ScenarioId
        });

        if (!result.Success)
            return BadRequest(new StartSessionApiResponse { Success = false, Error = result.Error });

        return Ok(new StartSessionApiResponse { Success = true, SessionId = result.SessionId });
    }

    /// <summary>POST /api/recursor/events/batch</summary>
    [HttpPost("events/batch")]
    public async Task<IActionResult> SubmitBatch([FromBody] RawEventBatch batch)
    {
        var result = await _sessionService.ProcessBatchAsync(batch);

        if (!result.Success)
            return BadRequest(new BatchApiResponse { Success = false, Error = result.Error });

        return Ok(new BatchApiResponse
        {
            Success = true,
            AdaptationProduced = result.AdaptationProduced,
            ParameterChanges = result.ParameterChanges,
            HypothesisLabels = result.HypothesisLabels,
            ReasoningSummary = result.ReasoningSummary,
            Explanation = result.Explanation
        });
    }

    /// <summary>POST /api/recursor/sessions/{sessionId}/end</summary>
    [HttpPost("sessions/{sessionId}/end")]
    public async Task<IActionResult> EndSession(string sessionId)
    {
        await _sessionService.EndSessionAsync(sessionId);
        return Ok();
    }

    /// <summary>
    /// GET /api/recursor/policy-recommendations
    ///
    /// Offline / analytics-only. Queries AdaptationEffectiveness in ADX and returns
    /// per-family reliability tiers and segmented breakdown by learner state.
    /// Not called during live adaptation decisions — safe to invoke at any time.
    /// </summary>
    [HttpGet("policy-recommendations")]
    public async Task<IActionResult> GetPolicyRecommendations()
    {
        var result = await _policyRecommendationService.GenerateRecommendationsAsync();
        return Ok(result);
    }

    /// <summary>
    /// GET /api/recursor/policy-recommendations/export-config
    ///
    /// Phase 9B/9C: Returns a PolicyReliabilityConfigExport shaped for manual insertion into
    /// Recursor:PolicyReliability in appsettings.json after human review.
    /// Includes global tiers (Phase 9B) and conditional tiers (Phase 9C).
    /// Mode is always "shadow" — reviewer must promote to "active" explicitly.
    /// Offline only — never influences live adaptation decisions.
    /// </summary>
    [HttpGet("policy-recommendations/export-config")]
    public async Task<IActionResult> ExportPolicyReliabilityConfig()
    {
        var json = await _policyRecommendationService.ExportPolicyReliabilityConfigAsync();
        return Content(json, "application/json");
    }

    /// <summary>
    /// GET /api/recursor/policy-recommendations/export-config-text
    ///
    /// Phase 9B/9C: Returns a human-readable summary with a JSON snippet ready to paste
    /// under "Recursor" → "PolicyReliability" in appsettings.json, including Phase 9C
    /// conditional tiers and guidance on the safe manual review workflow.
    /// Offline only — never influences live adaptation decisions.
    /// </summary>
    [HttpGet("policy-recommendations/export-config-text")]
    public async Task<IActionResult> ExportPolicyReliabilityConfigText()
    {
        var text = await _policyRecommendationService.ExportPolicyReliabilityConfigTextAsync();
        return Content(text, "text/plain");
    }

    [HttpGet("testconfig")]
    public async Task<IActionResult> testconfig()
    {
        var value = _config["TestConfig:TestValue"];

        return Ok(new
        {
            TestValue = value,
            Timestamp = DateTime.UtcNow
        });

    }

    [HttpGet("testconfig/full")]
    public IActionResult GetFullPolicyConfig()
    {
        return Ok(new
        {
            Mode = _config["Recursor:PolicyReliability:Mode"],
            DifficultyReductionGlobal = _config["Recursor:PolicyReliability:FamilyReliabilityTiers:difficulty-reduction"],
            ConditionalStruggling =
    _config["Recursor:PolicyReliability:ConditionalFamilyReliabilityTiers:difficulty-reduction:GuardrailOverallBehaviorState=struggling"]
        });
    }
}
