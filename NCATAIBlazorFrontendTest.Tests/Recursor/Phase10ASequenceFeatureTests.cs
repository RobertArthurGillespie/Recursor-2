using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10A tests: SequenceFeatureExtractor — slope, volatility, momentum,
/// state transitions, trajectory classification, and edge cases.
///
/// All internal static helpers are tested directly. No ADX, no ingestion pipeline.
/// </summary>
public class Phase10ASequenceFeatureTests
{
    private readonly SequenceFeatureExtractor _extractor = new();

    // ── LinearSlope ──────────────────────────────────────────────────────────

    [Fact]
    public void LinearSlope_AscendingValues_ReturnsPositiveSlope()
    {
        // [0, 1, 2, 3] — slope should be exactly 1.0
        var result = SequenceFeatureExtractor.LinearSlope([0.0, 1.0, 2.0, 3.0]);
        Assert.Equal(1.0, result, precision: 9);
    }

    [Fact]
    public void LinearSlope_DescendingValues_ReturnsNegativeSlope()
    {
        // [3, 2, 1, 0] — slope should be exactly -1.0
        var result = SequenceFeatureExtractor.LinearSlope([3.0, 2.0, 1.0, 0.0]);
        Assert.Equal(-1.0, result, precision: 9);
    }

    [Fact]
    public void LinearSlope_ConstantValues_ReturnsZero()
    {
        var result = SequenceFeatureExtractor.LinearSlope([0.5, 0.5, 0.5, 0.5]);
        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void LinearSlope_SingleValue_ReturnsZero()
    {
        var result = SequenceFeatureExtractor.LinearSlope([0.7]);
        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void LinearSlope_TwoValues_ReturnsCorrectSlope()
    {
        // [0.2, 0.8] → slope = (0.8 - 0.2) / 1 = 0.6
        var result = SequenceFeatureExtractor.LinearSlope([0.2, 0.8]);
        Assert.Equal(0.6, result, precision: 9);
    }

    // ── StdDev ───────────────────────────────────────────────────────────────

    [Fact]
    public void StdDev_IdenticalValues_ReturnsZero()
    {
        var result = SequenceFeatureExtractor.StdDev([0.4, 0.4, 0.4]);
        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void StdDev_TwoValues_ReturnsCorrectPopulationStdDev()
    {
        // [0.0, 1.0] → mean=0.5, variance=0.25, stddev=0.5
        var result = SequenceFeatureExtractor.StdDev([0.0, 1.0]);
        Assert.Equal(0.5, result, precision: 9);
    }

    [Fact]
    public void StdDev_SingleValue_ReturnsZero()
    {
        var result = SequenceFeatureExtractor.StdDev([0.8]);
        Assert.Equal(0.0, result, precision: 9);
    }

    // ── ComputeMomentum ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeMomentum_ConfusionDecreasingGoalIncreasing_ReturnsPositive()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.8, GoalUnderstanding = 0.3 },
            new() { ConfusionScore = 0.8, GoalUnderstanding = 0.3 },
            new() { ConfusionScore = 0.3, GoalUnderstanding = 0.8 },
            new() { ConfusionScore = 0.3, GoalUnderstanding = 0.8 },
        };
        var result = SequenceFeatureExtractor.ComputeMomentum(snapshots);
        Assert.True(result > 0, $"Expected positive momentum, got {result}");
    }

    [Fact]
    public void ComputeMomentum_ConfusionIncreasingGoalDecreasing_ReturnsNegative()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.2, GoalUnderstanding = 0.9 },
            new() { ConfusionScore = 0.2, GoalUnderstanding = 0.9 },
            new() { ConfusionScore = 0.8, GoalUnderstanding = 0.2 },
            new() { ConfusionScore = 0.8, GoalUnderstanding = 0.2 },
        };
        var result = SequenceFeatureExtractor.ComputeMomentum(snapshots);
        Assert.True(result < 0, $"Expected negative momentum, got {result}");
    }

    [Fact]
    public void ComputeMomentum_SingleSnapshot_ReturnsZero()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.5, GoalUnderstanding = 0.5 }
        };
        var result = SequenceFeatureExtractor.ComputeMomentum(snapshots);
        Assert.Equal(0.0, result, precision: 9);
    }

    // ── DeriveState ──────────────────────────────────────────────────────────

    [Fact]
    public void DeriveState_HighConfusion_ReturnsStruggling()
    {
        var s = new TrajectorySnapshot { ConfusionScore = 0.7, GoalUnderstanding = 0.5 };
        Assert.Equal("struggling", SequenceFeatureExtractor.DeriveState(s));
    }

    [Fact]
    public void DeriveState_LowGoal_ReturnsStruggling()
    {
        var s = new TrajectorySnapshot { ConfusionScore = 0.4, GoalUnderstanding = 0.2 };
        Assert.Equal("struggling", SequenceFeatureExtractor.DeriveState(s));
    }

    [Fact]
    public void DeriveState_LowConfusionHighGoal_ReturnsMastering()
    {
        var s = new TrajectorySnapshot { ConfusionScore = 0.2, GoalUnderstanding = 0.8 };
        Assert.Equal("mastering", SequenceFeatureExtractor.DeriveState(s));
    }

    [Fact]
    public void DeriveState_MidRangeGood_ReturnsStable()
    {
        var s = new TrajectorySnapshot { ConfusionScore = 0.4, GoalUnderstanding = 0.6 };
        Assert.Equal("stable", SequenceFeatureExtractor.DeriveState(s));
    }

    [Fact]
    public void DeriveState_AmbiguousMiddle_ReturnsRecovering()
    {
        // confusion=0.55 (not < 0.50), goal=0.55 (not > 0.65 for mastering, but > 0.50)
        var s = new TrajectorySnapshot { ConfusionScore = 0.55, GoalUnderstanding = 0.55 };
        Assert.Equal("recovering", SequenceFeatureExtractor.DeriveState(s));
    }

    // ── BuildStateTransitions ─────────────────────────────────────────────────

    [Fact]
    public void BuildStateTransitions_DeduplicatesConsecutiveIdenticalStates()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { OverallBehaviorState = "struggling" },
            new() { OverallBehaviorState = "struggling" },
            new() { OverallBehaviorState = "recovering" },
            new() { OverallBehaviorState = "stable" },
            new() { OverallBehaviorState = "stable" },
        };
        var result = SequenceFeatureExtractor.BuildStateTransitions(snapshots);
        Assert.Equal("struggling→recovering→stable", result);
    }

    [Fact]
    public void BuildStateTransitions_UsesOverallBehaviorStateWhenSet()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { OverallBehaviorState = "mastering", ConfusionScore = 0.9 }, // would derive "struggling" without the stamp
            new() { OverallBehaviorState = "mastering", ConfusionScore = 0.9 },
        };
        var result = SequenceFeatureExtractor.BuildStateTransitions(snapshots);
        Assert.Equal("mastering", result); // not "struggling"
    }

    [Fact]
    public void BuildStateTransitions_FallsBackToDerivedStateWhenEmpty()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { OverallBehaviorState = "", ConfusionScore = 0.2, GoalUnderstanding = 0.9 },
        };
        var result = SequenceFeatureExtractor.BuildStateTransitions(snapshots);
        Assert.Equal("mastering", result);
    }

    [Fact]
    public void BuildStateTransitions_EmptyList_ReturnsEmptyString()
    {
        var result = SequenceFeatureExtractor.BuildStateTransitions([]);
        Assert.Equal("", result);
    }

    // ── Extract ──────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_FewerThanTwoSnapshots_ReturnsNull()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.5, GoalUnderstanding = 0.5 }
        };
        var result = _extractor.Extract(snapshots);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_TwoOrMoreSnapshots_ReturnsNonNull()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.6, GoalUnderstanding = 0.4, SelfCorrection = 0.5 },
            new() { ConfusionScore = 0.4, GoalUnderstanding = 0.6, SelfCorrection = 0.7 },
        };
        var result = _extractor.Extract(snapshots);
        Assert.NotNull(result);
        Assert.Equal(2, result.WindowCount);
    }

    [Fact]
    public void Extract_ConfusionTrendSlope_IsNegativeWhenConfusionDecreasing()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            new() { ConfusionScore = 0.8, GoalUnderstanding = 0.3 },
            new() { ConfusionScore = 0.5, GoalUnderstanding = 0.5 },
            new() { ConfusionScore = 0.2, GoalUnderstanding = 0.8 },
        };
        var result = _extractor.Extract(snapshots);
        Assert.NotNull(result);
        Assert.True(result.ConfusionTrendSlope < 0, $"Expected negative slope, got {result.ConfusionTrendSlope}");
    }

    // ── Classify ──────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_StrongImprovingSignals_ReturnsIsImproving()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = -0.10, // < -0.05 threshold
            GoalTrendSlope = 0.06,       // > 0.03 threshold
            VolatilityScore = 0.05,
            MomentumScore = 0.1
        };
        var result = _extractor.Classify(summary);
        Assert.True(result.IsImproving);
        Assert.False(result.IsDeteriorating);
    }

    [Fact]
    public void Classify_StrongDeterioratingSignals_ReturnsIsDeteriorating()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.08,  // > 0.05 threshold
            GoalTrendSlope = -0.06,      // < -0.03 threshold
            VolatilityScore = 0.05,
            MomentumScore = -0.05
        };
        var result = _extractor.Classify(summary);
        Assert.True(result.IsDeteriorating);
        Assert.False(result.IsImproving);
    }

    [Fact]
    public void Classify_HighVolatility_ReturnsIsVolatile()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.0,
            GoalTrendSlope = 0.0,
            VolatilityScore = 0.20, // > 0.12 threshold
            MomentumScore = 0.0
        };
        var result = _extractor.Classify(summary);
        Assert.True(result.IsVolatile);
    }

    [Fact]
    public void Classify_HighRiskSlope_ReturnsPredictedRiskHigh()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.10, // > 0.08 high-risk threshold
            GoalTrendSlope = -0.01,
            VolatilityScore = 0.05,
            MomentumScore = 0.0
        };
        var result = _extractor.Classify(summary);
        Assert.Equal("high", result.PredictedNearTermRisk);
    }

    [Fact]
    public void Classify_MediumRiskSlope_ReturnsPredictedRiskMedium()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.05, // > 0.03 medium-risk, < 0.08 high-risk
            GoalTrendSlope = 0.0,
            VolatilityScore = 0.05,
            MomentumScore = 0.0
        };
        var result = _extractor.Classify(summary);
        Assert.Equal("medium", result.PredictedNearTermRisk);
    }

    [Fact]
    public void Classify_NoRiskSignals_ReturnsPredictedRiskLow()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = -0.02,
            GoalTrendSlope = 0.01,
            VolatilityScore = 0.05,
            MomentumScore = 0.02
        };
        var result = _extractor.Classify(summary);
        Assert.Equal("low", result.PredictedNearTermRisk);
    }

    [Fact]
    public void Classify_HighRiskMomentum_ReturnsPredictedRiskHigh()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.01, // not slope-triggered
            GoalTrendSlope = 0.0,
            VolatilityScore = 0.05,
            MomentumScore = -0.15 // < -0.12 high-risk threshold
        };
        var result = _extractor.Classify(summary);
        Assert.Equal("high", result.PredictedNearTermRisk);
    }

    [Fact]
    public void Classify_InsufficientDataAlwaysFalse()
    {
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.0,
            GoalTrendSlope = 0.0,
            VolatilityScore = 0.0,
            MomentumScore = 0.0
        };
        var result = _extractor.Classify(summary);
        Assert.False(result.InsufficientData);
    }

    [Fact]
    public void Classify_StabilityScore_ClampedBetweenZeroAndOne()
    {
        // Extreme volatility would push raw calculation negative — clamp ensures [0,1].
        var summary = new SequenceFeatureSummary
        {
            ConfusionTrendSlope = 0.0,
            GoalTrendSlope = 0.0,
            VolatilityScore = 1.0, // extremely high
            MomentumScore = 0.0
        };
        var result = _extractor.Classify(summary);
        Assert.True(result.StabilityScore >= 0.0 && result.StabilityScore <= 1.0,
            $"StabilityScore {result.StabilityScore} is out of [0, 1]");
    }
}
