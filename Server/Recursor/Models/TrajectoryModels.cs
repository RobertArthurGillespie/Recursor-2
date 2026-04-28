namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class TrajectorySnapshot
{
    public int WindowIndex { get; set; }
    public double AttentionDetection { get; set; }
    public double GoalUnderstanding { get; set; }
    public double ProcedureSequencing { get; set; }
    public double SelfCorrection { get; set; }
    public double FeedbackResponsiveness { get; set; }
    public double ConfusionScore { get; set; }
    public double HesitationScore { get; set; }
    public double ImpulsivityScore { get; set; }
    public double HintDependenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    // Phase 10A: stamped after multi-signal guardrail runs; empty string when guardrail did not fire.
    public string OverallBehaviorState { get; set; } = "";
}

public class TrajectoryAnalysisResult
{
    public bool HasEnoughHistory { get; set; }
    public bool IsImproving { get; set; }
    public bool IsWorsening { get; set; }
    public bool IsStableHighPerformance { get; set; }
    public bool IsRelapsing { get; set; }
    public double ConfusionTrend { get; set; }
    public double HintDependenceTrend { get; set; }
    public double GoalTrend { get; set; }
    public double AttentionTrend { get; set; }
    public List<string> TrajectoryLabels { get; set; } = new();
}

// Phase 10A: sequence-aware feature summary computed from the last N windows (default N=5).
// Contains regression slopes, volatility, and momentum across the sliding window.
public class SequenceFeatureSummary
{
    public int WindowCount { get; set; }
    public double MeanConfusionScore { get; set; }
    public double ConfusionTrendSlope { get; set; }   // OLS slope of confusion over recent windows
    public double GoalTrendSlope { get; set; }         // OLS slope of goal understanding
    public double HintTrendSlope { get; set; }         // OLS slope of hint dependence
    public double ErrorRateTrendSlope { get; set; }    // derived from SelfCorrection
    public double VolatilityScore { get; set; }        // population std dev of confusion scores
    public double MomentumScore { get; set; }          // positive = improving (confusion ↓ and/or goal ↑)
    public string RecentStateTransitions { get; set; } = ""; // e.g. "struggling→recovering→stable"
}

// Phase 10A: trajectory classification derived from SequenceFeatureSummary using interpretable rules.
// No ML involved — based on slopes and volatility thresholds.
public class TrajectorySummary
{
    public bool IsImproving { get; set; }
    public bool IsDeteriorating { get; set; }
    public bool IsVolatile { get; set; }
    public double StabilityScore { get; set; }           // 0 (chaotic) → 1 (stable)
    public string PredictedNearTermRisk { get; set; } = "low";  // "low" | "medium" | "high"
    public bool InsufficientData { get; set; }
}
