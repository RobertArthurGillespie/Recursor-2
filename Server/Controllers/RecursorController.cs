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

    public RecursorController(IRecursorSessionService sessionService)
    {
        _sessionService = sessionService;
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
            ReasoningSummary = result.ReasoningSummary
        });
    }

    /// <summary>POST /api/recursor/sessions/{sessionId}/end</summary>
    [HttpPost("sessions/{sessionId}/end")]
    public async Task<IActionResult> EndSession(string sessionId)
    {
        await _sessionService.EndSessionAsync(sessionId);
        return Ok();
    }
}
