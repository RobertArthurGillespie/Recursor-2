using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// ADX return types for the debrief pipeline.

public class MedicalSupplyRawEventCounts
{
    public int WrongBinErrors { get; set; }
    public int DamagedSupplyStocked { get; set; }
    public int UsableSupplyRejected { get; set; }
    public int SafetyErrors { get; set; }
    public int SuccessCount { get; set; }
}

public class MedicalSupplyBehaviorSummary
{
    public double AvgConfusion { get; set; }
    public double AvgSafetyCompliance { get; set; }
    public double AvgGoalUnderstanding { get; set; }
    public string DominantState { get; set; } = "";
    public int WindowCount { get; set; }
}

public interface IAdxMedicalSupplyDebriefQueryService
{
    Task<MedicalSupplyRawEventCounts> GetRawEventCountsAsync(string sessionId);
    Task<MedicalSupplyBehaviorSummary?> GetBehaviorSummaryAsync(string sessionId);
    Task<string?> GetPreviousSessionIdAsync(string userId, string simId, string scenarioId, string currentSessionId);
}

public class AdxMedicalSupplyDebriefQueryService : IAdxMedicalSupplyDebriefQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxMedicalSupplyDebriefQueryService> _logger;

    public AdxMedicalSupplyDebriefQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxMedicalSupplyDebriefQueryService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
        _logger = logger;
    }

    // Counts specific medical-supply event types for a session.
    // KQL summarize with no |by always returns exactly one row, so empty sessions
    // produce a row with all zeros rather than an empty result set.
    public async Task<MedicalSupplyRawEventCounts> GetRawEventCountsAsync(string sessionId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — returning empty event counts.");
            return new MedicalSupplyRawEventCounts();
        }

        var s = Sanitize(sessionId);
        var kql = $@"RawEvents
| where SessionId == '{s}'
| summarize
    WrongBinErrors        = countif(EventType == 'wrong_bin_error'),
    DamagedSupplyStocked  = countif(EventType == 'damaged_supply_stocked'),
    UsableSupplyRejected  = countif(EventType == 'usable_supply_rejected'),
    SafetyErrors          = countif(EventType in ('damaged_supply_stocked', 'expired_supply_stocked', 'inspection_skipped')),
    SuccessCount          = countif(EventType in ('supply_stocked_correctly', 'damaged_supply_rejected_correctly', 'expired_supply_rejected_correctly'))";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            if (!reader.Read())
                return new MedicalSupplyRawEventCounts();

            return new MedicalSupplyRawEventCounts
            {
                WrongBinErrors       = reader.GetInt32(0),
                DamagedSupplyStocked = reader.GetInt32(1),
                UsableSupplyRejected = reader.GetInt32(2),
                SafetyErrors         = reader.GetInt32(3),
                SuccessCount         = reader.GetInt32(4),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetRawEventCountsAsync failed for sessionId '{SessionId}'.", sessionId);
            return new MedicalSupplyRawEventCounts();
        }
    }

    // Returns averaged behavior scores across all windows for a session.
    // Returns null when no BehaviorStateTrainingRows exist for the session.
    public async Task<MedicalSupplyBehaviorSummary?> GetBehaviorSummaryAsync(string sessionId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping behavior summary.");
            return null;
        }

        var s = Sanitize(sessionId);

        var kqlAverages = $@"BehaviorStateTrainingRows
| where SessionId == '{s}'
| summarize
    AvgConfusion         = avg(ConfusionScore),
    AvgSafetyCompliance  = avg(SafetyCompliance),
    AvgGoalUnderstanding = avg(GoalUnderstanding),
    WindowCount          = count()";

        var kqlState = $@"BehaviorStateTrainingRows
| where SessionId == '{s}'
| where isnotempty(GuardrailOverallBehaviorState)
| top 1 by CreatedAtUtc desc
| project GuardrailOverallBehaviorState";

        try
        {
            double avgConfusion = 0, avgSafety = 0, avgGoal = 0;
            int windowCount = 0;

            using (var reader = await _queryProvider.ExecuteQueryAsync(_database, kqlAverages, new ClientRequestProperties()))
            {
                if (!reader.Read())
                    return null;

                windowCount = (int)reader.GetInt64(3);
                if (windowCount == 0)
                    return null;

                avgConfusion = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                avgSafety    = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                avgGoal      = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            }

            string dominantState = "";
            using (var stateReader = await _queryProvider.ExecuteQueryAsync(_database, kqlState, new ClientRequestProperties()))
            {
                if (stateReader.Read() && !stateReader.IsDBNull(0))
                    dominantState = stateReader.GetString(0);
            }

            return new MedicalSupplyBehaviorSummary
            {
                AvgConfusion         = avgConfusion,
                AvgSafetyCompliance  = avgSafety,
                AvgGoalUnderstanding = avgGoal,
                DominantState        = dominantState,
                WindowCount          = windowCount,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetBehaviorSummaryAsync failed for sessionId '{SessionId}'.", sessionId);
            return null;
        }
    }

    // Finds the most recent session ID for the same userId/simId/scenarioId that is
    // not the currentSessionId. Returns null when no prior session exists.
    public async Task<string?> GetPreviousSessionIdAsync(
        string userId, string simId, string scenarioId, string currentSessionId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured — skipping previous session lookup.");
            return null;
        }

        var kql = $@"BehaviorStateTrainingRows
| where UserId    == '{Sanitize(userId)}'
| where SimId     == '{Sanitize(simId)}'
| where ScenarioId == '{Sanitize(scenarioId)}'
| where SessionId  != '{Sanitize(currentSessionId)}'
| summarize LastWindow = max(CreatedAtUtc) by SessionId
| top 1 by LastWindow desc
| project SessionId";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            if (!reader.Read() || reader.IsDBNull(0))
                return null;

            return reader.GetString(0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GetPreviousSessionIdAsync failed for userId '{UserId}', simId '{SimId}'.",
                userId, simId);
            return null;
        }
    }

    private static string Sanitize(string value) => value.Replace("'", "");
}
