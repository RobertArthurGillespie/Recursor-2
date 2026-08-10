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

    // Shadow policy advisor — context-aligned lookup
    /// <summary>
    /// Returns the best-matching adaptation decision for a set of user-relative signals.
    /// Prefers the most recent decision in the same session at or before
    /// <paramref name="signalsTimestampUtc"/> (strategy A). If none exists at that
    /// timestamp, returns the latest decision in the same session (strategy B, logged).
    /// Returns <c>null</c> when the session has no decisions at all or ADX is not configured.
    /// The caller is responsible for the final user-latest fallback (strategy C).
    /// </summary>
    Task<AdaptationDecisionRow?> GetBestMatchingDecisionForSignalsAsync(
        string userId,
        string sessionId,
        DateTime signalsTimestampUtc);
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
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
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
                InterventionFamilies                = ReadDynamicSafe(reader, 5, "InterventionFamilies"),
                ParameterChanges                    = ReadDynamicSafe(reader, 6, "ParameterChanges"),
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
                InterventionFamilies                 = ReadDynamicSafe(reader, 5, "InterventionFamilies"),
                ParameterChanges                     = ReadDynamicSafe(reader, 6, "ParameterChanges"),
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

    public async Task<AdaptationDecisionRow?> GetBestMatchingDecisionForSignalsAsync(
        string userId,
        string sessionId,
        DateTime signalsTimestampUtc)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return null;
        }

        var sanitizedUser    = Sanitize(userId);
        var sanitizedSession = Sanitize(sessionId);

        // KQL datetime literal — Kusto treats unlabeled datetimes as UTC.
        var tsLiteral = signalsTimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fffffff");

        // ── Strategy A ────────────────────────────────────────────────────────────
        // Most recent decision in the same session at or before the signal timestamp.
        // This is the ideal match: the decision that was in effect when the signal
        // window was recorded.
        var kqlA = $@"AdaptationDecisions_v2
| where UserId == '{sanitizedUser}'
| where SessionId == '{sanitizedSession}'
| where CreatedAtUtc <= datetime({tsLiteral})
| top 1 by CreatedAtUtc desc
| project SessionId, UserId, DecisionIndex, SourceHypothesisSetId,
          PolicyVersion, InterventionFamilies, ParameterChanges,
          ReasoningSummary, ExpiresAfterWindow, CreatedAtUtc,
          HintDependenceNextProbability, NextHintDependenceGuardrailTriggered, NextHintDependenceGuardrailLevel";

        try
        {
            using var readerA = await _queryProvider.ExecuteQueryAsync(_database, kqlA, new ClientRequestProperties());

            if (readerA.Read())
            {
                return new AdaptationDecisionRow
                {
                    SessionId                            = readerA.GetString(0),
                    UserId                               = readerA.GetString(1),
                    DecisionIndex                        = readerA.GetInt32(2),
                    SourceHypothesisSetId                = readerA.GetString(3),
                    PolicyVersion                        = readerA.GetString(4),
                    InterventionFamilies                 = ReadDynamicSafe(readerA, 5, "InterventionFamilies"),
                    ParameterChanges                     = ReadDynamicSafe(readerA, 6, "ParameterChanges"),
                    ReasoningSummary                     = readerA.GetString(7),
                    ExpiresAfterWindow                   = readerA.GetInt32(8),
                    CreatedAtUtc                         = readerA.GetDateTime(9),
                    HintDependenceNextProbability        = readerA.IsDBNull(10) ? null : readerA.GetDouble(10),
                    NextHintDependenceGuardrailTriggered = !readerA.IsDBNull(11) && readerA.GetBoolean(11),
                    NextHintDependenceGuardrailLevel     = readerA.IsDBNull(12) ? null : readerA.GetString(12)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Shadow policy advisor: strategy A decision query failed for " +
                "userId '{UserId}', sessionId '{SessionId}'.",
                userId, sessionId);
            return null;
        }

        // ── Strategy B ────────────────────────────────────────────────────────────
        // Strategy A found nothing — no decision existed at or before the signal
        // timestamp. Fall back to the latest decision in the same session regardless
        // of timestamp (e.g. decision was written just after the training row).
        _logger.LogDebug(
            "Shadow policy advisor: no prior decision found at or before signal " +
            "timestamp {SignalTs:O} in sessionId '{SessionId}'; " +
            "falling back to latest decision in session (strategy B).",
            signalsTimestampUtc, sessionId);

        var kqlB = $@"AdaptationDecisions_v2
| where UserId == '{sanitizedUser}'
| where SessionId == '{sanitizedSession}'
| top 1 by CreatedAtUtc desc
| project SessionId, UserId, DecisionIndex, SourceHypothesisSetId,
          PolicyVersion, InterventionFamilies, ParameterChanges,
          ReasoningSummary, ExpiresAfterWindow, CreatedAtUtc,
          HintDependenceNextProbability, NextHintDependenceGuardrailTriggered, NextHintDependenceGuardrailLevel";

        try
        {
            using var readerB = await _queryProvider.ExecuteQueryAsync(_database, kqlB, new ClientRequestProperties());

            if (!readerB.Read())
                return null;

            return new AdaptationDecisionRow
            {
                SessionId                            = readerB.GetString(0),
                UserId                               = readerB.GetString(1),
                DecisionIndex                        = readerB.GetInt32(2),
                SourceHypothesisSetId                = readerB.GetString(3),
                PolicyVersion                        = readerB.GetString(4),
                InterventionFamilies                 = ReadDynamicSafe(readerB, 5, "InterventionFamilies"),
                ParameterChanges                     = ReadDynamicSafe(readerB, 6, "ParameterChanges"),
                ReasoningSummary                     = readerB.GetString(7),
                ExpiresAfterWindow                   = readerB.GetInt32(8),
                CreatedAtUtc                         = readerB.GetDateTime(9),
                HintDependenceNextProbability        = readerB.IsDBNull(10) ? null : readerB.GetDouble(10),
                NextHintDependenceGuardrailTriggered = !readerB.IsDBNull(11) && readerB.GetBoolean(11),
                NextHintDependenceGuardrailLevel     = readerB.IsDBNull(12) ? null : readerB.GetString(12)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Shadow policy advisor: strategy B decision query failed for " +
                "userId '{UserId}', sessionId '{SessionId}'.",
                userId, sessionId);
            return null;
        }
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

    // Reads an ADX dynamic column safely.
    // ADX dynamic columns can surface as DBNull, a provider object (JToken/string),
    // or a regular string depending on Kusto SDK version and row state.
    // Returns a null JsonElement rather than throwing on any routine case.
    private JsonElement ReadDynamicSafe(System.Data.IDataReader reader, int index, string columnHint = "")
    {
        try
        {
            if (reader.IsDBNull(index))
                return JsonDocument.Parse("null").RootElement;

            var raw = reader.GetValue(index);

            string? json = raw switch
            {
                string s         => s,
                null             => null,
                _                => raw.ToString()
            };

            if (string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("null").RootElement;

            return JsonDocument.Parse(json).RootElement;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read ADX dynamic column at index {Index} (hint: '{ColumnHint}'). " +
                "Returning null element.",
                index, columnHint);
            return JsonDocument.Parse("null").RootElement;
        }
    }

    // Parses a JSON string returned by Kusto for a dynamic column (non-reader path).
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
