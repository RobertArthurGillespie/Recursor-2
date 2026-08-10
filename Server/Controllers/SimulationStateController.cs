using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NCATAIBlazorFrontendTest.Server.Controllers
{
    // Unity sim clients are not participants in the JWT-based admin/dashboard login (AuthController
    // issues only Name/Role claims for internal-dashboard users); there is currently no per-learner
    // identity system to authenticate this endpoint against without breaking the existing Unity
    // workflow. Deferred follow-up: introduce a real per-learner auth mechanism and require it here
    // (see docs/recursor-manual-secret-rotation.md "Deferred hardening" note). Until then, userId/simId
    // are validated against a safe identifier charset to close the path-traversal / arbitrary-blob-
    // overwrite risk (they are interpolated directly into a blob path in BlobStateService).
    [ApiController]
    [Route("api/recursor/state")]
    public class SimulationStateController : ControllerBase
    {
        private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.Compiled);

        private readonly BlobStateService _blobService;

        public SimulationStateController(BlobStateService blobService)
        {
            _blobService = blobService;
        }

        private static bool IsValidIdentifier(string? value) =>
            !string.IsNullOrEmpty(value) && SafeIdentifier.IsMatch(value);

        /// <summary>
        /// Fetches the opaque state JSON file for any given user and simulation.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetState([FromQuery] string userId, [FromQuery] string simId)
        {
            if (!IsValidIdentifier(userId) || !IsValidIdentifier(simId))
                return BadRequest("userId and simId must be non-empty and contain only letters, digits, '.', '_' or '-'.");

            string rawJson = await _blobService.GetStateBlobAsync(userId, simId);

            // Return raw text directly with an application/json header
            return Content(rawJson, "application/json");
        }

        /// <summary>
        /// Accepts ANY valid JSON body payload from any simulation and overwrites the state file.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveState([FromQuery] string userId, [FromQuery] string simId, [FromBody] JsonElement dynamicPayload)
        {
            if (!IsValidIdentifier(userId) || !IsValidIdentifier(simId))
                return BadRequest("userId and simId must be non-empty and contain only letters, digits, '.', '_' or '-'.");

            // Extract raw JSON string string from the agnostic payload container
            string rawJson = dynamicPayload.GetRawText();

            await _blobService.SaveStateBlobAsync(userId, simId, rawJson);
            return Ok(new { status = "State synced successfully." });
        }
    }
}
