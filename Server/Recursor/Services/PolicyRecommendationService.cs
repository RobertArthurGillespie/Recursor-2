using System.Text.Json;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IPolicyRecommendationService
{
    // Queries ADX and returns reliability tier recommendations per intervention family.
    // This is an offline / analytics-only operation — never called during live adaptation decisions.
    Task<PolicyRecommendationResult> GenerateRecommendationsAsync();

    // Returns the recommendation result serialized as indented JSON.
    // Intended for piping to a file for manual config use.
    Task<string> ExportRecommendationsAsJsonAsync();
}

public class PolicyRecommendationService : IPolicyRecommendationService
{
    private readonly IAdxPolicyRecommendationQueryService _queryService;
    private readonly ILogger<PolicyRecommendationService> _logger;

    public PolicyRecommendationService(
        IAdxPolicyRecommendationQueryService queryService,
        ILogger<PolicyRecommendationService> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<PolicyRecommendationResult> GenerateRecommendationsAsync()
    {
        // Fire all five ADX queries in parallel — each is a read-only operation.
        var globalTask        = _queryService.GetGlobalEffectivenessAsync();
        var byStateTask       = _queryService.GetEffectivenessByBehaviorStateAsync();
        var byConfTask        = _queryService.GetEffectivenessByConfusionRiskAsync();
        var byHintTask        = _queryService.GetEffectivenessByHintDependenceRiskAsync();
        var totalRowCountTask = _queryService.GetTotalEffectivenessRowsAsync();

        await Task.WhenAll(globalTask, byStateTask, byConfTask, byHintTask, totalRowCountTask);

        var global              = globalTask.Result;
        var byState             = byStateTask.Result;
        var byConf              = byConfTask.Result;
        var byHint              = byHintTask.Result;
        var effectivenessCount  = totalRowCountTask.Result;

        var aggregatedRows = global.Count + byState.Count + byConf.Count + byHint.Count;
        _logger.LogInformation(
            "Phase 9A: {EffectivenessRows} AdaptationEffectiveness rows in ADX; " +
            "returned {AggregatedRows} aggregated rows ({Global} global, " +
            "{ByState} by behavior state, {ByConf} by confusion risk, {ByHint} by hint dependence risk).",
            effectivenessCount, aggregatedRows, global.Count, byState.Count, byConf.Count, byHint.Count);

        // ── Global tier classification ────────────────────────────────────────
        var globalRecommendations = new Dictionary<string, string>(StringComparer.Ordinal);
        var globalMetrics         = new Dictionary<string, FamilyReliabilityMetrics>(StringComparer.Ordinal);

        foreach (var row in global)
        {
            var tier = ClassifyTier(row.SampleCount, row.ImprovedCount, row.WorsenedCount);
            globalRecommendations[row.InterventionFamily] = tier;
            globalMetrics[row.InterventionFamily]         = BuildMetrics(row);

            if (tier == "insufficient-data")
            {
                _logger.LogInformation(
                    "Phase 9A: '{Family}' has insufficient data (SampleCount={Count}). " +
                    "Recommendation: gather-more-data.",
                    row.InterventionFamily, row.SampleCount);
            }
        }

        _logger.LogInformation(
            "Phase 9A: evaluated {Count} intervention families.",
            globalRecommendations.Count);

        // ── Segmented tier classification ─────────────────────────────────────
        // All three segmented result sets are flattened into a single pass.
        // Key format: "{DimensionName}:{SegmentValue}"
        var segmentedRecommendations = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        var allSegmented = byState.Concat(byConf).Concat(byHint);
        foreach (var row in allSegmented)
        {
            var segKey = $"{row.SegmentDimension}:{row.SegmentValue}";
            var tier   = ClassifyTier(row.SampleCount, row.ImprovedCount, row.WorsenedCount);

            if (!segmentedRecommendations.TryGetValue(row.InterventionFamily, out var familySegments))
            {
                familySegments = new Dictionary<string, string>(StringComparer.Ordinal);
                segmentedRecommendations[row.InterventionFamily] = familySegments;
            }

            familySegments[segKey] = tier;
        }

        // ── Conditional override ──────────────────────────────────────────────
        // A family that is "promising" in at least one qualified segment AND "risky"
        // in at least one other qualified segment gets overridden to "conditional".
        // This mirrors the Phase 8D "state-conditional" consistency label.
        foreach (var (family, segments) in segmentedRecommendations)
        {
            var qualifiedTiers = segments.Values
                .Where(t => t != "insufficient-data")
                .ToList();

            bool hasPromising = qualifiedTiers.Contains("promising");
            bool hasRisky     = qualifiedTiers.Contains("risky");

            if (hasPromising && hasRisky)
            {
                globalRecommendations[family] = "conditional";
                _logger.LogInformation(
                    "Phase 9A: '{Family}' reclassified as 'conditional' — " +
                    "promising in some learner-state segments, risky in others. " +
                    "Do not apply unconditionally; use segmented data to define state conditions.",
                    family);
            }
        }

        return new PolicyRecommendationResult
        {
            GlobalRecommendations          = globalRecommendations,
            SegmentedRecommendations       = segmentedRecommendations,
            GlobalMetrics                  = globalMetrics,
            TotalEffectivenessRowsAnalyzed = effectivenessCount,
            AggregatedRowsAnalyzed         = aggregatedRows,
            FamiliesEvaluated              = globalRecommendations.Count,
            GeneratedAtUtc                 = DateTimeOffset.UtcNow
        };
    }

    public async Task<string> ExportRecommendationsAsJsonAsync()
    {
        var result = await GenerateRecommendationsAsync();
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    // ── Tier classification — mirrors Phase 8D PolicyFamilyReliability() ─────
    //
    // Priority order (matches KQL):
    //   1. insufficient-data  (sample count < 10 — no reliable tier)
    //   2. risky              (worsened >= 35% — harm rate vetoes gain rate)
    //   3. promising          (improved >= 60% AND worsened <= 20%)
    //   4. neutral            (catch-all)
    private static string ClassifyTier(long sampleCount, long improvedCount, long worsenedCount)
    {
        if (sampleCount < 10)
            return "insufficient-data";

        var improvedRate = (double)improvedCount / sampleCount;
        var worsenedRate = (double)worsenedCount / sampleCount;

        if (worsenedRate >= 0.35)
            return "risky";

        if (improvedRate >= 0.60 && worsenedRate <= 0.20)
            return "promising";

        return "neutral";
    }

    private static FamilyReliabilityMetrics BuildMetrics(FamilyEffectivenessRow row)
    {
        var sc = row.SampleCount > 0 ? (double)row.SampleCount : 1.0;
        return new FamilyReliabilityMetrics
        {
            SampleCount       = (int)row.SampleCount,
            ImprovedRate      = Math.Round(row.ImprovedCount / sc, 3),
            WorsenedRate      = Math.Round(row.WorsenedCount / sc, 3),
            NeutralRate       = Math.Round(row.NeutralCount  / sc, 3),
            AvgConfusionDelta = row.AvgConfusionDelta
        };
    }
}
