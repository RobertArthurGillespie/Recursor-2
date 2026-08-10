using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Api;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Controllers;

[ApiController]
[Route("api/recursor")]
public class RecursorController : ControllerBase
{
    private readonly IRecursorSessionService _sessionService;
    private readonly IPolicyRecommendationService _policyRecommendationService;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISequenceFeatureExtractor _sequenceFeatureExtractor;
    private readonly ITemporalEmbeddingService _temporalEmbedding;
    private readonly ITemporalRiskModelTrainingService _temporalRiskTraining;
    private readonly ITemporalElevatedRiskModelTrainingService _temporalElevatedRiskTraining;
    private readonly ITemporalBehaviorStateModelTrainingService _temporalBehaviorStateTraining;
    private readonly IRecursorSimContractValidator _simContractValidator;
    private readonly IConfiguration _config;
    private readonly RecursorPoliciesOptions _policies;
    private readonly IWebHostEnvironment _env;

    public RecursorController(
        IRecursorSessionService sessionService,
        IPolicyRecommendationService policyRecommendationService,
        ISessionRepository sessionRepository,
        ISequenceFeatureExtractor sequenceFeatureExtractor,
        ITemporalEmbeddingService temporalEmbedding,
        ITemporalRiskModelTrainingService temporalRiskTraining,
        ITemporalElevatedRiskModelTrainingService temporalElevatedRiskTraining,
        ITemporalBehaviorStateModelTrainingService temporalBehaviorStateTraining,
        IRecursorSimContractValidator simContractValidator,
        IConfiguration config,
        IOptions<RecursorPoliciesOptions> policiesOptions,
        IWebHostEnvironment env)
    {
        _sessionService = sessionService;
        _policyRecommendationService = policyRecommendationService;
        _sessionRepository = sessionRepository;
        _sequenceFeatureExtractor = sequenceFeatureExtractor;
        _temporalEmbedding = temporalEmbedding;
        _temporalRiskTraining = temporalRiskTraining;
        _temporalElevatedRiskTraining = temporalElevatedRiskTraining;
        _temporalBehaviorStateTraining = temporalBehaviorStateTraining;
        _simContractValidator = simContractValidator;
        _config = config;
        _policies = policiesOptions.Value;
        _env = env;
    }

