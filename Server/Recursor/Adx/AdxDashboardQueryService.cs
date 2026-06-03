using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxDashboardQueryService
{
    Task<List<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20);
    Task<SessionTimelineDto?> GetSessionTimelineAsync(string sessionId);
    // Phase 10D-4: compares elevated-risk model versions by joining predictions to observed targets.
    Task<List<ElevatedRiskModelComparisonRow>> GetElevatedRiskModelComparisonAsync();
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
    string? Phase10TrajectoryNotes);

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
    private readonly ILogger<AdxDashboardQueryService> _logger;

    public AdxDashboardQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxDashboardQueryService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
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

    public async Task<List<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — dashboard recent-sessions returning empty.");
            return [];
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard recent-sessions query failed.");
            return [];
        }
    }

    public async Task<SessionTimelineDto?> GetSessionTimelineAsync(string sessionId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — dashboard session-timeline returning null.");
            return null;
        }

        var sid = Sanitize(sessionId);

        var rows        = await QueryTrainingRowsAsync(sid);
        if (rows.Count == 0) return null;

        var adaptations      = await QueryAdaptationsAsync(sid);
        var predictions      = await QueryRiskPredictionsAsync(sid);
        var elevatedPreds    = await QueryElevatedRiskPredictionsAsync(sid);
        var targets          = await QueryTargetsAsync(sid);

        return DashboardTimelineBuilder.Build(sessionId, rows, adaptations, predictions, elevatedPreds, targets);
    }

    // ── Private ADX query methods ─────────────────────────────────────────────

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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard training-rows query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
    }

    private async Task<List<DashboardAdaptationResult>> QueryAdaptationsAsync(string sanitizedSessionId)
    {
        var kql = $@"AdaptationDecisions_v2
| where SessionId == '{sanitizedSessionId}'
| order by CreatedAtUtc asc
| project CreatedAtUtc, DecisionIndex, InterventionFamilies, ParameterChanges,
          ReasoningSummary, Phase8AGuardrailNotes, Phase8EReliabilityNotes, Phase10TrajectoryNotes";

        try
        {
            using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<DashboardAdaptationResult>();
            while (reader.Read())
            {
                results.Add(new DashboardAdaptationResult(
                    CreatedAtUtc:            reader.GetDateTime(0),
                    DecisionIndex:           reader.GetInt32(1),
                    InterventionFamiliesJson: ReadDynamicAsString(reader, 2),
                    ParameterChangesJson:    ReadDynamicAsString(reader, 3),
                    ReasoningSummary:        reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Phase8AGuardrailNotes:   reader.IsDBNull(5) ? null : reader.GetString(5),
                    Phase8EReliabilityNotes: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Phase10TrajectoryNotes:  reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard adaptations query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
    }

    private async Task<List<DashboardRiskPredictionResult>> QueryRiskPredictionsAsync(string sanitizedSessionId)
    {
        var kql = $@"TemporalRiskPredictions
| where SessionId == '{sanitizedSessionId}'
| order by WindowIndex asc, Horizon asc
| project WindowIndex, Horizon, PredictedNearTermRisk, Confidence, ModelVersion";

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard risk-predictions query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
    }

    private async Task<List<DashboardElevatedRiskPredictionResult>> QueryElevatedRiskPredictionsAsync(
        string sanitizedSessionId)
    {
        var kql = $@"TemporalElevatedRiskPredictions
| where SessionId == '{sanitizedSessionId}'
| order by WindowIndex asc, Horizon asc
| project WindowIndex, Horizon, PredictedElevatedRisk, Confidence, ModelVersion";

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard elevated-risk-predictions query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
    }

    private async Task<List<DashboardTargetResult>> QueryTargetsAsync(string sanitizedSessionId)
    {
        var kql = $@"TemporalPredictionTargets
| where SessionId == '{sanitizedSessionId}'
| order by SourceWindowIndex asc, Horizon asc
| project SourceWindowIndex, TargetWindowIndex, Horizon,
          TargetNearTermRisk, TargetBehaviorState,
          TargetConfusionScore, TargetGoalUnderstanding, TargetHintDependenceScore";

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard targets query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
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

    public async Task<List<ElevatedRiskModelComparisonRow>> GetElevatedRiskModelComparisonAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — elevated-risk model-comparison returning empty.");
            return [];
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elevated-risk model-comparison query failed.");
            return [];
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
        List<DashboardTargetResult> targets)
    {
        var predsByWindow = predictions
            .GroupBy(p => p.WindowIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        var elevatedPredsByWindow = elevatedPredictions
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
                TemporalPredictionTargets    = windowTargets,
                H1PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 1),
                H2PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 2),
                H3PredictionCorrect          = ComputeCorrectness(windowPreds, windowTargets, 3),
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

    // ── Private mappers ───────────────────────────────────────────────────────

    private static AdaptationDecisionTimelineDto MapAdaptation(DashboardAdaptationResult a) =>
        new()
        {
            CreatedAtUtc           = a.CreatedAtUtc,
            DecisionIndex          = a.DecisionIndex,
            InterventionFamiliesJson = a.InterventionFamiliesJson,
            ParameterChangesJson   = a.ParameterChangesJson,
            ReasoningSummary       = a.ReasoningSummary,
            Phase8AGuardrailNotes  = a.Phase8AGuardrailNotes,
            Phase8EReliabilityNotes = a.Phase8EReliabilityNotes,
            Phase10TrajectoryNotes = a.Phase10TrajectoryNotes,
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
}
