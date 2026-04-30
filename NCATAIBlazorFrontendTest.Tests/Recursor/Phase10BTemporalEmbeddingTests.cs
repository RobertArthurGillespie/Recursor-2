using System.Text.Json;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10B tests: TemporalEmbeddingService — vector stability, one-hot encodings,
/// null/small-history handling, prediction target generation, and no-adaptation-change guarantee.
///
/// All tests are pure unit tests. No ADX, no ingestion pipeline, no DI.
/// </summary>
public class Phase10BTemporalEmbeddingTests
{
    private readonly TemporalEmbeddingService _svc = new();

    // ── Snapshot helper ──────────────────────────────────────────────────────

    private static TrajectorySnapshot MakeSnapshot(
        int windowIndex,
        double confusion = 0.4,
        double hint = 0.3,
        double goal = 0.6,
        double selfCorrection = 0.5,
        string behaviorState = "") =>
        new()
        {
            WindowIndex = windowIndex,
            ConfusionScore = confusion,
            HintDependenceScore = hint,
            GoalUnderstanding = goal,
            SelfCorrection = selfCorrection,
            OverallBehaviorState = behaviorState,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static SequenceFeatureSummary MakeSeq(int windowCount = 3) =>
        new()
        {
            WindowCount = windowCount,
            MeanConfusionScore = 0.35,
            ConfusionTrendSlope = -0.05,
            GoalTrendSlope = 0.04,
            HintTrendSlope = -0.02,
            VolatilityScore = 0.08,
            MomentumScore = 0.06,
        };

    private static TrajectorySummary MakeTraj(
        bool improving = false,
        bool deteriorating = false,
        bool isVolatile = false,
        double stability = 0.7,
        string risk = "low") =>
        new()
        {
            IsImproving = improving,
            IsDeteriorating = deteriorating,
            IsVolatile = isVolatile,
            StabilityScore = stability,
            PredictedNearTermRisk = risk,
        };

    // ── Vector structure ─────────────────────────────────────────────────────

    [Fact]
    public void BuildEmbedding_StableLength_Returns25Values()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), MakeTraj(), null, null);

        Assert.Equal(25, emb.Values.Length);
    }

    [Fact]
    public void BuildEmbedding_FeatureNamesStaticList_Has25Entries()
    {
        Assert.Equal(25, TemporalEmbeddingService.FeatureNames.Length);
    }

    [Fact]
    public void BuildEmbedding_FeatureNamesJson_DeserializesTo25Names()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), MakeTraj(), null, null);

        var names = JsonSerializer.Deserialize<string[]>(emb.FeatureNamesJson);
        Assert.NotNull(names);
        Assert.Equal(25, names!.Length);
    }

    [Fact]
    public void BuildEmbedding_FeatureNames_MatchStaticList()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), MakeTraj(), null, null);

        var names = JsonSerializer.Deserialize<string[]>(emb.FeatureNamesJson)!;
        Assert.Equal(TemporalEmbeddingService.FeatureNames, names);
    }

    [Fact]
    public void BuildEmbedding_ValuesLength_EqualsFeatureNamesLength()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), MakeTraj(), null, null);

        var names = JsonSerializer.Deserialize<string[]>(emb.FeatureNamesJson)!;
        Assert.Equal(emb.Values.Length, names.Length);
    }

    [Fact]
    public void BuildEmbedding_EmbeddingVersion_IsSet()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, null, null);

        Assert.Equal(TemporalEmbeddingService.EmbeddingVersion, emb.EmbeddingVersion);
        Assert.NotEmpty(emb.EmbeddingVersion);
    }

    // ── Current behavior scores at correct indices ────────────────────────────

    [Fact]
    public void BuildEmbedding_ConfusionScore_IsAtIndex0()
    {
        var snap = MakeSnapshot(0, confusion: 0.72);
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1", snap, null, null, null, null);

        Assert.Equal(0.72, emb.Values[0], precision: 9);
        Assert.Equal("ConfusionScore", TemporalEmbeddingService.FeatureNames[0]);
    }

    [Fact]
    public void BuildEmbedding_HintDependenceScore_IsAtIndex1()
    {
        var snap = MakeSnapshot(0, hint: 0.55);
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1", snap, null, null, null, null);

        Assert.Equal(0.55, emb.Values[1], precision: 9);
    }

    // ── Categorical one-hot encodings ─────────────────────────────────────────

    [Fact]
    public void BuildEmbedding_StateStruggling_SetsIndex17AndZerosOthers()
    {
        var guardrail = new MultiSignalGuardrailSummary { OverallBehaviorState = "struggling" };
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, guardrail, null);

        Assert.Equal(1.0, emb.Values[17]); // StateStruggling
        Assert.Equal(0.0, emb.Values[18]); // StateRecovering
        Assert.Equal(0.0, emb.Values[19]); // StateStable
        Assert.Equal(0.0, emb.Values[20]); // StateMastering
        Assert.Equal(0.0, emb.Values[21]); // StateConflicted
    }

    [Fact]
    public void BuildEmbedding_StateMastering_SetsIndex20()
    {
        var guardrail = new MultiSignalGuardrailSummary { OverallBehaviorState = "mastering" };
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, guardrail, null);

        Assert.Equal(0.0, emb.Values[17]);
        Assert.Equal(0.0, emb.Values[18]);
        Assert.Equal(0.0, emb.Values[19]);
        Assert.Equal(1.0, emb.Values[20]); // StateMastering
        Assert.Equal(0.0, emb.Values[21]);
    }

    [Fact]
    public void BuildEmbedding_StateConflicted_SetsIndex21()
    {
        var guardrail = new MultiSignalGuardrailSummary { OverallBehaviorState = "conflicted" };
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, guardrail, null);

        Assert.Equal(0.0, emb.Values[17]);
        Assert.Equal(0.0, emb.Values[18]);
        Assert.Equal(0.0, emb.Values[19]);
        Assert.Equal(0.0, emb.Values[20]);
        Assert.Equal(1.0, emb.Values[21]); // StateConflicted
    }

    [Fact]
    public void BuildEmbedding_RiskMedium_SetsIndex15AndZerosOthers()
    {
        var traj = MakeTraj(risk: "medium");
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, traj, null, null);

        Assert.Equal(0.0, emb.Values[14]); // PredictedRiskLow
        Assert.Equal(1.0, emb.Values[15]); // PredictedRiskMedium
        Assert.Equal(0.0, emb.Values[16]); // PredictedRiskHigh
    }

    [Fact]
    public void BuildEmbedding_RiskHigh_SetsIndex16()
    {
        var traj = MakeTraj(risk: "high");
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, traj, null, null);

        Assert.Equal(0.0, emb.Values[14]);
        Assert.Equal(0.0, emb.Values[15]);
        Assert.Equal(1.0, emb.Values[16]);
    }

    [Fact]
    public void BuildEmbedding_IsImproving_SetsIndex11()
    {
        var traj = MakeTraj(improving: true);
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), traj, null, null);

        Assert.Equal(1.0, emb.Values[11]); // IsImproving
        Assert.Equal(0.0, emb.Values[12]); // IsDeteriorating
        Assert.Equal(0.0, emb.Values[13]); // IsVolatile
    }

    [Fact]
    public void BuildEmbedding_IsDeteriorating_SetsIndex12()
    {
        var traj = MakeTraj(deteriorating: true);
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(), traj, null, null);

        Assert.Equal(0.0, emb.Values[11]);
        Assert.Equal(1.0, emb.Values[12]);
        Assert.Equal(0.0, emb.Values[13]);
    }

    // ── Null/missing input handling ────────────────────────────────────────────

    [Fact]
    public void BuildEmbedding_NullSequenceSummary_SlopesAreZero()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0, confusion: 0.5), null, null, null, null);

        Assert.Equal(0.0, emb.Values[5]);  // ConfusionTrendSlope
        Assert.Equal(0.0, emb.Values[6]);  // GoalTrendSlope
        Assert.Equal(0.0, emb.Values[7]);  // HintTrendSlope
        Assert.Equal(0.0, emb.Values[8]);  // VolatilityScore
        Assert.Equal(0.0, emb.Values[9]);  // MomentumScore
        // MeanConfusionScore falls back to snapshot confusion
        Assert.Equal(0.5, emb.Values[4], precision: 9);
    }

    [Fact]
    public void BuildEmbedding_NullTrajectorySummary_FlagsAreZero()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, null, null);

        Assert.Equal(0.0, emb.Values[11]); // IsImproving
        Assert.Equal(0.0, emb.Values[12]); // IsDeteriorating
        Assert.Equal(0.0, emb.Values[13]); // IsVolatile
        Assert.Equal(1.0, emb.Values[14]); // PredictedRiskLow — default
        Assert.Equal(1.0, emb.Values[10]); // StabilityScore defaults to 1.0
    }

    [Fact]
    public void BuildEmbedding_NullFeatureVector_AdaptiveParametersAreZero()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, null, featureVector: null);

        Assert.Equal(0.0, emb.Values[22]); // CurrentDifficulty
        Assert.Equal(0.0, emb.Values[23]); // CurrentTimePressure
        Assert.Equal(0.0, emb.Values[24]); // CurrentErrorTolerance
    }

    [Fact]
    public void BuildEmbedding_UnknownState_AllStateOneHotsAreZero()
    {
        var guardrail = new MultiSignalGuardrailSummary { OverallBehaviorState = "unknown" };
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, guardrail, null);

        Assert.Equal(0.0, emb.Values[17]);
        Assert.Equal(0.0, emb.Values[18]);
        Assert.Equal(0.0, emb.Values[19]);
        Assert.Equal(0.0, emb.Values[20]);
        Assert.Equal(0.0, emb.Values[21]);
    }

    [Fact]
    public void BuildEmbedding_GuardrailStateOverridesSnapshotState()
    {
        // Snapshot has "recovering" stamped, but guardrail says "struggling".
        // Guardrail takes priority.
        var snap = MakeSnapshot(0, behaviorState: "recovering");
        var guardrail = new MultiSignalGuardrailSummary { OverallBehaviorState = "struggling" };
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1", snap, null, null, guardrail, null);

        Assert.Equal(1.0, emb.Values[17]); // StateStruggling
        Assert.Equal(0.0, emb.Values[18]); // StateRecovering
    }

    [Fact]
    public void BuildEmbedding_NoGuardrail_FallsBackToSnapshotState()
    {
        var snap = MakeSnapshot(0, behaviorState: "stable");
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1", snap, null, null, null, null);

        Assert.Equal(0.0, emb.Values[17]); // StateStruggling
        Assert.Equal(1.0, emb.Values[19]); // StateStable
    }

    // ── Sequence window count ─────────────────────────────────────────────────

    [Fact]
    public void BuildEmbedding_SequenceWindowCount_ReflectsSequenceSummary()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), MakeSeq(windowCount: 4), null, null, null);

        Assert.Equal(4, emb.SequenceWindowCount);
    }

    [Fact]
    public void BuildEmbedding_NullSequenceSummary_SequenceWindowCountIsOne()
    {
        var emb = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1",
            MakeSnapshot(0), null, null, null, null);

        Assert.Equal(1, emb.SequenceWindowCount);
    }

    // ── Prediction targets ───────────────────────────────────────────────────

    [Fact]
    public void BuildPredictionTargets_EmptySnapshots_ReturnsEmpty()
    {
        var targets = _svc.BuildPredictionTargets("s1", "u1", [], null);
        Assert.Empty(targets);
    }

    [Fact]
    public void BuildPredictionTargets_SingleSnapshot_ReturnsEmpty()
    {
        var snapshots = new List<TrajectorySnapshot> { MakeSnapshot(0) };
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);
        Assert.Empty(targets);
    }

    [Fact]
    public void BuildPredictionTargets_TwoSnapshots_ReturnsHorizon1Only()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            MakeSnapshot(0),
            MakeSnapshot(1, confusion: 0.3, goal: 0.7),
        };
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Single(targets);
        Assert.Equal(1, targets[0].Horizon);
        Assert.Equal(0, targets[0].SourceWindowIndex);
        Assert.Equal(1, targets[0].TargetWindowIndex);
    }

    [Fact]
    public void BuildPredictionTargets_ThreeSnapshots_ReturnsHorizons1And2()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            MakeSnapshot(0),
            MakeSnapshot(1),
            MakeSnapshot(2),
        };
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.Horizon == 1 && t.SourceWindowIndex == 1);
        Assert.Contains(targets, t => t.Horizon == 2 && t.SourceWindowIndex == 0);
    }

    [Fact]
    public void BuildPredictionTargets_FourOrMoreSnapshots_ReturnsHorizons1To3()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            MakeSnapshot(0),
            MakeSnapshot(1),
            MakeSnapshot(2),
            MakeSnapshot(3, confusion: 0.25, goal: 0.8, hint: 0.15),
        };
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Equal(3, targets.Count);
        Assert.Contains(targets, t => t.Horizon == 1 && t.SourceWindowIndex == 2 && t.TargetWindowIndex == 3);
        Assert.Contains(targets, t => t.Horizon == 2 && t.SourceWindowIndex == 1 && t.TargetWindowIndex == 3);
        Assert.Contains(targets, t => t.Horizon == 3 && t.SourceWindowIndex == 0 && t.TargetWindowIndex == 3);
    }

    [Fact]
    public void BuildPredictionTargets_MaxHorizonIsThree_EvenWithLargerHistory()
    {
        // 6 snapshots — still only 3 targets (horizons 1, 2, 3)
        var snapshots = Enumerable.Range(0, 6)
            .Select(i => MakeSnapshot(i))
            .ToList();
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Equal(3, targets.Count);
    }

    [Fact]
    public void BuildPredictionTargets_TargetValuesMatchCurrentSnapshot()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            MakeSnapshot(0),
            MakeSnapshot(1, confusion: 0.38, goal: 0.65, hint: 0.22, behaviorState: "recovering"),
        };
        var traj = MakeTraj(risk: "medium");
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, traj);

        var t = Assert.Single(targets);
        Assert.Equal(0.38, t.TargetConfusionScore, precision: 9);
        Assert.Equal(0.65, t.TargetGoalUnderstanding, precision: 9);
        Assert.Equal(0.22, t.TargetHintDependenceScore, precision: 9);
        Assert.Equal("recovering", t.TargetBehaviorState);
        Assert.Equal("medium", t.TargetNearTermRisk);
    }

    [Fact]
    public void BuildPredictionTargets_NullTrajectorySummary_RiskDefaultsToLow()
    {
        var snapshots = new List<TrajectorySnapshot> { MakeSnapshot(0), MakeSnapshot(1) };
        var targets = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Equal("low", targets[0].TargetNearTermRisk);
    }

    // ── No adaptation change guarantee ────────────────────────────────────────

    [Fact]
    public void BuildEmbedding_DoesNotMutateSnapshot()
    {
        var snap = MakeSnapshot(3, confusion: 0.5, hint: 0.4, goal: 0.6, behaviorState: "stable");
        var originalState = snap.OverallBehaviorState;
        var originalWindow = snap.WindowIndex;

        _ = _svc.BuildEmbedding("s1", "u1", "sim1", "sc1", snap, null, null, null, null);

        Assert.Equal(originalState, snap.OverallBehaviorState);
        Assert.Equal(originalWindow, snap.WindowIndex);
    }

    [Fact]
    public void BuildPredictionTargets_DoesNotMutateSnapshotList()
    {
        var snapshots = new List<TrajectorySnapshot>
        {
            MakeSnapshot(0),
            MakeSnapshot(1),
            MakeSnapshot(2),
        };
        var countBefore = snapshots.Count;

        _ = _svc.BuildPredictionTargets("s1", "u1", snapshots, null);

        Assert.Equal(countBefore, snapshots.Count);
    }
}
