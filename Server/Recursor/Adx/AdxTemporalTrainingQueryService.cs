using System.Text.Json;
using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

public interface IAdxTemporalTrainingQueryService
{
    // Returns joined (TemporalEmbeddings ⋈ TemporalPredictionTargets) rows for ML training.
    // Returns empty list when ADX is not configured.
    //
    // Stage 5 (corrective pass): this service returns structurally valid rows only — it does
    // NOT apply any trainer-specific target-label rule. Earlier it dropped every row whose
    // TargetNearTermRisk was blank, which silently starved the Phase 10E behavior-state trainer
    // of rows that had a perfectly valid TargetBehaviorState. Each trainer is now responsible
    // for validating and excluding rows against its OWN target column
    // (TemporalRiskModelTrainingService / TemporalElevatedRiskModelTrainingService against
    // TargetNearTermRisk, TemporalBehaviorStateModelTrainingService against
    // TargetBehaviorState via BehaviorStateLabelPolicy) and for reporting its own exclusion
    // counts by reason.
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
    private const int OrdSimId                  = 2;
    private const int OrdScenarioId              = 3;
    private const int OrdSourceWindowIndex      = 4;
    private const int OrdTargetWindowIndex      = 5;
    private const int OrdHorizon                = 6;
    private const int OrdValues                 = 7;
    // ordinal 8 = FeatureNames (not used for ML; included for schema traceability)
    private const int OrdTargetNearTermRisk     = 9;
    private const int OrdTargetBehaviorState    = 10;
    private const int OrdTargetConfusionScore   = 11;
    private const int OrdTargetGoalUnderstanding = 12;
    private const int OrdTargetHintDependenceScore = 13;

    // Stage 7: defensive deduplication. ADX is append-only — a replayed batch or an ingest
    // retried after a transient failure can produce more than one physical row for the same
    // logical (SessionId, UserId, WindowIndex) embedding or (SessionId, UserId,
    // SourceWindowIndex, Horizon) target. Both sides are deduplicated to their newest row
    // (arg_max CreatedAtUtc) before joining, so a duplicate physical row never produces a
    // duplicate training row or inflates any trainer's row count.
    // Public so tests can assert on the KQL text (e.g. dedup coverage) without needing ADX,
    // matching the convention used by the dashboard query builders in AdxDashboardQueryService.
    public const string Kql = """
        let embeddings = TemporalEmbeddings
        | where EmbeddingVersion == "v1.0"
        | summarize arg_max(CreatedAtUtc, *) by SessionId, UserId, WindowIndex;
        let targets = TemporalPredictionTargets
        | summarize arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon;
        embeddings
        | join kind=inner targets on SessionId, UserId
        | where WindowIndex == SourceWindowIndex
        | project
            SessionId,
            UserId,
            SimId,
            ScenarioId,
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
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
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

                // Embedding-vector length is a structural validity check (a malformed row is
                // useless to every trainer), so it stays here. Label validity is deliberately
                // NOT checked here — see the class-level remarks above. Each trainer applies
                // its own target-label rules (TargetNearTermRisk for the risk/elevated-risk
                // trainers, BehaviorStateLabelPolicy for the Phase 10E trainer) and reports its
                // own exclusion counts by reason.
                if (doubles is null || doubles.Length != 25)
                {
                    skippedBadLength++;
                    continue;
                }

                results.Add(new TemporalRiskTrainingRow
                {
                    SessionId           = reader.GetString(OrdSessionId),
                    UserId              = reader.GetString(OrdUserId),
                    SimId               = reader.GetString(OrdSimId),
                    ScenarioId          = reader.GetString(OrdScenarioId),
                    SourceWindowIndex   = reader.GetInt32(OrdSourceWindowIndex),
                    TargetWindowIndex   = reader.GetInt32(OrdTargetWindowIndex),
                    Horizon             = reader.GetInt32(OrdHorizon),
                    EmbeddingValues     = doubles.Select(d => (float)d).ToArray(),
                    TargetNearTermRisk  = reader.GetString(OrdTargetNearTermRisk),
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
            "Temporal training query complete: {Count} rows returned, {BadLen} skipped (bad vector length). " +
            "Target-label validity is each trainer's own responsibility.",
            results.Count, skippedBadLength);

        return results;
    }
}
