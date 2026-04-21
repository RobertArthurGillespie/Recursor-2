using System.Globalization;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

/// <summary>
/// Filters applied when exporting BehaviorStateTrainingRows from ADX.
/// All fields are optional — omitting a field means "no restriction on that dimension".
/// </summary>
public class BehaviorStateTrainingExportFilter
{
    /// <summary>Only include rows ingested at or after this UTC timestamp.</summary>
    public DateTime? StartUtc { get; set; }

    /// <summary>Only include rows ingested strictly before this UTC timestamp.</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>Restrict to a specific InferenceMode value (e.g. "shadow-mlnet").</summary>
    public string? InferenceMode { get; set; }

    /// <summary>Restrict to a specific ModelVersion value (e.g. "mlnet-multi-v1").</summary>
    public string? ModelVersion { get; set; }

    /// <summary>Restrict to rows from any of these SimIds. Null or empty = all sims.</summary>
    public List<string>? SimIds { get; set; }

    /// <summary>Restrict to rows from any of these ScenarioIds. Null or empty = all scenarios.</summary>
    public List<string>? ScenarioIds { get; set; }
}

public interface IAdxTrainingExportService
{
    /// <summary>
    /// Exports all BehaviorStateTrainingRows matching the given filter from ADX.
    /// Returns an empty list when ADX is not configured or no rows match.
    /// </summary>
    Task<List<BehaviorStateTrainingRow>> ExportCleanRowsAsync(BehaviorStateTrainingExportFilter filter);
}