    /// <summary>
    /// Returns 403 Forbidden if model training is disabled by configuration or not permitted
    /// in the current environment; otherwise null, meaning the caller may proceed. This is
    /// independent of and in addition to the RecursorModelAdmin authorization policy applied
    /// to each training action — authorization answers "who", this answers "where/whether".
    /// </summary>
    internal IActionResult? CheckModelTrainingEnabled()
    {
        if (!_policies.EnableModelTraining)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Error = "Model training is disabled by configuration (Recursor:Policies:EnableModelTraining=false)."
            });
        }

        var allowedEnvironments = _policies.ModelTrainingAllowedEnvironments ?? Array.Empty<string>();
        var isAllowedEnvironment = allowedEnvironments.Any(
            e => string.Equals(e, _env.EnvironmentName, StringComparison.OrdinalIgnoreCase));
        if (!isAllowedEnvironment)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Error = $"Model training is not permitted in the '{_env.EnvironmentName}' environment. " +
                        $"Allowed environments: {string.Join(", ", allowedEnvironments)}."
            });
        }

        return null;
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
            Explanation = result.Explanation,
            SequenceSummary = result.SequenceSummary,
            TrajectorySummary = result.TrajectorySummary
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

    /// <summary>
    /// GET /api/recursor/session-timeline/{sessionId}
    ///
    /// Phase 10A: demo-friendly endpoint. Returns the recent trajectory snapshots for a session
    /// with per-window confusion, goal, hint-dependence, and behavior state, plus the current
    /// trajectory classification computed from those snapshots. Reads in-memory session state only.
    /// </summary>
    [HttpGet("session-timeline/{sessionId}")]
    public IActionResult GetSessionTimeline(string sessionId)
    {
        var session = _sessionRepository.Get(sessionId);
        if (session is null)
            return NotFound(new { Error = $"Session '{sessionId}' not found." });

        var sequenceSummary = _sequenceFeatureExtractor.Extract(session.RecentSnapshots);
        var trajectoryClassification = sequenceSummary is not null
            ? _sequenceFeatureExtractor.Classify(sequenceSummary)
            : null;

        var windows = session.RecentSnapshots.Select(s => new WindowTimelineEntry
        {
            WindowIndex = s.WindowIndex,
            ConfusionScore = s.ConfusionScore,
            GoalUnderstanding = s.GoalUnderstanding,
            HintDependenceScore = s.HintDependenceScore,
            BehaviorState = s.OverallBehaviorState,
            CreatedAtUtc = s.CreatedAtUtc
        }).ToList();

        return Ok(new SessionTimelineResponse
        {
            SessionId = sessionId,
            WindowCount = windows.Count,
            Windows = windows,
            CurrentTrajectory = trajectoryClassification
        });
    }

    /// <summary>
    /// GET /api/recursor/session-timeline/{sessionId}/temporal
    ///
    /// Phase 10B: demo/debug endpoint. Returns the latest temporal embedding and available
    /// prediction targets (horizons 1–3) built on-demand from in-memory session state.
    /// Does not query ADX — purely reads snapshot data already held in memory.
    /// </summary>
    [HttpGet("session-timeline/{sessionId}/temporal")]
    public IActionResult GetTemporalTimeline(string sessionId)
    {
        var session = _sessionRepository.Get(sessionId);
        if (session is null)
            return NotFound(new { Error = $"Session '{sessionId}' not found." });

        var sequenceSummary = _sequenceFeatureExtractor.Extract(session.RecentSnapshots);
        var trajectoryClassification = sequenceSummary is not null
            ? _sequenceFeatureExtractor.Classify(sequenceSummary)
            : null;

        var windows = session.RecentSnapshots.Select(s => new WindowTimelineEntry
        {
            WindowIndex = s.WindowIndex,
            ConfusionScore = s.ConfusionScore,
            GoalUnderstanding = s.GoalUnderstanding,
            HintDependenceScore = s.HintDependenceScore,
            BehaviorState = s.OverallBehaviorState,
            CreatedAtUtc = s.CreatedAtUtc
        }).ToList();

        TemporalEmbeddingVector? latestEmbedding = null;
        List<TemporalPredictionTarget> predictionTargets = [];

        if (session.RecentSnapshots.Count > 0)
        {
            var currentSnapshot = session.RecentSnapshots[^1];
            latestEmbedding = _temporalEmbedding.BuildEmbedding(
                session.SessionId, session.UserId, session.SimId, session.ScenarioId,
                currentSnapshot, sequenceSummary, trajectoryClassification,
                guardrail: null, featureVector: null);

            predictionTargets = _temporalEmbedding.BuildPredictionTargets(
                session.SessionId, session.UserId,
                session.RecentSnapshots, trajectoryClassification);
        }

        return Ok(new TemporalTimelineResponse
        {
            SessionId = sessionId,
            WindowCount = windows.Count,
            Windows = windows,
            LatestEmbedding = latestEmbedding,
            PredictionTargets = predictionTargets,
            CurrentTrajectory = trajectoryClassification,
        });
    }

    /// <summary>
    /// POST /api/recursor/train-temporal-risk — RecursorModelAdmin only.
    ///
    /// Phase 10C-2: Queries joined temporal training rows from ADX, trains one
    /// SdcaMaximumEntropy multiclass model per horizon (1–3), saves model files
    /// to configured paths, and returns a full training report.
    ///
    /// Optional query parameter: ?modelVersion=temporal-risk-v2
    /// Allowed characters: letters, numbers, dash, underscore, dot.
    /// If omitted, uses the configured/default version (temporal-risk-v1).
    ///
    /// Stage 4: immutable versioning — an existing version is never retrained/overwritten;
    /// artifacts publish atomically to versions/temporal-risk/{version}/ only if all three
    /// horizons train, save, and load-verify successfully (see report.Published).
    ///
    /// This endpoint trains a shadow-only baseline model. It does NOT affect
    /// adaptation decisions, policy suppression, or any live pipeline behavior.
    /// Publishing a version does not activate it — update configuration and restart/redeploy.
    /// </summary>
    [HttpPost("train-temporal-risk")]
    [Authorize(Policy = "RecursorModelAdmin")]
    public async Task<IActionResult> TrainTemporalRisk([FromQuery] string? modelVersion = null)
    {
        if (CheckModelTrainingEnabled() is { } disabled) return disabled;

        if (modelVersion is not null)
        {
            var sanitized = TemporalRiskModelTrainingService.SanitizeModelVersion(modelVersion);
            if (sanitized != modelVersion || sanitized.Length == 0)
                return BadRequest(new
                {
                    Error = $"Invalid modelVersion '{modelVersion}'. " +
                            "Use only letters, numbers, dash, underscore, and dot."
                });
        }

        var report = await _temporalRiskTraining.TrainAsync(modelVersion);
        return Ok(report);
    }

    /// <summary>
    /// POST /api/recursor/train-temporal-elevated-risk — Dev/admin only.
    ///
    /// Phase 10D-1/10D-4: Queries joined temporal training rows from ADX, collapses
    /// "medium" + "high" labels to "elevated", trains one SdcaMaximumEntropy
    /// binary model per horizon (1–3), saves model files to versioned paths,
    /// and returns a full training report with class-specific precision/recall/F1.
    ///
    /// Optional query parameter: ?modelVersion=temporal-elevated-risk-v2
    /// Allowed characters: letters, numbers, dash, underscore, dot.
    /// If omitted, uses the configured/default version (temporal-elevated-risk-v1).
    ///
    /// Shadow-only. Does NOT affect adaptation decisions. Restart after training.
    /// </summary>
    [HttpPost("train-temporal-elevated-risk")]
    [Authorize(Policy = "RecursorModelAdmin")]
    public async Task<IActionResult> TrainTemporalElevatedRisk([FromQuery] string? modelVersion = null)
    {
        if (CheckModelTrainingEnabled() is { } disabled) return disabled;

        if (modelVersion is not null)
        {
            var sanitized = TemporalElevatedRiskModelTrainingService.SanitizeModelVersion(modelVersion);
            if (sanitized != modelVersion || sanitized.Length == 0)
                return BadRequest(new
                {
                    Error = $"Invalid modelVersion '{modelVersion}'. " +
                            "Use only letters, numbers, dash, underscore, and dot."
                });
        }

        var report = await _temporalElevatedRiskTraining.TrainAsync(modelVersion);
        return Ok(report);
    }

    /// <summary>
    /// POST /api/recursor/train-temporal-behavior-state — Dev/admin only.
    ///
    /// Phase 10E: Queries joined temporal training rows from ADX, normalizes/excludes labels
    /// via BehaviorStateLabelPolicy (canonical: struggling/recovering/stable/mastering/conflicted;
    /// blank and "unknown" rows are excluded, never fabricated), trains one SdcaMaximumEntropy
    /// multiclass model per horizon (1-3) using a session-level train/validation split, saves
    /// model files to versioned paths, and returns a full training report with combined and
    /// per-simulation metrics.
    ///
    /// Optional query parameter: ?modelVersion=temporal-behavior-state-v2
    /// Allowed characters: letters, numbers, dash, underscore, dot.
    /// If omitted, uses the configured/default version (temporal-behavior-state-v1).
    ///
    /// Shadow-only. Does NOT affect adaptation decisions, guardrails, policy selection, hint
    /// behavior, difficulty, time pressure, or any other simulation parameter. Restart/redeploy
    /// after training so TemporalBehaviorStatePredictionService loads the new model files.
    /// </summary>
    [HttpPost("train-temporal-behavior-state")]
    [Authorize(Policy = "RecursorModelAdmin")]
    public async Task<IActionResult> TrainTemporalBehaviorState([FromQuery] string? modelVersion = null)
    {
        if (CheckModelTrainingEnabled() is { } disabled) return disabled;

        if (modelVersion is not null)
        {
            var sanitized = TemporalBehaviorStateModelTrainingService.SanitizeModelVersion(modelVersion);
            if (sanitized != modelVersion || sanitized.Length == 0)
                return BadRequest(new
                {
                    Error = $"Invalid modelVersion '{modelVersion}'. " +
                            "Use only letters, numbers, dash, underscore, and dot."
                });
        }

        var report = await _temporalBehaviorStateTraining.TrainAsync(modelVersion);
        return Ok(report);
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

    /// <summary>
    /// POST /api/recursor/validate-sim-batch — Dev/debug only.
    ///
    /// Phase 10S-1: Validates a RawEventBatch against the Recursor sim adapter contract.
    /// Returns warnings for missing recommended fields and a summary of detected event
    /// categories and types. Does NOT ingest, persist, or modify any data.
    /// Use this from Unity during integration to verify the event structure before going live.
    /// </summary>
    [HttpPost("validate-sim-batch")]
    public IActionResult ValidateSimBatch([FromBody] RawEventBatch batch)
    {
        var result = _simContractValidator.Validate(batch);
        return Ok(result);
    }
}
