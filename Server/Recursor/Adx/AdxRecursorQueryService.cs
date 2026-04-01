using System.Text.Json;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxRecursorQueryService
{
    Task<IEnumerable<FeatureWindowRow>> GetLatestFeatureWindowsAsync(string sessionId, int count = 5);
    Task<IEnumerable<BehaviorProfileRow>> GetLatestBehaviorProfilesAsync(string sessionId, int count = 5);
    Task<IEnumerable<HypothesisSetRow>> GetLatestHypothesisSetsAsync(string sessionId, int count = 5);
    Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsAsync(string sessionId, int count = 5);
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

    public async Task<IEnumerable<FeatureWindowRow>> GetLatestFeatureWindowsAsync(string sessionId, int count = 5)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX query provider not configured.");
            return [];
        }

        var kql = $"FeatureWindows | where SessionId == '{Sanitize(sessionId)}' | order by WindowIndex desc | take {count}";
        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<FeatureWindowRow>();
        while (reader.Read())
        {
            results.Add(new FeatureWindowRow
            {
                SessionId = reader.GetString(0),
                WindowIndex = reader.GetInt32(1),
                WindowType = reader.GetString(2),
                WindowStartSequence = reader.GetInt64(3),
                WindowEndSequence = reader.GetInt64(4),
                WindowStartUtc = reader.GetDateTime(5),
                WindowEndUtc = reader.GetDateTime(6),
                SimId = reader.GetString(7),
                ScenarioId = reader.GetString(8),
                FeatureExtractorVersion = reader.GetString(9),
                Features = ParseDynamic(reader.GetString(10))
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

        var kql = $"BehaviorProfiles | where SessionId == '{Sanitize(sessionId)}' | order by WindowIndex desc | take {count}";
        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<BehaviorProfileRow>();
        while (reader.Read())
        {
            results.Add(new BehaviorProfileRow
            {
                SessionId = reader.GetString(0),
                WindowIndex = reader.GetInt32(1),
                SourceFeatureWindowId = reader.GetString(2),
                InterpreterVersion = reader.GetString(3),
                DimensionScores = ParseDynamic(reader.GetString(4)),
                CreatedAtUtc = reader.GetDateTime(5)
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

        var kql = $"HypothesisSets | where SessionId == '{Sanitize(sessionId)}' | order by WindowIndex desc | take {count}";
        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<HypothesisSetRow>();
        while (reader.Read())
        {
            results.Add(new HypothesisSetRow
            {
                SessionId = reader.GetString(0),
                WindowIndex = reader.GetInt32(1),
                SourceBehaviorProfileId = reader.GetString(2),
                InterpreterMode = reader.GetString(3),
                InterpreterVersion = reader.GetString(4),
                Hypotheses = ParseDynamic(reader.GetString(5)),
                CreatedAtUtc = reader.GetDateTime(6)
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

        var kql = $"AdaptationDecisions | where SessionId == '{Sanitize(sessionId)}' | order by DecisionIndex desc | take {count}";
        using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());

        var results = new List<AdaptationDecisionRow>();
        while (reader.Read())
        {
            results.Add(new AdaptationDecisionRow
            {
                SessionId = reader.GetString(0),
                DecisionIndex = reader.GetInt32(1),
                SourceHypothesisSetId = reader.GetString(2),
                PolicyVersion = reader.GetString(3),
                InterventionFamilies = ParseDynamic(reader.GetString(4)),
                ParameterChanges = ParseDynamic(reader.GetString(5)),
                ReasoningSummary = reader.GetString(6),
                ExpiresAfterWindow = reader.GetInt32(7),
                CreatedAtUtc = reader.GetDateTime(8)
            });
        }

        return results;
    }

    // Parses a JSON string returned by Kusto for a dynamic column.
    private static JsonElement ParseDynamic(string json)
    {
        if (string.IsNullOrEmpty(json))
            return JsonDocument.Parse("null").RootElement;
        return JsonDocument.Parse(json).RootElement;
    }

    // Prevent KQL injection from session IDs (must be GUIDs in practice).
    private static string Sanitize(string value)
        => value.Replace("'", "");
}
