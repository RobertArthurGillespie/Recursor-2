using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// One aggregated row returned by each Phase 9A effectiveness query.
// SegmentDimension and SegmentValue are empty for the global (non-segmented) query.
public class FamilyEffectivenessRow
{
    public string InterventionFamily { get; set; } = "";
    public long SampleCount { get; set; }
    public long ImprovedCount { get; set; }
    public long NeutralCount { get; set; }
    public long WorsenedCount { get; set; }
    public double AvgConfusionDelta { get; set; }
    public string SegmentDimension { get; set; } = "";
    public string SegmentValue { get; set; } = "";
}

public interface IAdxPolicyRecommendationQueryService
{
    // Aggregate per family across all AdaptationEffectiveness rows — no learner-state join.
    Task<IReadOnlyList<FamilyEffectivenessRow>> GetGlobalEffectivenessAsync();

    // Per-family aggregates segmented by GuardrailOverallBehaviorState at decision time.
    Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByBehaviorStateAsync();

    // Per-family aggregates segmented by PredictedConfusionRisk at decision time.
    Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByConfusionRiskAsync();

    // Per-family aggregates segmented by GuardrailHintDependenceRiskLevel at decision time.
    Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByHintDependenceRiskAsync();

    // Total row count of the AdaptationEffectiveness table — the true denominator for coverage reporting.
    Task<long> GetTotalEffectivenessRowsAsync();
}

