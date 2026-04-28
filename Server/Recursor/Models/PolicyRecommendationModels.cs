namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class PolicyRecommendationResult
{
    // family → overall tier ("promising" | "neutral" | "risky" | "insufficient-data" | "conditional")
    // "conditional" is assigned when a family is promising in some learner-state segments
    // but risky in others; it overrides the global tier computed from aggregate data.
    public Dictionary<string, string> GlobalRecommendations { get; init; } = new();

    // family → "{DimensionName}:{SegmentValue}" → tier
    // e.g. SegmentedRecommendations["difficulty-increase"]["GuardrailOverallBehaviorState:stable"] = "promising"
    public Dictionary<string, Dictionary<string, string>> SegmentedRecommendations { get; init; } = new();

    // Per-family aggregate metrics — used for export and manual review.
    // Present only for families that appeared in global ADX results.
    public Dictionary<string, FamilyReliabilityMetrics> GlobalMetrics { get; init; } = new();

    // True count of rows in the AdaptationEffectiveness ADX table at query time.
    // This is what "rows analyzed" means — the underlying evaluation records, not the aggregated output.
    public long TotalEffectivenessRowsAnalyzed { get; init; }

    // Count of aggregated result rows returned by the four ADX queries (one per family / per segment).
    // Useful for gauging query output size, not data coverage.
    public int AggregatedRowsAnalyzed { get; init; }

    public int FamiliesEvaluated { get; init; }
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

public class FamilyReliabilityMetrics
{
    public int SampleCount { get; init; }
    public double ImprovedRate { get; init; }
    public double WorsenedRate { get; init; }
    public double NeutralRate { get; init; }
    public double AvgConfusionDelta { get; init; }
}

/// <summary>
/// Phase 9B/9C export artifact. Shaped for manual insertion into
/// Recursor:PolicyReliability in appsettings.json after human review.
///
/// RecommendedMode is always "shadow" — the reviewer must explicitly change
/// it to "active" in appsettings before live behavior is affected.
///
/// FamilyReliabilityTiers includes only "promising" and "risky" global families.
/// ConditionalFamilyReliabilityTiers includes only "promising" and "risky" segment entries.
/// "conditional" global families appear in Metadata["reviewRequired"].
/// "neutral" and "insufficient-data" entries are omitted entirely.
/// </summary>
public class PolicyReliabilityConfigExport
{
    // Always "shadow". Do not change to "active" without reviewing the full
    // segmented breakdown at GET /api/recursor/policy-recommendations.
    public string RecommendedMode { get; init; } = "shadow";

    // Global families safe to insert into appsettings Recursor:PolicyReliability:FamilyReliabilityTiers.
    // Contains only "promising" and "risky" entries.
    public Dictionary<string, string> FamilyReliabilityTiers { get; init; } = new();

    // Phase 9C: State-conditional tiers derived from Phase 9A segmented recommendations.
    // Maps family → condition-key → tier.
    // Contains only "promising" and "risky" segment entries; "neutral" and "insufficient-data" are omitted.
    // Condition keys use format "GuardrailOverallBehaviorState:<value>",
    // "PredictedConfusionRisk:<value>", or "GuardrailHintDependenceRiskLevel:<value>".
    // Safe to paste under Recursor:PolicyReliability:ConditionalFamilyReliabilityTiers after review.
    // Conditional rules have shadow-only effect until explicitly activated.
    public Dictionary<string, Dictionary<string, string>> ConditionalFamilyReliabilityTiers { get; init; } = new();

    // Keys present:
    //   generatedAtUtc                 — ISO-8601 string
    //   totalEffectivenessRowsAnalyzed — long
    //   aggregatedRowsAnalyzed         — int
    //   familiesEvaluated              — int
    //   source                         — "Phase9A"
    //   warning                        — human-review message
    //   reviewRequired                 — List<Dictionary<string,string>> of conditional families
    //                                    each entry: { family, globalTier, reason }
    public Dictionary<string, object> Metadata { get; init; } = new();
}
