using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// ── User Baseline Query Service ───────────────────────────────────────────────
// Computes a UserBaselineSnapshot from BehaviorStateTrainingRows for a given
// user. This is a read-only longitudinal analytics service — it does NOT affect
// the live adaptation pipeline.
//
// Future: the snapshot returned here may be compared against current-window
// behavior to enable personalized adaptation (e.g., per-user guardrail offsets).
// ─────────────────────────────────────────────────────────────────────────────

public interface IAdxUserBaselineQueryService
{
    /// <summary>
    /// Returns a longitudinal behavioral snapshot for the given user,
    /// computed from BehaviorStateTrainingRows across all sessions.
    /// <para>
    /// <paramref name="recentWindowCount"/> controls how many of the most recent
    /// windows contribute to the RecentAvg* fields (default: 10).
    /// </para>
    /// Returns <c>null</c> when no rows exist for the user, or when ADX is not configured.
    /// </summary>
    Task<UserBaselineSnapshot?> GetUserBaselineAsync(string userId, int recentWindowCount = 10);

    /// <summary>
    /// Returns the most recent BehaviorStateTrainingRow for the given user,
    /// projected to the columns needed for user-relative signal computation.
    /// Returns <c>null</c> when no rows exist, or when ADX is not configured.
    /// </summary>
    Task<LatestUserTrainingRow?> GetLatestTrainingRowByUserAsync(string userId);
}

public class AdxUserBaselineQueryService : IAdxUserBaselineQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxUserBaselineQueryService> _logger;

    public AdxUserBaselineQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxUserBaselineQueryService> logger)
    {
        // GetService returns null when ADX is not configured (no ClusterUri registered).
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
        _logger = logger;
    }

    public async Task<UserBaselineSnapshot?> GetUserBaselineAsync(string userId, int recentWindowCount = 10)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping user baseline query.");
            return null;
        }

        var sanitized = Sanitize(userId);
        var recentN   = Math.Clamp(recentWindowCount, 1, 500);

        // ── Query 1: lifetime aggregates ──────────────────────────────────────
        // Summarizes all rows for the user to produce snapshot-level stats.
        var lifetimeKql = $@"BehaviorStateTrainingRows
| where UserId == '{sanitized}'
| summarize
    SampleWindowCount     = count(),
    StartUtc              = min(CreatedAtUtc),
    LastSeenUtc           = max(CreatedAtUtc),
    AvgConfusionScore     = avg(ConfusionScore),
    AvgHintDependenceScore = avg(HintDependenceScore),
    AvgHesitationScore    = avg(HesitationScore),
    AvgGoalUnderstanding  = avg(GoalUnderstanding),
    AvgAttentionDetection = avg(AttentionDetection),
    AvgProcedureSequencing = avg(ProcedureSequencing),
    AvgSelfCorrection     = avg(SelfCorrection),
    AvgTaskContinuity     = avg(TaskContinuity),
    StableMasteryRate     = avg(todouble(LabelStableMastery)),
    ConfusionRate         = avg(todouble(LabelConfusion)),
    HintDependenceRate    = avg(todouble(LabelHintDependence))";

        long sampleWindowCount;
        DateTime startUtc, lastSeenUtc;
        double avgConfusion, avgHintDependence, avgHesitation;
        double avgGoal, avgAttention, avgProcedure, avgSelfCorrection, avgTaskContinuity;
        double stableMasteryRate, confusionRate, hintDependenceRate;

        try
        {
            using var r = await _queryProvider.ExecuteQueryAsync(_database, lifetimeKql, new ClientRequestProperties());

            if (!r.Read())
                return null;

            sampleWindowCount = r.GetInt64(0);

            if (sampleWindowCount == 0)
                return null;

            startUtc      = r.GetDateTime(1);
            lastSeenUtc   = r.GetDateTime(2);
            avgConfusion  = ReadDoubleSafe(r, 3);
            avgHintDependence = ReadDoubleSafe(r, 4);
            avgHesitation = ReadDoubleSafe(r, 5);
            avgGoal       = ReadDoubleSafe(r, 6);
            avgAttention  = ReadDoubleSafe(r, 7);
            avgProcedure  = ReadDoubleSafe(r, 8);
            avgSelfCorrection = ReadDoubleSafe(r, 9);
            avgTaskContinuity = ReadDoubleSafe(r, 10);
            stableMasteryRate = ReadDoubleSafe(r, 11);
            confusionRate = ReadDoubleSafe(r, 12);
            hintDependenceRate = ReadDoubleSafe(r, 13);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADX user baseline lifetime query failed for userId '{UserId}'.", userId);
            return null;
        }

        // ── Query 2: recent-window averages ───────────────────────────────────
        // Restricts to the N most recent windows to capture current behavioral state.
        var recentKql = $@"BehaviorStateTrainingRows