public class AdxPolicyRecommendationQueryService : IAdxPolicyRecommendationQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxPolicyRecommendationQueryService> _logger;

    public AdxPolicyRecommendationQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxPolicyRecommendationQueryService> logger)
    {
        _queryProvider = services.GetService<ICslQueryProvider>();
        _database = configuration["Adx:Database"] ?? "RecursorDbMain";
        _logger = logger;
    }

    public async Task<IReadOnlyList<FamilyEffectivenessRow>> GetGlobalEffectivenessAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("Phase 9A: ADX query provider not configured. Returning empty global effectiveness data.");
            return [];
        }

        // KQL: expand one row per family per effectiveness evaluation, then summarize.
        // Single-quoted string literals avoid C# escaping requirements.
        const string kql =
            "AdaptationEffectiveness\n" +
            "| mv-expand family = InterventionFamilies\n" +
            "| summarize\n" +
            "    SampleCount   = count(),\n" +
            "    ImprovedCount = countif(Outcome == 'improved'),\n" +
            "    NeutralCount  = countif(Outcome == 'neutral'),\n" +
            "    WorsenedCount = countif(Outcome == 'worsened'),\n" +
            "    AvgConfusionDelta = round(avg(ConfusionDelta), 4)\n" +
            "  by InterventionFamily = tostring(family)\n" +
            "| project InterventionFamily, SampleCount, ImprovedCount, NeutralCount, WorsenedCount, AvgConfusionDelta";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<FamilyEffectivenessRow>();
            while (reader.Read())
            {
                results.Add(new FamilyEffectivenessRow
                {
                    InterventionFamily = reader.GetString(0),
                    SampleCount        = reader.GetInt64(1),
                    ImprovedCount      = reader.GetInt64(2),
                    NeutralCount       = reader.GetInt64(3),
                    WorsenedCount      = reader.GetInt64(4),
                    AvgConfusionDelta  = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                    SegmentDimension   = "",
                    SegmentValue       = ""
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phase 9A: global effectiveness query failed.");
            return [];
        }
    }

    public Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByBehaviorStateAsync()
    {
        const string kql =
            "let PreState =\n" +
            "    BehaviorStateTrainingRows\n" +
            "    | project\n" +
            "        SessionId,\n" +
            "        UserId,\n" +
            "        AppliedAtWindowIndex = WindowIndex,\n" +
            "        GuardrailOverallBehaviorState;\n" +
            "AdaptationEffectiveness\n" +
            "| mv-expand family = InterventionFamilies\n" +
            "| join kind=leftouter PreState on SessionId, UserId, AppliedAtWindowIndex\n" +
            "| where isnotempty(GuardrailOverallBehaviorState)\n" +
            "| summarize\n" +
            "    SampleCount   = count(),\n" +
            "    ImprovedCount = countif(Outcome == 'improved'),\n" +
            "    NeutralCount  = countif(Outcome == 'neutral'),\n" +
            "    WorsenedCount = countif(Outcome == 'worsened'),\n" +
            "    AvgConfusionDelta = round(avg(ConfusionDelta), 4)\n" +
            "  by InterventionFamily = tostring(family), GuardrailOverallBehaviorState\n" +
            "| project InterventionFamily, SampleCount, ImprovedCount, NeutralCount, WorsenedCount, AvgConfusionDelta, GuardrailOverallBehaviorState";

        return RunSegmentedQueryAsync("GuardrailOverallBehaviorState", kql);
    }

    public Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByConfusionRiskAsync()
    {
        const string kql =
            "let PreState =\n" +
            "    BehaviorStateTrainingRows\n" +
            "    | project\n" +
            "        SessionId,\n" +
            "        UserId,\n" +
            "        AppliedAtWindowIndex = WindowIndex,\n" +
            "        PredictedConfusionRisk;\n" +
            "AdaptationEffectiveness\n" +
            "| mv-expand family = InterventionFamilies\n" +
            "| join kind=leftouter PreState on SessionId, UserId, AppliedAtWindowIndex\n" +
            "| where isnotempty(PredictedConfusionRisk)\n" +
            "| summarize\n" +
            "    SampleCount   = count(),\n" +
            "    ImprovedCount = countif(Outcome == 'improved'),\n" +
            "    NeutralCount  = countif(Outcome == 'neutral'),\n" +
            "    WorsenedCount = countif(Outcome == 'worsened'),\n" +
            "    AvgConfusionDelta = round(avg(ConfusionDelta), 4)\n" +
            "  by InterventionFamily = tostring(family), PredictedConfusionRisk\n" +
            "| project InterventionFamily, SampleCount, ImprovedCount, NeutralCount, WorsenedCount, AvgConfusionDelta, PredictedConfusionRisk";

        return RunSegmentedQueryAsync("PredictedConfusionRisk", kql);
    }

    public Task<IReadOnlyList<FamilyEffectivenessRow>> GetEffectivenessByHintDependenceRiskAsync()
    {
        const string kql =
            "let PreState =\n" +
            "    BehaviorStateTrainingRows\n" +
            "    | project\n" +
            "        SessionId,\n" +
            "        UserId,\n" +
            "        AppliedAtWindowIndex = WindowIndex,\n" +
            "        GuardrailHintDependenceRiskLevel;\n" +
            "AdaptationEffectiveness\n" +
            "| mv-expand family = InterventionFamilies\n" +
            "| join kind=leftouter PreState on SessionId, UserId, AppliedAtWindowIndex\n" +
            "| where isnotempty(GuardrailHintDependenceRiskLevel)\n" +
            "| summarize\n" +
            "    SampleCount   = count(),\n" +
            "    ImprovedCount = countif(Outcome == 'improved'),\n" +
            "    NeutralCount  = countif(Outcome == 'neutral'),\n" +
            "    WorsenedCount = countif(Outcome == 'worsened'),\n" +
            "    AvgConfusionDelta = round(avg(ConfusionDelta), 4)\n" +
            "  by InterventionFamily = tostring(family), GuardrailHintDependenceRiskLevel\n" +
            "| project InterventionFamily, SampleCount, ImprovedCount, NeutralCount, WorsenedCount, AvgConfusionDelta, GuardrailHintDependenceRiskLevel";

        return RunSegmentedQueryAsync("GuardrailHintDependenceRiskLevel", kql);
    }

    public async Task<long> GetTotalEffectivenessRowsAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("Phase 9A: ADX query provider not configured. Total effectiveness row count unavailable.");
            return 0;
        }

        const string kql = "AdaptationEffectiveness | count";

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            if (reader.Read())
                return reader.GetInt64(0);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phase 9A: total effectiveness row count query failed.");
            return 0;
        }
    }

    // ── Shared helper ─────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<FamilyEffectivenessRow>> RunSegmentedQueryAsync(
        string dimensionName,
        string kql)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning(
                "Phase 9A: ADX query provider not configured. Skipping segmented query for {Dimension}.",
                dimensionName);
            return [];
        }

        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<FamilyEffectivenessRow>();
            while (reader.Read())
            {
                results.Add(new FamilyEffectivenessRow
                {
                    InterventionFamily = reader.GetString(0),
                    SampleCount        = reader.GetInt64(1),
                    ImprovedCount      = reader.GetInt64(2),
                    NeutralCount       = reader.GetInt64(3),
                    WorsenedCount      = reader.GetInt64(4),
                    AvgConfusionDelta  = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                    SegmentDimension   = dimensionName,
                    SegmentValue       = reader.GetString(6)
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Phase 9A: segmented effectiveness query for dimension '{Dimension}' failed.",
                dimensionName);
            return [];
        }
    }
}
