using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

[ApiController]
[Route("api/medical-supply/debrief")]
public class MedicalSupplyDebriefController : ControllerBase
{
    private readonly IMedicalSupplyDebriefService _debrief;
    private readonly ILogger<MedicalSupplyDebriefController> _logger;

    public MedicalSupplyDebriefController(
        IMedicalSupplyDebriefService debrief,
        ILogger<MedicalSupplyDebriefController> logger)
    {
        _debrief = debrief;
        _logger  = logger;
    }

    /// <summary>
    /// POST /api/medical-supply/debrief/generate-trend-aware
    ///
    /// Compares the current session to the user's most recent prior session for the same
    /// sim/scenario, computes trend deltas in code, then asks GPT to generate structured
    /// coaching feedback. GPT receives only computed summaries — no raw telemetry arrays.
    /// </summary>
    [HttpPost("generate-trend-aware")]
    public async Task<IActionResult> GenerateTrendAwareDebrief([FromBody] TrendAwareDebriefRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentSessionId))
            return BadRequest(new { Error = "currentSessionId is required." });
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { Error = "userId is required." });

        _logger.LogInformation(
            "Trend-aware debrief requested for userId '{UserId}', sessionId '{SessionId}'.",
            request.UserId, request.CurrentSessionId);

        var result = await _debrief.GenerateTrendAwareDebriefAsync(request);
        return Ok(result);
    }
}
