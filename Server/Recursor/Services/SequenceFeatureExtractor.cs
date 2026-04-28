using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface ISequenceFeatureExtractor
{
    // Returns null when fewer than 2 snapshots are available (insufficient history).
    SequenceFeatureSummary? Extract(IReadOnlyList<TrajectorySnapshot> snapshots);

    // Classifies a non-null SequenceFeatureSummary into a TrajectorySummary using interpretable rules.
    TrajectorySummary Classify(SequenceFeatureSummary summary);
}

public class SequenceFeatureExtractor : ISequenceFeatureExtractor
{
    // Slope thresholds for trajectory classification.
    private const double ImprovingConfusionSlope  = -0.05;
    private const double ImprovingGoalSlope        =  0.03;
    private const double DeterioratingConfusionSlope =  0.05;
    private const double DeterioratingGoalSlope      = -0.03;

    // Volatility threshold (population std dev of confusion scores).
    private const double HighVolatilityThreshold = 0.12;

    // Momentum thresholds for near-term risk.
    private const double HighRiskMomentum   = -0.12;
    private const double MediumRiskMomentum = -0.05;

    // Confusion slope thresholds for near-term risk.
    private const double HighRiskSlope   = 0.08;
    private const double MediumRiskSlope = 0.03;

    public SequenceFeatureSummary? Extract(IReadOnlyList<TrajectorySnapshot> snapshots)
    {
        if (snapshots.Count < 2)
            return null;

        var confusion  = snapshots.Select(s => s.ConfusionScore).ToArray();
        var goal       = snapshots.Select(s => s.GoalUnderstanding).ToArray();
        var hint       = snapshots.Select(s => s.HintDependenceScore).ToArray();
        // ErrorRate derived: SelfCorrection = clamp(1 - errorRate * 2) → errorRate ≈ (1 - SelfCorrection) / 2
        var errorRate  = snapshots.Select(s => Math.Max(0.0, (1.0 - s.SelfCorrection) / 2.0)).ToArray();

        return new SequenceFeatureSummary
        {
            WindowCount          = snapshots.Count,
            MeanConfusionScore   = confusion.Average(),
            ConfusionTrendSlope  = LinearSlope(confusion),
            GoalTrendSlope       = LinearSlope(goal),
            HintTrendSlope       = LinearSlope(hint),
            ErrorRateTrendSlope  = LinearSlope(errorRate),
            VolatilityScore      = StdDev(confusion),
            MomentumScore        = ComputeMomentum(snapshots),
            RecentStateTransitions = BuildStateTransitions(snapshots)
        };
    }

    public TrajectorySummary Classify(SequenceFeatureSummary summary)
    {
        bool improving = summary.ConfusionTrendSlope < ImprovingConfusionSlope
                      && summary.GoalTrendSlope > ImprovingGoalSlope;

        bool deteriorating = summary.ConfusionTrendSlope > DeterioratingConfusionSlope
                          && summary.GoalTrendSlope < DeterioratingGoalSlope;

        bool volatile_ = summary.VolatilityScore > HighVolatilityThreshold;

        double stability = Math.Max(0.0, Math.Min(1.0, 1.0 - summary.VolatilityScore * 4.0));

        string risk;
        if (summary.ConfusionTrendSlope > HighRiskSlope || summary.MomentumScore < HighRiskMomentum)
            risk = "high";
        else if (summary.ConfusionTrendSlope > MediumRiskSlope || summary.MomentumScore < MediumRiskMomentum)
            risk = "medium";
        else
            risk = "low";

        return new TrajectorySummary
        {
            IsImproving           = improving,
            IsDeteriorating       = deteriorating,
            IsVolatile            = volatile_,
            StabilityScore        = stability,
            PredictedNearTermRisk = risk,
            InsufficientData      = false
        };
    }

    // Ordinary least-squares slope for values indexed 0..N-1.
    // Returns 0.0 when N < 2 or all x-values are identical (degenerate case).
    internal static double LinearSlope(double[] values)
    {
        int n = values.Length;
        if (n < 2) return 0.0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < n; i++)
        {
            sumX  += i;
            sumY  += values[i];
            sumXY += i * values[i];
            sumXX += (double)i * i;
        }

        double denominator = n * sumXX - sumX * sumX;
        return denominator == 0.0 ? 0.0 : (n * sumXY - sumX * sumY) / denominator;
    }

    // Population standard deviation.
    internal static double StdDev(double[] values)
    {
        if (values.Length < 2) return 0.0;
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / values.Length;
        return Math.Sqrt(variance);
    }

    // Positive momentum = confusion decreasing and/or goal increasing (learner improving).
    // Splits the window into earlier half and recent half and compares averages.
    internal static double ComputeMomentum(IReadOnlyList<TrajectorySnapshot> snapshots)
    {
        int n = snapshots.Count;
        if (n < 2) return 0.0;

        int earlierCount = n / 2;
        var earlier = snapshots.Take(earlierCount);
        var recent  = snapshots.Skip(earlierCount);

        double earlierConfusion = earlier.Average(s => s.ConfusionScore);
        double recentConfusion  = recent.Average(s => s.ConfusionScore);
        double earlierGoal      = earlier.Average(s => s.GoalUnderstanding);
        double recentGoal       = recent.Average(s => s.GoalUnderstanding);

        // (confusion going down) + (goal going up) both contribute positive momentum.
        return (earlierConfusion - recentConfusion) + (recentGoal - earlierGoal);
    }

    // Builds a compact transition string by deduplicating consecutive identical states.
    // Uses OverallBehaviorState from the snapshot when populated; otherwise derives from scores.
    internal static string BuildStateTransitions(IReadOnlyList<TrajectorySnapshot> snapshots)
    {
        if (snapshots.Count == 0) return "";

        var states = snapshots.Select(s =>
            !string.IsNullOrWhiteSpace(s.OverallBehaviorState)
                ? s.OverallBehaviorState
                : DeriveState(s));

        var distinct = new List<string>();
        foreach (var state in states)
        {
            if (distinct.Count == 0 || distinct[^1] != state)
                distinct.Add(state);
        }

        return string.Join("→", distinct);
    }

    // Simple rule-based state derivation when guardrail state is not available.
    internal static string DeriveState(TrajectorySnapshot s)
    {
        if (s.ConfusionScore > 0.60 || s.GoalUnderstanding < 0.35) return "struggling";
        if (s.ConfusionScore < 0.35 && s.GoalUnderstanding > 0.65) return "mastering";
        if (s.ConfusionScore < 0.50 && s.GoalUnderstanding > 0.50) return "stable";
        return "recovering";
    }
}