| where UserId == '{sanitized}'
| top {recentN} by CreatedAtUtc desc
| summarize
    RecentAvgConfusionScore     = avg(ConfusionScore),
    RecentAvgHintDependenceScore = avg(HintDependenceScore),
    RecentAvgGoalUnderstanding  = avg(GoalUnderstanding),
    RecentAvgAttentionDetection = avg(AttentionDetection)";

        double recentConfusion, recentHintDependence, recentGoal, recentAttention;

        try
        {
            using var r = await _queryProvider.ExecuteQueryAsync(_database, recentKql, new ClientRequestProperties());

            if (r.Read())
            {
                recentConfusion      = ReadDoubleSafe(r, 0);
                recentHintDependence = ReadDoubleSafe(r, 1);
                recentGoal           = ReadDoubleSafe(r, 2);
                recentAttention      = ReadDoubleSafe(r, 3);
            }
            else
            {
                // No recent rows — fall back to lifetime averages.
                recentConfusion      = avgConfusion;
                recentHintDependence = avgHintDependence;
                recentGoal           = avgGoal;
                recentAttention      = avgAttention;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADX user baseline recent-window query failed for userId '{UserId}' — using lifetime averages.", userId);
            recentConfusion      = avgConfusion;
            recentHintDependence = avgHintDependence;
            recentGoal           = avgGoal;
            recentAttention      = avgAttention;
        }

        return new UserBaselineSnapshot
        {
            UserId                     = userId,
            SampleWindowCount          = sampleWindowCount,
            StartUtc                   = startUtc,
            EndUtc                     = lastSeenUtc,
            LastSeenUtc                = lastSeenUtc,
            AvgConfusionScore          = avgConfusion,
            AvgHintDependenceScore     = avgHintDependence,
            AvgHesitationScore         = avgHesitation,
            AvgGoalUnderstanding       = avgGoal,
            AvgAttentionDetection      = avgAttention,
            AvgProcedureSequencing     = avgProcedure,
            AvgSelfCorrection          = avgSelfCorrection,
            AvgTaskContinuity          = avgTaskContinuity,
            StableMasteryRate          = stableMasteryRate,
            ConfusionRate              = confusionRate,
            HintDependenceRate         = hintDependenceRate,
            RecentAvgConfusionScore    = recentConfusion,
            RecentAvgHintDependenceScore = recentHintDependence,
            RecentAvgGoalUnderstanding = recentGoal,
            RecentAvgAttentionDetection = recentAttention
        };
    }

    public async Task<LatestUserTrainingRow?> GetLatestTrainingRowByUserAsync(string userId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping latest training row query.");
            return null;
        }

        var sanitized = Sanitize(userId);

        var kql = $@"BehaviorStateTrainingRows
| where UserId == '{sanitized}'
| top 1 by CreatedAtUtc desc
| project SessionId, UserId, WindowIndex, CreatedAtUtc,
          ConfusionScore, HintDependenceScore,
          GoalUnderstanding, AttentionDetection, ProcedureSequencing, SelfCorrection, TaskContinuity";

        try
        {
            using var r = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

            if (!r.Read())
                return null;

            return new LatestUserTrainingRow
            {
                SessionId           = r.GetString(0),
                UserId              = r.GetString(1),
                WindowIndex         = r.GetInt32(2),
                CreatedAtUtc        = r.GetDateTime(3),
                ConfusionScore      = ReadDoubleSafe(r, 4),
                HintDependenceScore = ReadDoubleSafe(r, 5),
                GoalUnderstanding   = ReadDoubleSafe(r, 6),
                AttentionDetection  = ReadDoubleSafe(r, 7),
                ProcedureSequencing = ReadDoubleSafe(r, 8),
                SelfCorrection      = ReadDoubleSafe(r, 9),
                TaskContinuity      = ReadDoubleSafe(r, 10)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADX latest training row query failed for userId '{UserId}'.", userId);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns 0.0 when the ADX column is null (e.g. avg with no matching rows).
    private static double ReadDoubleSafe(System.Data.IDataReader reader, int index) =>
        reader.IsDBNull(index) ? 0.0 : reader.GetDouble(index);

    // Strips single quotes to prevent KQL injection.
    // Consistent with the approach used in AdxRecursorQueryService.
    private static string Sanitize(string value) =>
        value.Replace("'", "");
}
