using System.Text.Json;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxRecursorQueryService
{
    // Session-scoped queries
    Task<IEnumerable<FeatureWindowRow>> GetLatestFeatureWindowsAsync(string sessionId, int count = 5);
    Task<IEnumerable<BehaviorProfileRow>> GetLatestBehaviorProfilesAsync(string sessionId, int count = 5);
    Task<IEnumerable<HypothesisSetRow>> GetLatestHypothesisSetsAsync(string sessionId, int count = 5);
    Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsAsync(string sessionId, int count = 5);

    // User-scoped queries (longitudinal layer)
    Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsByUserAsync(string userId, int count = 10);
    Task<long> GetTrainingRowCountByUserAsync(string userId);
}

public class AdxRecursorQueryService : IAdxRecursorQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxRecursorQueryService> _logger;

    public AdxRecursorQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxRecursorQueryService> logger)
    {
        // GetService returns null when not registered (ADX not configured).
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    // ── Session-scoped queries ────────────────────────────────────────────────
    // Explicit | project fixes column ordinals regardless of live table column order.

    public async Task<IEnumerable<FeatureWindowRow>> GetLatestFeatureWindowsAsync(string sessionId, int count = 5)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $@"FeatureWindows
| where SessionId == '{Sanitize(sessionId)}'
| order by WindowIndex desc
| take {count}
| project SessionId, UserId, WindowIndex, WindowType,
          WindowStartSequence, WindowEndSequence,
          WindowStartUtc, WindowEndUtc,
          SimId, ScenarioId, FeatureExtractorVersion, Features";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<FeatureWindowRow>();
        while (reader.Read())
        {
            results.Add(new FeatureWindowRow
            {
                SessionId               = reader.GetString(0),
                UserId                  = reader.GetString(1),
                WindowIndex             = reader.GetInt32(2),
                WindowType              = reader.GetString(3),
                WindowStartSequence     = reader.GetInt64(4),
                WindowEndSequence       = reader.GetInt64(5),
                WindowStartUtc          = reader.GetDateTime(6),
                WindowEndUtc            = reader.GetDateTime(7),
                SimId                   = reader.GetString(8),
                ScenarioId              = reader.GetString(9),
                FeatureExtractorVersion = reader.GetString(10),
                Features                = ParseDynamic(reader.GetString(11))
            });
        }

        return results;
    }

    public async Task<IEnumerable<BehaviorProfileRow>> GetLatestBehaviorProfilesAsync(string sessionId, int count = 5)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $@"BehaviorProfiles
| where SessionId == '{Sanitize(sessionId)}'
| order by WindowIndex desc
| take {count}
| project SessionId, UserId, WindowIndex, SourceFeatureWindowId,
          InterpreterVersion, DimensionScores, BehaviorScores, CreatedAtUtc";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<BehaviorProfileRow>();
        while (reader.Read())
        {
            results.Add(new BehaviorProfileRow
            {
                SessionId             = reader.GetString(0),
                UserId                = reader.GetString(1),
                WindowIndex           = reader.GetInt32(2),
                SourceFeatureWindowId = reader.GetString(3),
                InterpreterVersion    = reader.GetString(4),
                DimensionScores       = ParseDynamic(reader.GetString(5)),
                BehaviorScores        = ParseDynamic(reader.GetString(6)),
                CreatedAtUtc          = reader.GetDateTime(7)
            });
        }

        return results;
    }

    public async Task<IEnumerable<HypothesisSetRow>> GetLatestHypothesisSetsAsync(string sessionId, int count = 5)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $@"HypothesisSets
| where SessionId == '{Sanitize(sessionId)}'
| order by WindowIndex desc
| take {count}
| project SessionId, UserId, WindowIndex, SourceBehaviorProfileId,
          InterpreterMode, InterpreterVersion, Hypotheses, CreatedAtUtc";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<HypothesisSetRow>();
        while (reader.Read())
        {
            results.Add(new HypothesisSetRow
            {
                SessionId               = reader.GetString(0),
                UserId                  = reader.GetString(1),
                WindowIndex             = reader.GetInt32(2),
                SourceBehaviorProfileId = reader.GetString(3),
                InterpreterMode         = reader.GetString(4),
                InterpreterVersion      = reader.GetString(5),
                Hypotheses              = ParseDynamic(reader.GetString(6)),
                CreatedAtUtc            = reader.GetDateTime(7)
            });
        }

        return results;
    }

    public async Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsAsync(string sessionId, int count = 5)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $@"AdaptationDecisions_v2
