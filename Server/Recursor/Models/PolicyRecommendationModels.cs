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
