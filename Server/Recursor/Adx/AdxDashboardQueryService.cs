using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxDashboardQueryService
{
    // Stage 7 (corrective pass): typed result distinguishes zero rows (Success, empty Rows) from
    // a query that never ran or failed outright — an ADX outage must never render as "no
    // sessions found."
    Task<DashboardQueryResult<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20);
    // Stage 10: riskModelVersion / behaviorStateModelVersion select exactly one model version's
    // predictions per prediction family — when omitted, defaults to the configured version
    // (Recursor:Models:TemporalRiskModelVersion / TemporalBehaviorStateModelVersion). This is
    // the ONLY correctness lever: the query layer filters to the selected version before the
    // timeline is built, so no window/horizon can ever have more than one candidate prediction
    // to pick from — a single timeline evaluation never silently mixes model versions.
    // Stage 7 (corrective pass): Status == Success with Value == null means the session
    // genuinely was not found in ADX; any other Status means the underlying query failed —
    // these two cases must never be conflated (previously both surfaced as a 404).
    Task<DashboardSingleQueryResult<SessionTimelineDto>> GetSessionTimelineAsync(
        string sessionId, string? riskModelVersion = null, string? behaviorStateModelVersion = null);
    // Distinct ModelVersion values seen for a session, for populating a dashboard selector.
    Task<DashboardQueryResult<string>> GetAvailableBehaviorStateModelVersionsAsync(string sessionId);
    // Phase 10D-4: compares elevated-risk model versions by joining predictions to observed targets.
    Task<DashboardQueryResult<ElevatedRiskModelComparisonRow>> GetElevatedRiskModelComparisonAsync();
    // Phase 10E: per-class precision/recall/F1 for the shadow-only behavior-state predictor.
    // Stage 11: optional filters narrow to one simulation/scenario/model-version/horizon. The
    // returned Status distinguishes zero rows (Success, empty Rows) from a query that never ran
    // or failed (ServiceUnavailable / AuthorizationFailed / SchemaMissing / QueryFailed).
    Task<DashboardQueryResult<BehaviorStateModelComparisonRow>> GetBehaviorStateModelComparisonAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null);
    // Phase 10E: resolved/pending/invalid-target coverage breakdown.
    Task<DashboardQueryResult<BehaviorStateCoverageRow>> GetBehaviorStateCoverageAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null);
    // Stage 7 (corrective pass): actual-vs-predicted counts for every (ModelVersion, Horizon,
    // ActualClass, PredictedClass) combination — the confusion matrix underlying the per-class
    // precision/recall/F1 already reported by GetBehaviorStateModelComparisonAsync.
    Task<DashboardQueryResult<BehaviorStateConfusionMatrixCell>> GetBehaviorStateConfusionMatrixAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null);
}

// ── Intermediate ADX row results — public so DashboardTimelineBuilder and tests can use them ──

public record DashboardTrainingRowResult(
    string SessionId,
    string UserId,
    string SimId,
    string ScenarioId,
    int WindowIndex,
    DateTime CreatedAtUtc,
    double ConfusionScore,
    double GoalUnderstanding,
    double HintDependenceScore,
    string GuardrailOverallBehaviorState,
    string GuardrailHintDependenceRiskLevel,
    int SequenceWindowCount,
    double VolatilityScore,
    double MomentumScore,
    double StabilityScore,
    string PredictedNearTermRisk);

public record DashboardAdaptationResult(
    DateTime CreatedAtUtc,
    int DecisionIndex,
    string InterventionFamiliesJson,
    string ParameterChangesJson,
    string ReasoningSummary,
    string? Phase8AGuardrailNotes,
    string? Phase8EReliabilityNotes,
    string? Phase10TrajectoryNotes,
    string? HypothesisLabelsJson,
    string? LearnerStateSummary,
    string? WhySupportChanged,
    string? CoachMessage,
    string? ConfidenceNote);

public record DashboardRiskPredictionResult(
    int WindowIndex,
    int Horizon,
    string PredictedNearTermRisk,
    double Confidence,
    string ModelVersion);

public record DashboardElevatedRiskPredictionResult(
    int WindowIndex,
    int Horizon,
    string PredictedElevatedRisk,
    double Confidence,
    string ModelVersion);

public record DashboardBehaviorStatePredictionResult(
    int WindowIndex,
    int Horizon,
    string PredictedBehaviorState,
    double Confidence,
    string ModelVersion);

public record DashboardTargetResult(
    int SourceWindowIndex,
    int TargetWindowIndex,
    int Horizon,
    string TargetNearTermRisk,
    string TargetBehaviorState,
    double TargetConfusionScore,
    double TargetGoalUnderstanding,
    double TargetHintDependenceScore);

// ── Service ───────────────────────────────────────────────────────────────────

public class AdxDashboardQueryService : IAdxDashboardQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdxDashboardQueryService> _logger;

    public AdxDashboardQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxDashboardQueryService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
        _configuration = configuration;
        _logger = logger;
    }

    // ── Public interface ──────────────────────────────────────────────────────

    // Exposed as public static so tests can assert on the KQL text without ADX.
    public static string BuildRecentSessionsKql(int count) => $@"let sessions = BehaviorStateTrainingRows
| summarize FirstSeenUtc = min(CreatedAtUtc), LastSeenUtc = max(CreatedAtUtc),
            WindowCount = count(),
            UserId = take_any(UserId), SimId = take_any(SimId), ScenarioId = take_any(ScenarioId)
  by SessionId
| top {count} by LastSeenUtc desc;
let adaptCounts = AdaptationDecisions_v2
| summarize AdaptationCount = count() by SessionId;
let predCounts = TemporalRiskPredictions
| summarize PredictionCount = count() by SessionId;
sessions
| join kind=leftouter adaptCounts on SessionId
| join kind=leftouter predCounts on SessionId
| project SessionId, UserId, SimId, ScenarioId, FirstSeenUtc, LastSeenUtc,
          WindowCount,
          AdaptationCount = coalesce(AdaptationCount, tolong(0)),
          PredictionCount = coalesce(PredictionCount, tolong(0))
