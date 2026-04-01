using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

// ── DEBUG ENDPOINTS ───────────────────────────────────────────────────────────
// These endpoints are intended for development and debugging only.
// They query ADX directly and return domain model representations of stored rows.
//
// All endpoints in this controller return 404 Not Found when the application
// is not running in the Development environment.
//
// Do NOT expose these endpoints in production.
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/recursor")]
public class RecursorDebugController : ControllerBase
{
    private readonly IAdxRecursorQueryService _queryService;
    private readonly IWebHostEnvironment _env;

    public RecursorDebugController(
        IAdxRecursorQueryService queryService,
        IWebHostEnvironment env)
    {
        _queryService = queryService;
        _env = env;
    }

    /// <summary>
    /// [DEBUG] GET /api/recursor/sessions/{sessionId}/features/latest
    ///
    /// Returns the latest FeatureWindowDocument stored in ADX for the given session.
    /// Queries the FeatureWindows table and maps the most recent row back to the domain model.
    ///
    /// Only available in the Development environment.
    /// Returns 404 in all other environments.
    /// Returns 503 if ADX is not configured.
    /// Returns 404 if no feature windows exist yet for the session.
    /// </summary>
    [HttpGet("sessions/{sessionId}/features/latest")]
    public async Task<IActionResult> GetLatestFeatureWindow(string sessionId)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("sessionId is required.");

        var rows = await _queryService.GetLatestFeatureWindowsAsync(sessionId, count: 1);
        var row = rows.FirstOrDefault();

        if (row is null)
            return NotFound($"No feature windows found in ADX for session '{sessionId}'.");

        var document = AdxRowMapper.MapToFeatureWindowDocument(row);
        return Ok(document);
    }
}
