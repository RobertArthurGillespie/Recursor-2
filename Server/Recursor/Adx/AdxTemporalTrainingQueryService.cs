using System.Text.Json;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxTemporalTrainingQueryService
{
    // Returns joined (TemporalEmbeddings ⋈ TemporalPredictionTargets) rows for ML training.
    // Returns empty list when ADX is not configured.
    Task<List<TemporalRiskTrainingRow>> QueryTrainingRowsAsync();
}

public class AdxTemporalTrainingQueryService : IAdxTemporalTrainingQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxTemporalTrainingQueryService> _logger;

    // Ordinal index of each projected column — must stay in sync with the KQL below.
    private const int OrdSessionId              = 0;
    private const int OrdUserId                 = 1;
    private const int OrdSourceWindowIndex      = 2;
    private const int OrdTargetWindowIndex      = 3;
    private const int OrdHorizon                = 4;
    private const int OrdValues                 = 5;
    // ordinal 6 = FeatureNames (not used for ML; included for schema traceability)
    private const int OrdTargetNearTermRisk     = 7;
    private const int OrdTargetBehaviorState    = 8;
    private const int OrdTargetConfusionScore   = 9;
    private const int OrdTargetGoalUnderstanding = 10;
    private const int OrdTargetHintDependenceScore = 11;

    private const string Kql = """
        TemporalEmbeddings
        | join kind=inner (
            TemporalPredictionTargets
        ) on SessionId, UserId
        | where WindowIndex == SourceWindowIndex
        | where EmbeddingVersion == "v1.0"
        | project
            SessionId,
            UserId,
            SourceWindowIndex = WindowIndex,
            TargetWindowIndex,
            Horizon,
            Values,
            FeatureNames,
            TargetNearTermRisk,
            TargetBehaviorState,
            TargetConfusionScore,
            TargetGoalUnderstanding,
            TargetHintDependenceScore
        """;

    public AdxTemporalTrainingQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxTemporalTrainingQueryService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDb";
        _logger = logger;
    }

    public async Task<List<TemporalRiskTrainingRow>> QueryTrainingRowsAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning(
                "ADX query provider not configured — temporal training query skipped.");
            return [];
        }

        _logger.LogInformation(
            "Querying temporal training rows from ADX (database: {Database}).", _database);

        var results = new List<TemporalRiskTrainingRow>();
        int skippedBadLength = 0;
        int skippedEmptyLabel = 0;

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(
                _database, Kql, new ClientRequestProperties());

            while (reader.Read())
            {
                // Values is a dynamic ADX column stored as a JSON double array.
                var valuesRaw = reader.GetValue(OrdValues)?.ToString() ?? "[]";
                double[]? doubles;
                try
                {
                    doubles = JsonSerializer.Deserialize<double[]>(valuesRaw);
                }
                catch
                {
                    doubles = null;
                }

                if (doubles is null || doubles.Length != 25)
                {
                    skippedBadLength++;
                    continue;
                }

                var label = reader.GetString(OrdTargetNearTermRisk);
                if (string.IsNullOrEmpty(label))
                {
                    skippedEmptyLabel++;
                    continue;
                }

                results.Add(new TemporalRiskTrainingRow
                {
                    SessionId           = reader.GetString(OrdSessionId),
                    UserId              = reader.GetString(OrdUserId),
                    SourceWindowIndex   = reader.GetInt32(OrdSourceWindowIndex),
                    TargetWindowIndex   = reader.GetInt32(OrdTargetWindowIndex),
                    Horizon             = reader.GetInt32(OrdHorizon),
                    EmbeddingValues     = doubles.Select(d => (float)d).ToArray(),
                    TargetNearTermRisk  = label,
                    TargetBehaviorState = reader.GetString(OrdTargetBehaviorState),
                    TargetConfusionScore      = reader.GetDouble(OrdTargetConfusionScore),
                    TargetGoalUnderstanding   = reader.GetDouble(OrdTargetGoalUnderstanding),
                    TargetHintDependenceScore = reader.GetDouble(OrdTargetHintDependenceScore),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Temporal training row query failed.");
            throw;
        }

        _logger.LogInformation(
            "Temporal training query complete: {Count} rows returned, {BadLen} skipped (bad vector length), " +
            "{EmptyLabel} skipped (empty label).",
            results.Count, skippedBadLength, skippedEmptyLabel);

        return results;
    }
}