| order by LastSeenUtc desc";

    public async Task<DashboardQueryResult<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — dashboard recent-sessions returning empty.");
            return new DashboardQueryResult<DashboardSessionSummaryDto>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var kql = BuildRecentSessionsKql(count);

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<DashboardSessionSummaryDto>();
            while (reader.Read())
            {
                results.Add(new DashboardSessionSummaryDto
                {
                    SessionId       = reader.GetString(0),
                    UserId          = reader.GetString(1),
                    SimId           = reader.GetString(2),
                    ScenarioId      = reader.GetString(3),
                    FirstSeenUtc    = reader.GetDateTime(4),
                    LastSeenUtc     = reader.GetDateTime(5),
                    WindowCount     = (int)reader.GetInt64(6),
                    AdaptationCount = (int)reader.GetInt64(7),
                    PredictionCount = (int)reader.GetInt64(8)
                });
            }
            return new DashboardQueryResult<DashboardSessionSummaryDto> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard recent-sessions query failed.");
            return new DashboardQueryResult<DashboardSessionSummaryDto>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    public async Task<DashboardSingleQueryResult<SessionTimelineDto>> GetSessionTimelineAsync(
        string sessionId, string? riskModelVersion = null, string? behaviorStateModelVersion = null)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — dashboard session-timeline returning null.");
            return new DashboardSingleQueryResult<SessionTimelineDto>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var sid = Sanitize(sessionId);

        // Stage 7 (corrective pass): a single try/catch around the whole timeline assembly
        // (rather than each private query method swallowing its own exceptions into an empty
        // list) so an ADX failure partway through is never silently reinterpreted as "this
        // prediction family just happens to have no data" — it must surface as a distinct
        // QueryFailed/AuthorizationFailed/SchemaMissing status, never as a 404 "not found."
        try
        {
            var rows = await QueryTrainingRowsAsync(sid);
            if (rows.Count == 0)
            {
                // The query itself succeeded — this session genuinely has no rows in ADX.
                return new DashboardSingleQueryResult<SessionTimelineDto> { Status = DashboardQueryStatus.Success, Value = null };
            }

            // Stage 10: resolve to a single concrete version per prediction family —
            // deterministic default is the configured version, never "whatever happens to be in
            // ADX first."
            var resolvedRiskVersion = Sanitize(string.IsNullOrWhiteSpace(riskModelVersion)
                ? (_configuration["Recursor:Models:TemporalRiskModelVersion"] ?? TemporalRiskPredictionService.ModelVersion)
                : riskModelVersion);
            var resolvedBehaviorStateVersion = Sanitize(string.IsNullOrWhiteSpace(behaviorStateModelVersion)
                ? (_configuration["Recursor:Models:TemporalBehaviorStateModelVersion"] ?? TemporalBehaviorStatePredictionService.ModelVersion)
                : behaviorStateModelVersion);

            var adaptations        = await QueryAdaptationsAsync(sid);
            var predictions        = await QueryRiskPredictionsAsync(sid, resolvedRiskVersion);
            var elevatedPreds      = await QueryElevatedRiskPredictionsAsync(sid);
            var behaviorStatePreds = await QueryBehaviorStatePredictionsAsync(sid, resolvedBehaviorStateVersion);
            var targets            = await QueryTargetsAsync(sid);

            var timeline = DashboardTimelineBuilder.Build(
                sessionId, rows, adaptations, predictions, elevatedPreds, behaviorStatePreds, targets);
            timeline.SelectedRiskModelVersion = resolvedRiskVersion;
            timeline.SelectedBehaviorStateModelVersion = resolvedBehaviorStateVersion;
            return new DashboardSingleQueryResult<SessionTimelineDto> { Status = DashboardQueryStatus.Success, Value = timeline };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard session-timeline query failed for session '{SessionId}'.", sid);
            return new DashboardSingleQueryResult<SessionTimelineDto>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    public async Task<DashboardQueryResult<string>> GetAvailableBehaviorStateModelVersionsAsync(string sessionId)
    {
        if (_queryProvider is null)
        {
            return new DashboardQueryResult<string>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var sid = Sanitize(sessionId);
        var kql = $@"TemporalBehaviorStatePredictions
| where SessionId == '{sid}'
| summarize by ModelVersion
| order by ModelVersion asc";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<string>();
            while (reader.Read())
                results.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            return new DashboardQueryResult<string> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard available-model-versions query failed for session '{SessionId}'.", sid);
            return new DashboardQueryResult<string>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    // ── Private ADX query methods ─────────────────────────────────────────────

    // Stage 7 (corrective pass): no try/catch here — exceptions propagate to the single
    // top-level catch in GetSessionTimelineAsync, which classifies the failure instead of this
    // method silently returning an empty list indistinguishable from "no rows."
    private async Task<List<DashboardTrainingRowResult>> QueryTrainingRowsAsync(string sanitizedSessionId)
    {
        var kql = $@"BehaviorStateTrainingRows
| where SessionId == '{sanitizedSessionId}'
| order by WindowIndex asc
| take 1000
| project SessionId, UserId, SimId, ScenarioId, WindowIndex, CreatedAtUtc,
          ConfusionScore, GoalUnderstanding, HintDependenceScore,
          GuardrailOverallBehaviorState, GuardrailHintDependenceRiskLevel,
          SequenceWindowCount, VolatilityScore, MomentumScore, StabilityScore,
          PredictedNearTermRisk";

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardTrainingRowResult>();
        while (reader.Read())
        {
            results.Add(new DashboardTrainingRowResult(
                SessionId:                      reader.GetString(0),
                UserId:                         reader.GetString(1),
                SimId:                          reader.GetString(2),
                ScenarioId:                     reader.GetString(3),
                WindowIndex:                    reader.GetInt32(4),
                CreatedAtUtc:                   reader.GetDateTime(5),
                ConfusionScore:                 reader.GetDouble(6),
                GoalUnderstanding:              reader.GetDouble(7),
                HintDependenceScore:            reader.GetDouble(8),
                GuardrailOverallBehaviorState:  reader.IsDBNull(9)  ? "" : reader.GetString(9),
                GuardrailHintDependenceRiskLevel: reader.IsDBNull(10) ? "" : reader.GetString(10),
                SequenceWindowCount:            reader.GetInt32(11),
                VolatilityScore:                reader.GetDouble(12),
                MomentumScore:                  reader.GetDouble(13),
                StabilityScore:                 reader.GetDouble(14),
                PredictedNearTermRisk:          reader.IsDBNull(15) ? "" : reader.GetString(15)));
        }
        return results;
    }

    // Exposed as public static so tests can assert on the KQL text without ADX.
    public static string BuildAdaptationsKql(string sanitizedSessionId) => $@"AdaptationDecisions_v2
| where SessionId == '{sanitizedSessionId}'
| order by CreatedAtUtc asc
| project CreatedAtUtc, DecisionIndex, InterventionFamilies, ParameterChanges,
          ReasoningSummary, Phase8AGuardrailNotes, Phase8EReliabilityNotes, Phase10TrajectoryNotes,
          HypothesisLabelsJson = column_ifexists(""HypothesisLabelsJson"", """"),
          LearnerStateSummary  = column_ifexists(""LearnerStateSummary"",  """"),
          WhySupportChanged    = column_ifexists(""WhySupportChanged"",    """"),
          CoachMessage         = column_ifexists(""CoachMessage"",         """"),
          ConfidenceNote       = column_ifexists(""ConfidenceNote"",       """")";

    private async Task<List<DashboardAdaptationResult>> QueryAdaptationsAsync(string sanitizedSessionId)
    {
        var kql = BuildAdaptationsKql(sanitizedSessionId);

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardAdaptationResult>();
        while (reader.Read())
        {
            results.Add(new DashboardAdaptationResult(
                CreatedAtUtc:             reader.GetDateTime(0),
                DecisionIndex:            reader.GetInt32(1),
                InterventionFamiliesJson: ReadDynamicAsString(reader, 2),
                ParameterChangesJson:     ReadDynamicAsString(reader, 3),
                ReasoningSummary:         reader.IsDBNull(4) ? "" : reader.GetString(4),
                Phase8AGuardrailNotes:    reader.IsDBNull(5) ? null : reader.GetString(5),
                Phase8EReliabilityNotes:  reader.IsDBNull(6) ? null : reader.GetString(6),
                Phase10TrajectoryNotes:   reader.IsDBNull(7) ? null : reader.GetString(7),
                HypothesisLabelsJson:     reader.IsDBNull(8)  ? null : reader.GetString(8),
                LearnerStateSummary:      reader.IsDBNull(9)  ? null : reader.GetString(9),
                WhySupportChanged:        reader.IsDBNull(10) ? null : reader.GetString(10),
                CoachMessage:             reader.IsDBNull(11) ? null : reader.GetString(11),
                ConfidenceNote:           reader.IsDBNull(12) ? null : reader.GetString(12)));
        }
        return results;
    }

    // Exposed as public static so tests can assert on the KQL text without needing ADX. Stage
    // 10: filtered to exactly one model version — the timeline never sees more than one
    // candidate prediction per (WindowIndex, Horizon), so DashboardTimelineBuilder.
    // ComputeCorrectness's FirstOrDefault can never silently pick among multiple versions.
    public static string BuildRiskPredictionsKql(string sanitizedSessionId, string modelVersion) => $@"TemporalRiskPredictions
| where SessionId == '{sanitizedSessionId}' and ModelVersion == '{modelVersion}'
| summarize arg_max(CreatedAtUtc, *) by WindowIndex, Horizon
| order by WindowIndex asc, Horizon asc
| project WindowIndex, Horizon, PredictedNearTermRisk, Confidence, ModelVersion";

    private async Task<List<DashboardRiskPredictionResult>> QueryRiskPredictionsAsync(
        string sanitizedSessionId, string modelVersion)
    {
        var kql = BuildRiskPredictionsKql(sanitizedSessionId, modelVersion);

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardRiskPredictionResult>();
        while (reader.Read())
        {
            results.Add(new DashboardRiskPredictionResult(
                WindowIndex:          reader.GetInt32(0),
                Horizon:              reader.GetInt32(1),
                PredictedNearTermRisk: reader.IsDBNull(2) ? "" : reader.GetString(2),
                Confidence:           reader.GetDouble(3),
                ModelVersion:         reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return results;
    }

    private async Task<List<DashboardElevatedRiskPredictionResult>> QueryElevatedRiskPredictionsAsync(
        string sanitizedSessionId)
    {
        var kql = $@"TemporalElevatedRiskPredictions
| where SessionId == '{sanitizedSessionId}'
| order by WindowIndex asc, Horizon asc
| project WindowIndex, Horizon, PredictedElevatedRisk, Confidence, ModelVersion";

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardElevatedRiskPredictionResult>();
        while (reader.Read())
        {
            results.Add(new DashboardElevatedRiskPredictionResult(
                WindowIndex:          reader.GetInt32(0),
                Horizon:              reader.GetInt32(1),
                PredictedElevatedRisk: reader.IsDBNull(2) ? "" : reader.GetString(2),
                Confidence:           reader.GetDouble(3),
                ModelVersion:         reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return results;
    }

    // Stage 6/7 (corrective pass): shared dedup fragment for every TemporalBehaviorStatePredictions
    // query in this file. Rows ingested before the V3 migration added the PredictionId column
    // (an additive .create-merge change) have PredictionId == "" — deduplicating those blank IDs
    // directly via `arg_max(CreatedAtUtc, *) by PredictionId` would collapse every legacy row
    // across every session/window/horizon/version into one logical prediction, which is wrong.
    // LogicalPredictionId falls back to the same composite identity
    // BehaviorStatePredictionKeys.ComputePredictionId hashes (SessionId, WindowIndex, Horizon,
    // ModelVersion) — mirrored exactly by BehaviorStatePredictionKeys.ComputeLogicalPredictionId
    // in Server/Recursor/Models/SessionModels.cs, which is unit tested directly (KQL text itself
    // cannot be executed without a live ADX cluster in this environment). New rows (nonblank
    // PredictionId) still dedup by their deterministic PredictionId; legacy rows dedup by the
    // fallback composite key instead of colliding with unrelated legacy rows. Centralized here so
    // every query builder below uses the identical fragment instead of each reimplementing it.
    private const string BehaviorStatePredictionDedupKql =
        "| extend LogicalPredictionId = iif(isempty(PredictionId), strcat(\"legacy|\", SessionId, \"|\", tostring(WindowIndex), \"|\", tostring(Horizon), \"|\", ModelVersion), PredictionId)\n| summarize arg_max(CreatedAtUtc, *) by LogicalPredictionId";

    // Exposed as public static so tests can assert on the KQL text without needing ADX.
    // Stage 7: dedup to the newest physical row per logical (WindowIndex, Horizon, ModelVersion)
    // key before projecting — a replayed/retried ingest must not surface as two rows for the
    // same prediction in the timeline. Stage 10: filtered to exactly one model version, so the
    // timeline never has more than one candidate prediction per (WindowIndex, Horizon) to
    // arbitrarily pick between.
    public static string BuildBehaviorStatePredictionsKql(string sanitizedSessionId, string modelVersion) => $@"TemporalBehaviorStatePredictions
| where SessionId == '{sanitizedSessionId}' and ModelVersion == '{modelVersion}'
{BehaviorStatePredictionDedupKql}
| order by WindowIndex asc, Horizon asc
| project WindowIndex, Horizon, PredictedBehaviorState, Confidence, ModelVersion";

    private async Task<List<DashboardBehaviorStatePredictionResult>> QueryBehaviorStatePredictionsAsync(
        string sanitizedSessionId, string modelVersion)
    {
        var kql = BuildBehaviorStatePredictionsKql(sanitizedSessionId, modelVersion);

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardBehaviorStatePredictionResult>();
        while (reader.Read())
        {
            results.Add(new DashboardBehaviorStatePredictionResult(
                WindowIndex:             reader.GetInt32(0),
                Horizon:                 reader.GetInt32(1),
                PredictedBehaviorState:  reader.IsDBNull(2) ? "" : reader.GetString(2),
                Confidence:              reader.GetDouble(3),
                ModelVersion:            reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return results;
    }

    private async Task<List<DashboardTargetResult>> QueryTargetsAsync(string sanitizedSessionId)
    {
        // Stage 7: dedup to the newest physical row per logical (SourceWindowIndex, Horizon)
        // key before projecting.
        var kql = $@"TemporalPredictionTargets
| where SessionId == '{sanitizedSessionId}'
| summarize arg_max(CreatedAtUtc, *) by SourceWindowIndex, Horizon
| order by SourceWindowIndex asc, Horizon asc
| project SourceWindowIndex, TargetWindowIndex, Horizon,
          TargetNearTermRisk, TargetBehaviorState,
          TargetConfusionScore, TargetGoalUnderstanding, TargetHintDependenceScore";

        using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
        var results = new List<DashboardTargetResult>();
        while (reader.Read())
        {
            results.Add(new DashboardTargetResult(
                SourceWindowIndex:      reader.GetInt32(0),
                TargetWindowIndex:      reader.GetInt32(1),
                Horizon:                reader.GetInt32(2),
                TargetNearTermRisk:     reader.IsDBNull(3) ? "" : reader.GetString(3),
                TargetBehaviorState:    reader.IsDBNull(4) ? "" : reader.GetString(4),
                TargetConfusionScore:   reader.GetDouble(5),
                TargetGoalUnderstanding: reader.GetDouble(6),
                TargetHintDependenceScore: reader.GetDouble(7)));
        }
        return results;
    }

    // ── Phase 10D-4: elevated-risk model comparison ───────────────────────────

    // Exposed as public static so tests can assert on the KQL without needing ADX.
    public static string BuildElevatedRiskModelComparisonKql() => @"TemporalElevatedRiskPredictions
| join kind=inner (
    TemporalPredictionTargets
    | project SessionId, UserId, Horizon, SourceWindowIndex,
              TargetElevatedRisk = iff(TargetNearTermRisk == ""low"", ""low"", ""elevated"")
) on SessionId, UserId, Horizon
| where WindowIndex == SourceWindowIndex
| summarize
    Total         = count(),
    Correct       = countif(PredictedElevatedRisk == TargetElevatedRisk),
    Accuracy      = countif(PredictedElevatedRisk == TargetElevatedRisk) * 1.0 / count(),
    ActualElevated= countif(TargetElevatedRisk == ""elevated""),
    TruePositive  = countif(PredictedElevatedRisk == ""elevated"" and TargetElevatedRisk == ""elevated""),
    FalseNegative = countif(PredictedElevatedRisk == ""low""      and TargetElevatedRisk == ""elevated""),
    FalsePositive = countif(PredictedElevatedRisk == ""elevated"" and TargetElevatedRisk == ""low""),
    ElevatedPrecision = iif(
        countif(PredictedElevatedRisk == ""elevated"") == 0,
        real(null),
        countif(PredictedElevatedRisk == ""elevated"" and TargetElevatedRisk == ""elevated"") * 1.0
            / countif(PredictedElevatedRisk == ""elevated"")
    ),
    ElevatedRecall = iif(
        countif(TargetElevatedRisk == ""elevated"") == 0,
        real(null),
        countif(PredictedElevatedRisk == ""elevated"" and TargetElevatedRisk == ""elevated"") * 1.0
            / countif(TargetElevatedRisk == ""elevated"")
    )
    by ModelVersion, Horizon
| extend ElevatedF1 = iif(
    isnull(ElevatedPrecision) or isnull(ElevatedRecall) or ElevatedPrecision + ElevatedRecall == 0,
    real(null),
    2.0 * ElevatedPrecision * ElevatedRecall / (ElevatedPrecision + ElevatedRecall)
)
| order by ModelVersion asc, Horizon asc
| project ModelVersion, Horizon, Total, Correct, Accuracy,
          ActualElevated, TruePositive, FalseNegative, FalsePositive,
          ElevatedPrecision, ElevatedRecall, ElevatedF1";

    public async Task<DashboardQueryResult<ElevatedRiskModelComparisonRow>> GetElevatedRiskModelComparisonAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — elevated-risk model-comparison returning empty.");
            return new DashboardQueryResult<ElevatedRiskModelComparisonRow>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var kql = BuildElevatedRiskModelComparisonKql();

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<ElevatedRiskModelComparisonRow>();
            while (reader.Read())
            {
                results.Add(new ElevatedRiskModelComparisonRow
                {
                    ModelVersion      = reader.IsDBNull(0)  ? "" : reader.GetString(0),
                    Horizon           = reader.GetInt32(1),
                    Total             = (int)reader.GetInt64(2),
                    Correct           = (int)reader.GetInt64(3),
                    Accuracy          = reader.GetDouble(4),
                    ActualElevated    = (int)reader.GetInt64(5),
                    TruePositive      = (int)reader.GetInt64(6),
                    FalseNegative     = (int)reader.GetInt64(7),
                    FalsePositive     = (int)reader.GetInt64(8),
                    ElevatedPrecision = reader.IsDBNull(9)  ? null : reader.GetDouble(9),
                    ElevatedRecall    = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    ElevatedF1        = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                });
            }
            return new DashboardQueryResult<ElevatedRiskModelComparisonRow> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elevated-risk model-comparison query failed.");
            return new DashboardQueryResult<ElevatedRiskModelComparisonRow>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    // ── Phase 10E: behavior-state model comparison (shadow-only) ──────────────

    // Stage 6/7/8 corrective pass: shared normalization + dedup block reused by every
    // behavior-state KQL query in this file (and mirrored — same trim/lowercase/alias/
    // membership rules — by BehaviorStateLabelPolicy.Normalize in C# and by the standalone
    // docs/kql/phase10e-*.kql audit/evaluation scripts). Classifies a raw label as one of:
    // "missing" (blank), "unknown" (explicit no-signal), "invalid" (unrecognized literal), or
    // one of the five canonical class names (after trimming/lowercasing and mapping the legacy
    // "steady" alias to "stable"). ADX is append-only, so every query here also defensively
    // deduplicates by keeping only the newest row (arg_max CreatedAtUtc) per logical key before
    // computing anything — a replayed/retried ingest must not inflate counts.
    private const string BehaviorStateNormalizationKql = @"let CanonicalBehaviorStates = dynamic([""struggling"",""recovering"",""stable"",""mastering"",""conflicted""]);
let NormalizeBehaviorState = (raw: string) {
    let trimmed = trim("" "", tolower(raw));
    let mapped = iif(trimmed == ""steady"", ""stable"", trimmed);
    case(
        isempty(trimmed), ""missing"",
        trimmed == ""unknown"", ""unknown"",
        mapped in (CanonicalBehaviorStates), mapped,
        ""invalid""
    )
};
";

    // Exposed as public static so tests can assert on the KQL without needing ADX.
    // Multiclass: one row per (ModelVersion, Horizon, ClassLabel) rather than a single row per
    // (ModelVersion, Horizon) — the dashboard aggregates rows per (ModelVersion, Horizon) into
    // macro F1 / micro accuracy client-side. All five canonical classes are always emitted for
    // every (ModelVersion, Horizon) pair that has at least one evaluated row, even a class with
    // zero support or zero predictions — Stage 8 requires the canonical class set to never
    // silently shrink.
    // Stage 11: simId/scenarioId/modelVersion/horizon narrow the evaluated predictions to
    // exactly one simulation/scenario/version/horizon — "All simulations" is simId: null.
    public static string BuildBehaviorStateModelComparisonKql(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null) =>
        BehaviorStateNormalizationKql + $@"
let predictions = TemporalBehaviorStatePredictions
{BuildPredictionFilterClause(simId, scenarioId, modelVersion, horizon)}{BehaviorStatePredictionDedupKql};
let targets = TemporalPredictionTargets
| summarize arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon
| extend NormalizedTarget = NormalizeBehaviorState(TargetBehaviorState)
| where NormalizedTarget in (CanonicalBehaviorStates); // exclude missing/unknown/invalid — never a legitimate class
let joined = predictions
| join kind=inner targets on SessionId, UserId, Horizon
| where WindowIndex == SourceWindowIndex
| extend PredictedNormalized = NormalizeBehaviorState(PredictedBehaviorState)
| project ModelVersion, Horizon, Actual = NormalizedTarget, Predicted = PredictedNormalized;
let modelHorizons = joined | summarize by ModelVersion, Horizon;
let classUniverse = modelHorizons | extend ClassLabel = CanonicalBehaviorStates | mv-expand ClassLabel to typeof(string);
let byActual = joined
| summarize SupportCount = count(), TruePositive = countif(Predicted == Actual)
    by ModelVersion, Horizon, ClassLabel = Actual;
let byPredicted = joined
| summarize PredictedCount = count()
    by ModelVersion, Horizon, ClassLabel = Predicted;
classUniverse
| join kind=leftouter byActual on ModelVersion, Horizon, ClassLabel
| join kind=leftouter byPredicted on ModelVersion, Horizon, ClassLabel
| project
    ModelVersion,
    Horizon,
    ClassLabel,
    SupportCount   = coalesce(SupportCount, 0),
    TruePositive   = coalesce(TruePositive, 0),
    PredictedCount = coalesce(PredictedCount, 0)
| extend
    Recall    = iif(SupportCount == 0, 0.0, TruePositive * 1.0 / SupportCount),
    Precision = iif(PredictedCount == 0, 0.0, TruePositive * 1.0 / PredictedCount)
| extend F1 = iif(Precision + Recall == 0, 0.0, 2.0 * Precision * Recall / (Precision + Recall))
| order by ModelVersion asc, Horizon asc, ClassLabel asc
| project ModelVersion, Horizon, ClassLabel, SupportCount, PredictedCount, TruePositive,
          Precision, Recall, F1";

    public async Task<DashboardQueryResult<BehaviorStateModelComparisonRow>> GetBehaviorStateModelComparisonAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — behavior-state model-comparison unavailable.");
            return new DashboardQueryResult<BehaviorStateModelComparisonRow>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var kql = BuildBehaviorStateModelComparisonKql(simId, scenarioId, modelVersion, horizon);

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<BehaviorStateModelComparisonRow>();
            while (reader.Read())
            {
                results.Add(new BehaviorStateModelComparisonRow
                {
                    ModelVersion   = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Horizon        = reader.GetInt32(1),
                    ClassLabel     = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SupportCount   = (int)reader.GetInt64(3),
                    PredictedCount = (int)reader.GetInt64(4),
                    TruePositive   = (int)reader.GetInt64(5),
                    Precision      = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    Recall         = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    F1             = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                });
            }
            return new DashboardQueryResult<BehaviorStateModelComparisonRow> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Behavior-state model-comparison query failed.");
            return new DashboardQueryResult<BehaviorStateModelComparisonRow>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    // Exposed as public static so tests can assert on the KQL without needing ADX.
    // Coverage buckets distinguish missing / unknown / invalid targets (Stage 6) rather than
    // collapsing them into one "invalid-target" bucket, and both sides are deduplicated by
    // logical key before joining (Stage 7) so a replayed prediction or target never inflates a
    // count.
    public static string BuildBehaviorStateCoverageKql(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null) =>
        BehaviorStateNormalizationKql + $@"
let predictions = TemporalBehaviorStatePredictions
{BuildPredictionFilterClause(simId, scenarioId, modelVersion, horizon)}{BehaviorStatePredictionDedupKql};
let targets = TemporalPredictionTargets
| summarize arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon
| project SessionId, UserId, Horizon, SourceWindowIndex, TargetBehaviorState;
predictions
| join kind=leftouter targets on SessionId, UserId, Horizon, $left.WindowIndex == $right.SourceWindowIndex
| extend NormalizedTarget = iif(isnull(SourceWindowIndex), ""pending"", NormalizeBehaviorState(TargetBehaviorState))
| extend Coverage = case(
    NormalizedTarget == ""pending"", ""pending"",
    NormalizedTarget == ""missing"", ""missing-target"",
    NormalizedTarget == ""unknown"", ""unknown-target"",
    NormalizedTarget == ""invalid"", ""invalid-target"",
    ""resolved""
  )
| summarize Count = count() by ModelVersion, Horizon, Coverage
| order by ModelVersion asc, Horizon asc, Coverage asc";

    public async Task<DashboardQueryResult<BehaviorStateCoverageRow>> GetBehaviorStateCoverageAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — behavior-state coverage unavailable.");
            return new DashboardQueryResult<BehaviorStateCoverageRow>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var kql = BuildBehaviorStateCoverageKql(simId, scenarioId, modelVersion, horizon);

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<BehaviorStateCoverageRow>();
            while (reader.Read())
            {
                results.Add(new BehaviorStateCoverageRow
                {
                    ModelVersion = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Horizon      = reader.GetInt32(1),
                    Coverage     = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Count        = (int)reader.GetInt64(3),
                });
            }
            return new DashboardQueryResult<BehaviorStateCoverageRow> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Behavior-state coverage query failed.");
            return new DashboardQueryResult<BehaviorStateCoverageRow>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    // ── Stage 7 (corrective pass): behavior-state confusion matrix ────────────

    // Exposed as public static so tests can assert on the KQL without needing ADX. Reuses the
    // exact same normalization/dedup/join logic as BuildBehaviorStateModelComparisonKql (the
    // per-class precision/recall/F1 numbers on that panel are themselves derivable from this
    // matrix), but reports every (ActualClass, PredictedClass) cell instead of collapsing to
    // per-class aggregates. All 25 (5x5 canonical class) cells are always emitted for every
    // (ModelVersion, Horizon) pair that has at least one evaluated row, even a cell with zero
    // count, for the same "never let a class silently vanish" reason as Stage 8.
    public static string BuildBehaviorStateConfusionMatrixKql(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null) =>
        BehaviorStateNormalizationKql + $@"
let predictions = TemporalBehaviorStatePredictions
{BuildPredictionFilterClause(simId, scenarioId, modelVersion, horizon)}{BehaviorStatePredictionDedupKql};
let targets = TemporalPredictionTargets
| summarize arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon
| extend NormalizedTarget = NormalizeBehaviorState(TargetBehaviorState)
| where NormalizedTarget in (CanonicalBehaviorStates); // exclude missing/unknown/invalid — never a legitimate class
let joined = predictions
| join kind=inner targets on SessionId, UserId, Horizon
| where WindowIndex == SourceWindowIndex
| extend PredictedNormalized = NormalizeBehaviorState(PredictedBehaviorState)
| project ModelVersion, Horizon, Actual = NormalizedTarget, Predicted = PredictedNormalized;
let modelHorizons = joined | summarize by ModelVersion, Horizon;
let classUniverse = modelHorizons | extend ClassLabel = CanonicalBehaviorStates | mv-expand ClassLabel to typeof(string);
let cellUniverse = classUniverse
| project ModelVersion, Horizon, ActualClass = tostring(ClassLabel)
| join kind=inner (classUniverse | project ModelVersion, Horizon, PredictedClass = tostring(ClassLabel)) on ModelVersion, Horizon
| project ModelVersion, Horizon, ActualClass, PredictedClass;
let counts = joined
| summarize Count = count() by ModelVersion, Horizon, ActualClass = Actual, PredictedClass = Predicted;
cellUniverse
| join kind=leftouter counts on ModelVersion, Horizon, ActualClass, PredictedClass
| project ModelVersion, Horizon, ActualClass, PredictedClass, Count = coalesce(Count, 0)
| order by ModelVersion asc, Horizon asc, ActualClass asc, PredictedClass asc";

    public async Task<DashboardQueryResult<BehaviorStateConfusionMatrixCell>> GetBehaviorStateConfusionMatrixAsync(
        string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — behavior-state confusion matrix unavailable.");
            return new DashboardQueryResult<BehaviorStateConfusionMatrixCell>
            {
                Status = DashboardQueryStatus.ServiceUnavailable,
                ErrorMessage = "ADX is not configured in this environment.",
            };
        }

        var kql = BuildBehaviorStateConfusionMatrixKql(simId, scenarioId, modelVersion, horizon);

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<BehaviorStateConfusionMatrixCell>();
            while (reader.Read())
            {
                results.Add(new BehaviorStateConfusionMatrixCell
                {
                    ModelVersion   = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Horizon        = reader.GetInt32(1),
                    ActualClass    = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    PredictedClass = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Count          = (int)reader.GetInt64(4),
                });
            }
            return new DashboardQueryResult<BehaviorStateConfusionMatrixCell> { Status = DashboardQueryStatus.Success, Rows = results };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Behavior-state confusion-matrix query failed.");
            return new DashboardQueryResult<BehaviorStateConfusionMatrixCell>
            {
                Status = ClassifyQueryException(ex),
                ErrorMessage = ex.Message,
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ReadDynamicAsString(System.Data.IDataReader reader, int index)
    {
        try
        {
            if (reader.IsDBNull(index)) return "null";
            var raw = reader.GetValue(index);
            return raw switch
            {
                string s => s,
                null     => "null",
                _        => raw.ToString() ?? "null"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard: failed to read dynamic column at index {Index}.", index);
            return "null";
        }
    }

    private static string Sanitize(string value) => value.Replace("'", "");

    // Stage 11: builds an optional `| where ... \n` clause narrowing TemporalBehaviorStatePredictions
    // to one simulation/scenario/model-version/horizon combination — any argument left null/blank
    // is not filtered on ("All simulations" = simId: null). Trailing newline keeps the caller's
    // string concatenation readable regardless of whether any filter was actually emitted.
    private static string BuildPredictionFilterClause(string? simId, string? scenarioId, string? modelVersion, int? horizon)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(simId)) filters.Add($"SimId == '{Sanitize(simId)}'");
        if (!string.IsNullOrWhiteSpace(scenarioId)) filters.Add($"ScenarioId == '{Sanitize(scenarioId)}'");
        if (!string.IsNullOrWhiteSpace(modelVersion)) filters.Add($"ModelVersion == '{Sanitize(modelVersion)}'");
        if (horizon.HasValue) filters.Add($"Horizon == {horizon.Value}");
        return filters.Count > 0 ? $"| where {string.Join(" and ", filters)}\n" : "";
    }

    // Stage 11: classifies a failed ADX query so the API/UI can distinguish "genuinely zero
    // rows" from "the query never actually ran." Best-effort — Kusto client exception shapes
    // are matched by message content, since no live cluster was available to verify exact
    // exception types/messages during this pass (see the final report's "live validation
    // pending" section). Never over-claims: anything not clearly auth- or schema-related falls
    // back to the generic QueryFailed status rather than being guessed.
    private static DashboardQueryStatus ClassifyQueryException(Exception ex)
    {
        var message = ex.Message ?? "";
        if (message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("403", StringComparison.Ordinal) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase) && message.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardQueryStatus.AuthorizationFailed;
        }

        if (message.Contains("Failed to resolve table", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Failed to resolve column", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Failed to resolve scalar expression", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("SEM0100", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardQueryStatus.SchemaMissing;
        }

        return DashboardQueryStatus.QueryFailed;
    }
}

// ── Timeline assembly — public so tests can exercise it directly ──────────────

public static class DashboardTimelineBuilder
{
    public static SessionTimelineDto Build(
        string sessionId,
        List<DashboardTrainingRowResult> trainingRows,
        List<DashboardAdaptationResult> adaptations,
        List<DashboardRiskPredictionResult> predictions,
        List<DashboardElevatedRiskPredictionResult> elevatedPredictions,
        List<DashboardBehaviorStatePredictionResult> behaviorStatePredictions,
        List<DashboardTargetResult> targets)
    {
        var predsByWindow = predictions
            .GroupBy(p => p.WindowIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var elevatedPredsByWindow = elevatedPredictions
            .GroupBy(p => p.WindowIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var behaviorStatePredsByWindow = behaviorStatePredictions
            .GroupBy(p => p.WindowIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var targetsBySource = targets
            .GroupBy(t => t.SourceWindowIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var adaptationTolerance = TimeSpan.FromSeconds(5);

        var windows = trainingRows.Select(row =>
        {
            var windowPreds = predsByWindow.TryGetValue(row.WindowIndex, out var p)
                ? p.Select(MapPrediction).ToList()
                : new List<TemporalRiskPredictionTimelineDto>();

            var windowElevated = elevatedPredsByWindow.TryGetValue(row.WindowIndex, out var e)
                ? e.Select(MapElevatedPrediction).ToList()
                : new List<TemporalElevatedRiskPredictionTimelineDto>();

            var windowBehaviorState = behaviorStatePredsByWindow.TryGetValue(row.WindowIndex, out var b)
                ? b.Select(MapBehaviorStatePrediction).ToList()
                : new List<TemporalBehaviorStatePredictionTimelineDto>();

            var windowTargets = targetsBySource.TryGetValue(row.WindowIndex, out var t)
                ? t.Select(MapTarget).ToList()
                : new List<TemporalTargetTimelineDto>();

            var nearest = FindNearestAdaptation(row, adaptations, adaptationTolerance);

            return new WindowTimelineDto
            {
                WindowIndex                  = row.WindowIndex,
                CreatedAtUtc                 = row.CreatedAtUtc,
                UserId                       = row.UserId,
                SimId                        = row.SimId,
                ScenarioId                   = row.ScenarioId,
                ConfusionScore               = row.ConfusionScore,
                GoalUnderstanding            = row.GoalUnderstanding,
                HintDependenceScore          = row.HintDependenceScore,
                GuardrailOverallBehaviorState   = row.GuardrailOverallBehaviorState,
                GuardrailHintDependenceRiskLevel = row.GuardrailHintDependenceRiskLevel,
                SequenceWindowCount          = row.SequenceWindowCount,
                VolatilityScore              = row.VolatilityScore,
                MomentumScore                = row.MomentumScore,
                StabilityScore               = row.StabilityScore,
                PredictedNearTermRisk        = row.PredictedNearTermRisk,
                AdaptationDecision           = nearest is not null ? MapAdaptation(nearest) : null,
                TemporalRiskPredictions      = windowPreds,
                ElevatedRiskPredictions      = windowElevated,
                BehaviorStatePredictions     = windowBehaviorState,
                TemporalPredictionTargets    = windowTargets,
                H1PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 1),
                H2PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 2),
                H3PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 3),
                H1BehaviorStateCorrect       = ComputeBehaviorStateCorrectness(windowBehaviorState, windowTargets, 1),
                H2BehaviorStateCorrect       = ComputeBehaviorStateCorrectness(windowBehaviorState, windowTargets, 2),
                H3BehaviorStateCorrect       = ComputeBehaviorStateCorrectness(windowBehaviorState, windowTargets, 3),
            };
        }).ToList();

        int totalAdaptations = windows.Count(w => w.AdaptationDecision is not null);
        int totalPredictions  = windows.Sum(w => w.TemporalRiskPredictions.Count);

        double? avgConfidence = totalPredictions > 0
            ? windows.SelectMany(w => w.TemporalRiskPredictions).Average(p => p.Confidence)
            : null;

        return new SessionTimelineDto
        {
            SessionId                  = sessionId,
            TotalWindows               = windows.Count,
            TotalAdaptations           = totalAdaptations,
            TotalPredictions           = totalPredictions,
            AveragePredictionConfidence = avgConfidence,
            H1PredictionAccuracy       = HorizonAccuracy(windows, 1),
            H2PredictionAccuracy       = HorizonAccuracy(windows, 2),
            H3PredictionAccuracy       = HorizonAccuracy(windows, 3),
            H1BehaviorStateAccuracy    = BehaviorStateHorizonAccuracy(windows, 1),
            H2BehaviorStateAccuracy    = BehaviorStateHorizonAccuracy(windows, 2),
            H3BehaviorStateAccuracy    = BehaviorStateHorizonAccuracy(windows, 3),
            Windows                    = windows,
        };
    }

    public static bool? ComputeCorrectness(
        List<TemporalRiskPredictionTimelineDto> windowPredictions,
        List<TemporalTargetTimelineDto> windowTargets,
        int horizon)
    {
        var pred   = windowPredictions.FirstOrDefault(p => p.Horizon == horizon);
        var target = windowTargets.FirstOrDefault(t => t.Horizon == horizon);

        if (pred is null || target is null || string.IsNullOrEmpty(target.TargetNearTermRisk))
            return null;

        return pred.PredictedNearTermRisk == target.TargetNearTermRisk;
    }

    // Behavior-state correctness. Runs both sides through BehaviorStateLabelPolicy rather than
    // comparing raw literals — a blank, explicit "unknown", or unrecognized target is never
    // counted as an incorrect prediction (pending/no-signal/invalid are all excluded, matching
    // BehaviorStateLabelPolicy's rule that these are distinct from a wrong answer), and a
    // legacy-aliased or differently-cased value (e.g. "Steady", " stable ") on either side is
    // normalized to its canonical form before comparing so it isn't spuriously marked wrong.
    public static bool? ComputeBehaviorStateCorrectness(
        List<TemporalBehaviorStatePredictionTimelineDto> windowPredictions,
        List<TemporalTargetTimelineDto> windowTargets,
        int horizon)
    {
        var pred   = windowPredictions.FirstOrDefault(p => p.Horizon == horizon);
        var target = windowTargets.FirstOrDefault(t => t.Horizon == horizon);
        if (pred is null || target is null) return null;

        var targetResult = BehaviorStateLabelPolicy.Normalize(target.TargetBehaviorState);
        if (!targetResult.IsTrainable) return null; // missing / unknown / invalid target = pending, not "incorrect"

        var predResult = BehaviorStateLabelPolicy.Normalize(pred.PredictedBehaviorState);
        if (!predResult.IsTrainable) return false; // a non-canonical prediction can never be judged "correct"

        return string.Equals(predResult.CanonicalLabel, targetResult.CanonicalLabel, StringComparison.Ordinal);
    }

    // ── Private mappers ───────────────────────────────────────────────────────

    private static AdaptationDecisionTimelineDto MapAdaptation(DashboardAdaptationResult a) =>
        new()
        {
            CreatedAtUtc            = a.CreatedAtUtc,
            DecisionIndex           = a.DecisionIndex,
            InterventionFamiliesJson = a.InterventionFamiliesJson,
            ParameterChangesJson    = a.ParameterChangesJson,
            ReasoningSummary        = a.ReasoningSummary,
            Phase8AGuardrailNotes   = a.Phase8AGuardrailNotes,
            Phase8EReliabilityNotes = a.Phase8EReliabilityNotes,
            Phase10TrajectoryNotes  = a.Phase10TrajectoryNotes,
            HypothesisLabelsJson    = a.HypothesisLabelsJson,
            LearnerStateSummary     = a.LearnerStateSummary,
            WhySupportChanged       = a.WhySupportChanged,
            CoachMessage            = a.CoachMessage,
            ConfidenceNote          = a.ConfidenceNote,
        };

    private static DashboardAdaptationResult? FindNearestAdaptation(
        DashboardTrainingRowResult row,
        List<DashboardAdaptationResult> adaptations,
        TimeSpan tolerance)
    {
        DashboardAdaptationResult? best = null;
        TimeSpan bestDiff = TimeSpan.MaxValue;

        foreach (var a in adaptations)
        {
            var diff = (a.CreatedAtUtc - row.CreatedAtUtc).Duration();
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = a;
            }
        }

        return bestDiff <= tolerance ? best : null;
    }

    private static TemporalRiskPredictionTimelineDto MapPrediction(DashboardRiskPredictionResult p) =>
        new()
        {
            Horizon               = p.Horizon,
            PredictedNearTermRisk = p.PredictedNearTermRisk,
            Confidence            = p.Confidence,
            ModelVersion          = p.ModelVersion,
        };

    private static TemporalElevatedRiskPredictionTimelineDto MapElevatedPrediction(
        DashboardElevatedRiskPredictionResult p) =>
        new()
        {
            Horizon               = p.Horizon,
            PredictedElevatedRisk = p.PredictedElevatedRisk,
            Confidence            = p.Confidence,
            ModelVersion          = p.ModelVersion,
        };

    private static TemporalBehaviorStatePredictionTimelineDto MapBehaviorStatePrediction(
        DashboardBehaviorStatePredictionResult p) =>
        new()
        {
            Horizon                = p.Horizon,
            PredictedBehaviorState = p.PredictedBehaviorState,
            Confidence             = p.Confidence,
            ModelVersion           = p.ModelVersion,
        };

    private static TemporalTargetTimelineDto MapTarget(DashboardTargetResult t) =>
        new()
        {
            Horizon                   = t.Horizon,
            TargetWindowIndex         = t.TargetWindowIndex,
            TargetNearTermRisk        = t.TargetNearTermRisk,
            TargetBehaviorState       = t.TargetBehaviorState,
            TargetConfusionScore      = t.TargetConfusionScore,
            TargetGoalUnderstanding   = t.TargetGoalUnderstanding,
            TargetHintDependenceScore = t.TargetHintDependenceScore,
        };

    private static double? HorizonAccuracy(List<WindowTimelineDto> windows, int horizon)
    {
        var results = windows
            .Select(w => horizon == 1 ? w.H1PredictionCorrect
                       : horizon == 2 ? w.H2PredictionCorrect
                       :                w.H3PredictionCorrect)
            .Where(c => c.HasValue)
            .ToList();

        return results.Count > 0
            ? (double)results.Count(c => c == true) / results.Count
            : null;
    }

    private static double? BehaviorStateHorizonAccuracy(List<WindowTimelineDto> windows, int horizon)
    {
        var results = windows
            .Select(w => horizon == 1 ? w.H1BehaviorStateCorrect
                       : horizon == 2 ? w.H2BehaviorStateCorrect
                       :                w.H3BehaviorStateCorrect)
            .Where(c => c.HasValue)
            .ToList();

        return results.Count > 0
            ? (double)results.Count(c => c == true) / results.Count
            : null;
    }
}