public class AdxTrainingExportService : IAdxTrainingExportService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxTrainingExportService> _logger;

    public AdxTrainingExportService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxTrainingExportService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    public async Task<List<BehaviorStateTrainingRow>> ExportCleanRowsAsync(BehaviorStateTrainingExportFilter filter)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning(
                "ADX query provider not configured — skipping BehaviorStateTrainingRows export.");
            return [];
        }

        var kql = BuildKql(filter);

        _logger.LogInformation(
            "Exporting BehaviorStateTrainingRows from ADX (database: {Database}).\nKQL:\n{Kql}",
            _database, kql);

        var results = new List<BehaviorStateTrainingRow>();

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(
                _database, kql, new ClientRequestProperties());

            while (reader.Read())
            {
                results.Add(new BehaviorStateTrainingRow
                {
                    // Identity / metadata  (ordinals match the | project below)
                    SessionId                    = reader.GetString(0),
                    UserId                       = reader.GetString(1),
                    SimId                        = reader.GetString(2),
                    ScenarioId                   = reader.GetString(3),
                    WindowIndex                  = reader.GetInt32(4),
                    TaskType                     = reader.GetString(5),
                    CreatedAtUtc                 = reader.GetDateTime(6),

                    // Dimension scores
                    AttentionDetection           = reader.GetDouble(7),
                    GoalUnderstanding            = reader.GetDouble(8),
                    ProcedureSequencing          = reader.GetDouble(9),
                    PaceRegulation               = reader.GetDouble(10),
                    SelfCorrection               = reader.GetDouble(11),
                    FeedbackResponsiveness       = reader.GetDouble(12),
                    SafetyCompliance             = reader.GetDouble(13),
                    TaskContinuity               = reader.GetDouble(14),

                    // Higher-order behavior scores
                    ConfusionScore               = reader.GetDouble(15),
                    HesitationScore              = reader.GetDouble(16),
                    ImpulsivityScore             = reader.GetDouble(17),
                    HintDependenceScore          = reader.GetDouble(18),

                    // Trajectory features
                    GoalTrend                    = reader.GetDouble(19),
                    AttentionTrend               = reader.GetDouble(20),
                    ConfusionTrend               = reader.GetDouble(21),
                    HintDependenceTrend          = reader.GetDouble(22),

                    // Adaptive state
                    CurrentHintMode              = reader.GetString(23),
                    CurrentDifficulty            = reader.GetDouble(24),
                    CurrentTimePressure          = reader.GetDouble(25),
                    CurrentErrorTolerance        = reader.GetDouble(26),

                    // Counters
                    ConsecutiveStableMasteryWindows = reader.GetInt32(27),
                    ConsecutiveRelapseWindows       = reader.GetInt32(28),

                    // Window summary
                    EventCountInWindow           = reader.GetInt32(29),
                    ErrorCountInWindow           = reader.GetInt32(30),
                    HintCountInWindow            = reader.GetInt32(31),
                    StepCompleteCountInWindow    = reader.GetInt32(32),

                    // Weak labels (stored as int 0/1 in ADX)
                    LabelConfusion               = reader.GetInt32(33),
                    LabelHintDependence          = reader.GetInt32(34),
                    LabelStableMastery           = reader.GetInt32(35),

                    // Shadow prediction columns
                    PredConfusionProbability       = reader.GetDouble(36),
                    PredHintDependenceProbability  = reader.GetDouble(37),
                    PredStableMasteryProbability   = reader.GetDouble(38),

                    // Model metadata
                    ModelVersion                 = reader.GetString(39),
                    InferenceMode                = reader.GetString(40),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BehaviorStateTrainingRows export failed. Filter: StartUtc={StartUtc}, " +
                "EndUtc={EndUtc}, InferenceMode={InferenceMode}, ModelVersion={ModelVersion}.",
                filter.StartUtc, filter.EndUtc, filter.InferenceMode, filter.ModelVersion);
            throw;
        }

        _logger.LogInformation(
            "BehaviorStateTrainingRows export complete: {Count} rows returned.", results.Count);

        return results;
    }

    // ── KQL builder ──────────────────────────────────────────────────────────────

    private static string BuildKql(BehaviorStateTrainingExportFilter filter)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("BehaviorStateTrainingRows");

        if (filter.StartUtc.HasValue)
        {
            var ts = filter.StartUtc.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            sb.AppendLine($"| where CreatedAtUtc >= datetime({ts})");
        }

        if (filter.EndUtc.HasValue)
        {
            var ts = filter.EndUtc.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            sb.AppendLine($"| where CreatedAtUtc < datetime({ts})");
        }

        if (!string.IsNullOrWhiteSpace(filter.InferenceMode))
            sb.AppendLine($"| where InferenceMode == '{Sanitize(filter.InferenceMode)}'");

        if (!string.IsNullOrWhiteSpace(filter.ModelVersion))
            sb.AppendLine($"| where ModelVersion == '{Sanitize(filter.ModelVersion)}'");

        if (filter.SimIds is { Count: > 0 })
        {
            var values = string.Join(", ", filter.SimIds.Select(s => $"'{Sanitize(s)}'"));
            sb.AppendLine($"| where SimId in ({values})");
        }

        if (filter.ScenarioIds is { Count: > 0 })
        {
            var values = string.Join(", ", filter.ScenarioIds.Select(s => $"'{Sanitize(s)}'"));
            sb.AppendLine($"| where ScenarioId in ({values})");
        }

        // Explicit | project ensures column ordinals are stable regardless of live table column order.
        sb.AppendLine("""
| project
    SessionId, UserId, SimId, ScenarioId, WindowIndex, TaskType, CreatedAtUtc,
    AttentionDetection, GoalUnderstanding, ProcedureSequencing, PaceRegulation,
    SelfCorrection, FeedbackResponsiveness, SafetyCompliance, TaskContinuity,
    ConfusionScore, HesitationScore, ImpulsivityScore, HintDependenceScore,
    GoalTrend, AttentionTrend, ConfusionTrend, HintDependenceTrend,
    CurrentHintMode, CurrentDifficulty, CurrentTimePressure, CurrentErrorTolerance,
    ConsecutiveStableMasteryWindows, ConsecutiveRelapseWindows,
    EventCountInWindow, ErrorCountInWindow, HintCountInWindow, StepCompleteCountInWindow,
    LabelConfusion, LabelHintDependence, LabelStableMastery,
    PredConfusionProbability, PredHintDependenceProbability, PredStableMasteryProbability,
    ModelVersion, InferenceMode
| order by SessionId asc, WindowIndex asc
""");

        return sb.ToString();
    }

    private static string Sanitize(string value) => value.Replace("'", "");
}
