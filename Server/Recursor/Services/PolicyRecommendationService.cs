using System.Text;
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

    // Phase 9B/9C: returns a PolicyReliabilityConfigExport serialized as indented JSON.
    // Includes only "promising" and "risky" global families. "conditional" families appear in
    // metadata.reviewRequired. ConditionalFamilyReliabilityTiers includes segmented promising/risky entries.
    // Mode is always "shadow". Offline only — never called during live adaptation decisions.
    Task<string> ExportPolicyReliabilityConfigAsync();

    // Phase 9B/9C: returns a human-readable text summary showing the JSON snippet to paste
    // under "Recursor" → "PolicyReliability" in appsettings.json, plus review instructions
    // for conditional families and Phase 9C conditional tier guidance.
    // Offline only — never called during live adaptation decisions.
    Task<string> ExportPolicyReliabilityConfigTextAsync();
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

    // ── Phase 9B/9C: human-in-the-loop config export ─────────────────────────

    public async Task<string> ExportPolicyReliabilityConfigAsync()
    {
        var result = await GenerateRecommendationsAsync();
        var export = BuildConfigFromResult(result);

        var reviewRequired = (List<Dictionary<string, string>>)export.Metadata["reviewRequired"];
        int exported    = export.FamilyReliabilityTiers.Count;
        int review      = reviewRequired.Count;
        int conditional = export.ConditionalFamilyReliabilityTiers.Count;
        int omitted     = result.FamiliesEvaluated - exported - review;

        _logger.LogInformation(
            "Phase 9B/9C export-config: {Exported} families exported, {Omitted} omitted " +
            "(neutral/insufficient-data), {Review} requiring review (conditional), " +
            "{Conditional} families with conditional tier entries.",
            exported, omitted, review, conditional);

        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> ExportPolicyReliabilityConfigTextAsync()
    {
        var result = await GenerateRecommendationsAsync();
        var export = BuildConfigFromResult(result);

        var reviewRequired = (List<Dictionary<string, string>>)export.Metadata["reviewRequired"];
        int exported    = export.FamilyReliabilityTiers.Count;
        int review      = reviewRequired.Count;
        int conditional = export.ConditionalFamilyReliabilityTiers.Count;
        int omitted     = result.FamiliesEvaluated - exported - review;

        _logger.LogInformation(
            "Phase 9B/9C export-config-text: {Exported} families exported, {Omitted} omitted " +
            "(neutral/insufficient-data), {Review} requiring review (conditional), " +
            "{Conditional} families with conditional tier entries.",
            exported, omitted, review, conditional);

        var configSnippet = new
        {
            Mode                              = export.RecommendedMode,
            FamilyReliabilityTiers            = export.FamilyReliabilityTiers,
            ConditionalFamilyReliabilityTiers = export.ConditionalFamilyReliabilityTiers
        };
        var snippetJson = JsonSerializer.Serialize(configSnippet, new JsonSerializerOptions { WriteIndented = true });

        var sb = new StringBuilder();
        sb.AppendLine("Phase 9B/9C — Policy Reliability Config Export");
        sb.AppendLine("===============================================");
        sb.AppendLine($"Generated : {export.Metadata["generatedAtUtc"]}");
        sb.AppendLine($"Source    : {export.Metadata["source"]}");
        sb.AppendLine();
        sb.AppendLine($"WARNING: {export.Metadata["warning"]}");
        sb.AppendLine();
        sb.AppendLine("Paste the following under \"Recursor\" → \"PolicyReliability\" in appsettings.json:");
        sb.AppendLine();
        sb.AppendLine(snippetJson);
        sb.AppendLine();
        sb.AppendLine($"Families exported (global)       : {exported}  (risky or promising — insert after review)");
        sb.AppendLine($"Families omitted                 : {omitted}  (neutral or insufficient-data — no action required)");
        sb.AppendLine($"Families requiring review        : {review}  (conditional — see below before inserting)");
        sb.AppendLine($"Families with conditional tiers  : {conditional}  (Phase 9C — shadow-only until activated)");

        if (review > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Conditional families (require review before use) ---");
            foreach (var item in reviewRequired)
            {
                sb.AppendLine($"  Family : {item["family"]}");
                sb.AppendLine($"  Reason : {item["reason"]}");
                sb.AppendLine($"  Action : Review segmented breakdown at GET /api/recursor/policy-recommendations");
                sb.AppendLine($"           then define a state-specific condition before adding to FamilyReliabilityTiers.");
                sb.AppendLine();
            }
        }

        if (export.ConditionalFamilyReliabilityTiers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Phase 9C: Conditional reliability tiers (shadow-only) ---");
            sb.AppendLine("These entries are already included in the snippet above under ConditionalFamilyReliabilityTiers.");
            sb.AppendLine("Each entry defines state conditions under which a family has been observed as promising or risky.");
            sb.AppendLine("Condition keys: GuardrailOverallBehaviorState:<value> | PredictedConfusionRisk:<value> | GuardrailHintDependenceRiskLevel:<value>");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: Conditional rules have shadow-only effect when Mode=\"shadow\".");
            sb.AppendLine("           Matches are recorded in Phase8EReliabilityNotes and logs — no adaptation changes occur.");
            sb.AppendLine("           Do not change Mode to \"active\" until global tiers have been validated first.");
            sb.AppendLine();

            foreach (var (family, conditions) in export.ConditionalFamilyReliabilityTiers)
            {
                sb.AppendLine($"  Family: {family}");
                foreach (var (condKey, tier) in conditions)
                    sb.AppendLine($"    {condKey} → {tier}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("--- How to apply ---");
        sb.AppendLine("1. Review the JSON snippet above and the segmented data at GET /api/recursor/policy-recommendations.");
        sb.AppendLine("2. Copy the \"PolicyReliability\" block into appsettings.json (or appsettings.Production.json).");
        sb.AppendLine("3. Keep \"Mode\" as \"shadow\" first; verify Phase8EReliabilityNotes in ADX before promoting to \"active\".");
        sb.AppendLine("4. ConditionalFamilyReliabilityTiers can be pasted alongside FamilyReliabilityTiers — shadow-only.");
        sb.AppendLine("5. Redeploy the application.");
        sb.AppendLine("6. No runtime behavior changes occur until steps 2-5 are complete.");

        return sb.ToString();
    }

    // ── Builds a PolicyReliabilityConfigExport from a completed recommendation result.
    // Global: includes only "promising" and "risky" families.
    //   "conditional" families go into metadata["reviewRequired"].
    //   "neutral" and "insufficient-data" families are omitted entirely.
    // Conditional (Phase 9C): includes only segment entries with tier "promising" or "risky".
    //   "neutral" and "insufficient-data" segment tiers are omitted.
    //   Families with no qualifying segments produce no entry in ConditionalFamilyReliabilityTiers.
    private static PolicyReliabilityConfigExport BuildConfigFromResult(PolicyRecommendationResult result)
    {
        var tiers          = new Dictionary<string, string>(StringComparer.Ordinal);
        var reviewRequired = new List<Dictionary<string, string>>();

        foreach (var (family, tier) in result.GlobalRecommendations)
        {
            switch (tier)
            {
                case "promising":
                case "risky":
                    tiers[family] = tier;
                    break;

                case "conditional":
                    reviewRequired.Add(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["family"]     = family,
                        ["globalTier"] = tier,
                        ["reason"]     = "Promising in some learner-state segments, risky in others. " +
                                         "State-specific guardrail conditions required before active use."
                    });
                    break;

                // "neutral" and "insufficient-data" are omitted — no entry produced.
            }
        }

        // Phase 9C: build ConditionalFamilyReliabilityTiers from segmented recommendations.
        // Include only segment entries where tier is "promising" or "risky".
        var conditionalTiers = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var (family, segments) in result.SegmentedRecommendations)
        {
            var qualifiedSegments = segments
                .Where(kv => kv.Value == "promising" || kv.Value == "risky")
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            if (qualifiedSegments.Count > 0)
                conditionalTiers[family] = qualifiedSegments;
        }

        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["generatedAtUtc"]                 = result.GeneratedAtUtc.ToString("O"),
            ["totalEffectivenessRowsAnalyzed"] = result.TotalEffectivenessRowsAnalyzed,
            ["aggregatedRowsAnalyzed"]         = result.AggregatedRowsAnalyzed,
            ["familiesEvaluated"]              = result.FamiliesEvaluated,
            ["source"]                         = "Phase9A",
            ["warning"]                        = "Human review required before applying to appsettings.",
            ["reviewRequired"]                 = reviewRequired
        };

        return new PolicyReliabilityConfigExport
        {
            RecommendedMode                   = "shadow",
            FamilyReliabilityTiers            = tiers,
            ConditionalFamilyReliabilityTiers = conditionalTiers,
            Metadata                          = metadata
        };
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
