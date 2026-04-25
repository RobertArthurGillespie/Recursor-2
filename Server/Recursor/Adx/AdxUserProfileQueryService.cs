using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// ── ADX User Profile Query Service (Phase 6D) ─────────────────────────────────
// Reads the latest UserBehaviorProfile for a given UserId from the
// UserBehaviorProfiles ADX table using arg_max(UpdatedAtUtc, *).
//
// ADX is append-only for profile data. UserProfileUpdateService writes a new
// snapshot row on every behavior window via IAdxIngestionService. This service
// reads the row with the latest UpdatedAtUtc, which is always the most current
// complete profile including any Phase 6C derived thresholds.
//
// Returns null when:
//   - ADX is not configured (no ClusterUri registered)
//   - No profile row exists for the given UserId
//   - The query fails for any reason (logged at Warning; never throws)
// ─────────────────────────────────────────────────────────────────────────────

public interface IAdxUserProfileQueryService
{
    /// <summary>
    /// Returns the latest UserBehaviorProfile for <paramref name="userId"/> from
    /// UserBehaviorProfiles (arg_max on UpdatedAtUtc), or null if not found.
    /// </summary>
    Task<UserBehaviorProfile?> GetLatestProfileAsync(string userId);
}

public class AdxUserProfileQueryService : IAdxUserProfileQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxUserProfileQueryService> _logger;

    public AdxUserProfileQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxUserProfileQueryService> logger)
    {
        // Returns null when ADX ClusterUri is not configured — same pattern as all
        // other Recursor ADX services.
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    public async Task<UserBehaviorProfile?> GetLatestProfileAsync(string userId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning(
                "ADX query provider not configured — skipping user profile lookup for userId '{UserId}'.",
                userId);
            return null;
        }

        var sanitized = Sanitize(userId);

        // Explicit | project fixes column ordinals regardless of live table column order.
        // Ordinals 0-16: identity + behavioral averages/rates (non-nullable)
        // Ordinals 17-26: threshold override fields (nullable — null = use global default)
        var kql = $@"UserBehaviorProfiles
| where UserId == '{sanitized}'
| summarize arg_max(UpdatedAtUtc, *) by UserId
| project UserId, CreatedAtUtc, UpdatedAtUtc, LastSessionId,
          TotalSessions, TotalWindows,
          AvgConfusionScore, AvgHintDependenceScore, AvgHesitationScore,
          AvgGoalUnderstanding, AvgAttentionDetection, AvgProcedureSequencing,
          AvgSelfCorrection, AvgTaskContinuity,
          StableMasteryRate, ConfusionRate, HintDependenceRate,
          ConfusionBlockDifficultyIncreaseThreshold,
          HintFadeConfusionDeltaThreshold, HintFadeHintDependenceDeltaThreshold,
          HintFadeMaxCurrentConfusionScore, HintFadeMinCurrentGoalUnderstanding,
          DifficultyAccelerationConfusionDeltaThreshold, DifficultyAccelerationHintDependenceDeltaThreshold,
          DifficultyAccelerationMaxCurrentConfusionScore, DifficultyAccelerationMinCurrentGoalUnderstanding,
          DifficultyAccelerationDelta";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(
                _database, kql, new ClientRequestProperties());

            if (!reader.Read())
                return null;

            return new UserBehaviorProfile
            {
                UserId        = reader.GetString(0),
                CreatedAtUtc  = reader.GetDateTime(1),
                UpdatedAtUtc  = reader.GetDateTime(2),
                LastSessionId = reader.GetString(3),
                TotalSessions = reader.GetInt32(4),
                TotalWindows  = reader.GetInt32(5),

                // ── Behavioral averages and rates (non-nullable) ──────────────
                AvgConfusionScore      = ReadDouble(reader, 6),
                AvgHintDependenceScore = ReadDouble(reader, 7),
                AvgHesitationScore     = ReadDouble(reader, 8),
                AvgGoalUnderstanding   = ReadDouble(reader, 9),
                AvgAttentionDetection  = ReadDouble(reader, 10),
                AvgProcedureSequencing = ReadDouble(reader, 11),
                AvgSelfCorrection      = ReadDouble(reader, 12),
                AvgTaskContinuity      = ReadDouble(reader, 13),
                StableMasteryRate      = ReadDouble(reader, 14),
                ConfusionRate          = ReadDouble(reader, 15),
                HintDependenceRate     = ReadDouble(reader, 16),

                // ── Threshold overrides (nullable — null means use global default) ─
                ConfusionBlockDifficultyIncreaseThreshold          = ReadNullableDouble(reader, 17),
                HintFadeConfusionDeltaThreshold                    = ReadNullableDouble(reader, 18),
                HintFadeHintDependenceDeltaThreshold               = ReadNullableDouble(reader, 19),
                HintFadeMaxCurrentConfusionScore                   = ReadNullableDouble(reader, 20),
                HintFadeMinCurrentGoalUnderstanding                = ReadNullableDouble(reader, 21),
                DifficultyAccelerationConfusionDeltaThreshold      = ReadNullableDouble(reader, 22),
                DifficultyAccelerationHintDependenceDeltaThreshold = ReadNullableDouble(reader, 23),
                DifficultyAccelerationMaxCurrentConfusionScore     = ReadNullableDouble(reader, 24),
                DifficultyAccelerationMinCurrentGoalUnderstanding  = ReadNullableDouble(reader, 25),
                DifficultyAccelerationDelta                        = ReadNullableDouble(reader, 26),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ADX user profile query failed for userId '{UserId}'.", userId);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns 0.0 when the ADX column is null (e.g. avg across no rows).
    private static double ReadDouble(System.Data.IDataReader reader, int index)
        => reader.IsDBNull(index) ? 0.0 : reader.GetDouble(index);

    // Returns null when the ADX column is null — preserves the "no override set"
    // semantic for threshold fields.
    private static double? ReadNullableDouble(System.Data.IDataReader reader, int index)
        => reader.IsDBNull(index) ? null : reader.GetDouble(index);

    // Strips single quotes to prevent KQL injection.
    // Consistent with the approach used in all Recursor ADX services.
    internal static string Sanitize(string value) =>
        value.Replace("'", "");
}