| where SessionId == '{Sanitize(sessionId)}'
| order by DecisionIndex desc
| take {count}
| project SessionId, UserId, DecisionIndex, SourceHypothesisSetId,
          PolicyVersion, InterventionFamilies, ParameterChanges,
          ReasoningSummary, ExpiresAfterWindow, CreatedAtUtc,
          HintDependenceNextProbability, NextHintDependenceGuardrailTriggered, NextHintDependenceGuardrailLevel";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<AdaptationDecisionRow>();
        while (reader.Read())
        {
            results.Add(new AdaptationDecisionRow
            {
                SessionId                           = reader.GetString(0),
                UserId                              = reader.GetString(1),
                DecisionIndex                       = reader.GetInt32(2),
                SourceHypothesisSetId               = reader.GetString(3),
                PolicyVersion                       = reader.GetString(4),
                InterventionFamilies                = ParseDynamic(reader.GetString(5)),
                ParameterChanges                    = ParseDynamic(reader.GetString(6)),
                ReasoningSummary                    = reader.GetString(7),
                ExpiresAfterWindow                  = reader.GetInt32(8),
                CreatedAtUtc                        = reader.GetDateTime(9),
                HintDependenceNextProbability       = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                NextHintDependenceGuardrailTriggered = !reader.IsDBNull(11) && reader.GetBoolean(11),
                NextHintDependenceGuardrailLevel    = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return results;
    }

    // ── User-scoped queries (longitudinal layer) ──────────────────────────────

    public async Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsByUserAsync(string userId, int count = 10)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $@"AdaptationDecisions_v2
| where UserId == '{Sanitize(userId)}'
| order by CreatedAtUtc desc
| take {count}
| project SessionId, UserId, DecisionIndex, SourceHypothesisSetId,
          PolicyVersion, InterventionFamilies, ParameterChanges,
          ReasoningSummary, ExpiresAfterWindow, CreatedAtUtc,
          HintDependenceNextProbability, NextHintDependenceGuardrailTriggered, NextHintDependenceGuardrailLevel";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<AdaptationDecisionRow>();
        while (reader.Read())
        {
            results.Add(new AdaptationDecisionRow
            {
                SessionId                            = reader.GetString(0),
                UserId                               = reader.GetString(1),
                DecisionIndex                        = reader.GetInt32(2),
                SourceHypothesisSetId                = reader.GetString(3),
                PolicyVersion                        = reader.GetString(4),
                InterventionFamilies                 = ParseDynamic(reader.GetString(5)),
                ParameterChanges                     = ParseDynamic(reader.GetString(6)),
                ReasoningSummary                     = reader.GetString(7),
                ExpiresAfterWindow                   = reader.GetInt32(8),
                CreatedAtUtc                         = reader.GetDateTime(9),
                HintDependenceNextProbability        = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                NextHintDependenceGuardrailTriggered = !reader.IsDBNull(11) && reader.GetBoolean(11),
                NextHintDependenceGuardrailLevel     = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return results;
    }

    public async Task<long> GetTrainingRowCountByUserAsync(string userId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return 0;
        }

        var kql = $@"BehaviorStateTrainingRows
| where UserId == '{Sanitize(userId)}'
| count";

        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        if (reader.Read())
            return reader.GetInt64(0);

        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Parses a JSON string returned by Kusto for a dynamic column.
    private static JsonElement ParseDynamic(string json)
    {
        if (string.IsNullOrEmpty(json))
            return JsonDocument.Parse("null").RootElement;
        return JsonDocument.Parse(json).RootElement;
    }

    // Prevent KQL injection from session/user IDs (must be GUIDs in practice).
    private static string Sanitize(string value)
        => value.Replace("'", "");
}
