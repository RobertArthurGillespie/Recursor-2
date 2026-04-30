using System.Text.Json;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface ITemporalEmbeddingService
{
    TemporalEmbeddingVector BuildEmbedding(
        string sessionId,
        string userId,
        string simId,
        string scenarioId,
        TrajectorySnapshot currentSnapshot,
        SequenceFeatureSummary? sequenceSummary,
        TrajectorySummary? trajectorySummary,
        MultiSignalGuardrailSummary? guardrail,
        BehaviorStateFeatureVector? featureVector);

    List<TemporalPredictionTarget> BuildPredictionTargets(
        string sessionId,
        string userId,
        List<TrajectorySnapshot> recentSnapshots,
        TrajectorySummary? trajectorySummary);
}

// Phase 10B: builds interpretable fixed-length temporal embeddings from current-window signals.
// No trained model is used — all values are engineered features.
// Vector length and order are stable within an EmbeddingVersion.
public class TemporalEmbeddingService : ITemporalEmbeddingService
{
    public const string EmbeddingVersion = "v1.0";

    // Stable, ordered list of feature names.  Index i in FeatureNames == index i in the Values array.
    // Do not reorder or remove entries without bumping EmbeddingVersion.
    public static readonly string[] FeatureNames =
    [
        // Current behavior scores (indices 0–3)
        "ConfusionScore",
        "HintDependenceScore",
        "GoalUnderstanding",
        "SelfCorrection",

        // Sequence summary — regression slopes and aggregate stats (indices 4–10)
        "MeanConfusionScore",
        "ConfusionTrendSlope",
        "GoalTrendSlope",
        "HintTrendSlope",
        "VolatilityScore",
        "MomentumScore",
        "StabilityScore",

        // Trajectory flags — one-hot-style, each 0.0 or 1.0 (indices 11–13)
        "IsImproving",
        "IsDeteriorating",
        "IsVolatile",

        // Predicted near-term risk — one-hot over {low, medium, high} (indices 14–16)
        "PredictedRiskLow",
        "PredictedRiskMedium",
        "PredictedRiskHigh",

        // Guardrail behavior state — one-hot over known states (indices 17–21)
        "StateStruggling",
        "StateRecovering",
        "StateStable",
        "StateMastering",
        "StateConflicted",

        // Current adaptive parameters (indices 22–24)
        "CurrentDifficulty",
        "CurrentTimePressure",
        "CurrentErrorTolerance",
    ];

    public TemporalEmbeddingVector BuildEmbedding(
        string sessionId,
        string userId,
        string simId,
        string scenarioId,
        TrajectorySnapshot currentSnapshot,
        SequenceFeatureSummary? sequenceSummary,
        TrajectorySummary? trajectorySummary,
        MultiSignalGuardrailSummary? guardrail,
        BehaviorStateFeatureVector? featureVector)
    {
        var values = BuildValues(currentSnapshot, sequenceSummary, trajectorySummary, guardrail, featureVector);

        return new TemporalEmbeddingVector
        {
            SessionId = sessionId,
            UserId = userId,
            SimId = simId,
            ScenarioId = scenarioId,
            WindowIndex = currentSnapshot.WindowIndex,
            SequenceWindowCount = sequenceSummary?.WindowCount ?? 1,
            Values = values,
            FeatureNamesJson = JsonSerializer.Serialize(FeatureNames),
            EmbeddingVersion = EmbeddingVersion,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public List<TemporalPredictionTarget> BuildPredictionTargets(
        string sessionId,
        string userId,
        List<TrajectorySnapshot> recentSnapshots,
        TrajectorySummary? trajectorySummary)
    {
        var targets = new List<TemporalPredictionTarget>();
        if (recentSnapshots.Count < 2)
            return targets;

        var current = recentSnapshots[^1];
        var now = DateTime.UtcNow;

        // Walk back up to 3 prior windows and create (source→target, horizon) pairs.
        for (int horizon = 1; horizon <= 3; horizon++)
        {
            int sourceIndex = recentSnapshots.Count - 1 - horizon;
            if (sourceIndex < 0)
                break;

            var source = recentSnapshots[sourceIndex];
            targets.Add(new TemporalPredictionTarget
            {
                SessionId = sessionId,
                UserId = userId,
                SourceWindowIndex = source.WindowIndex,
                TargetWindowIndex = current.WindowIndex,
                Horizon = horizon,
                TargetBehaviorState = current.OverallBehaviorState,
                TargetNearTermRisk = trajectorySummary?.PredictedNearTermRisk ?? "low",
                TargetConfusionScore = current.ConfusionScore,
                TargetGoalUnderstanding = current.GoalUnderstanding,
                TargetHintDependenceScore = current.HintDependenceScore,
                CreatedAtUtc = now,
            });
        }

        return targets;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static double[] BuildValues(
        TrajectorySnapshot snap,
        SequenceFeatureSummary? seq,
        TrajectorySummary? traj,
        MultiSignalGuardrailSummary? guardrail,
        BehaviorStateFeatureVector? fv)
    {
        // Guardrail state takes priority; fall back to the snapshot's stamped state.
        var state = !string.IsNullOrEmpty(guardrail?.OverallBehaviorState)
            ? guardrail!.OverallBehaviorState
            : snap.OverallBehaviorState;

        var risk = traj?.PredictedNearTermRisk ?? "low";

        return
        [
            // indices 0–3: current behavior scores
            snap.ConfusionScore,
            snap.HintDependenceScore,
            snap.GoalUnderstanding,
            snap.SelfCorrection,

            // indices 4–10: sequence summary
            seq?.MeanConfusionScore ?? snap.ConfusionScore,
            seq?.ConfusionTrendSlope ?? 0.0,
            seq?.GoalTrendSlope ?? 0.0,
            seq?.HintTrendSlope ?? 0.0,
            seq?.VolatilityScore ?? 0.0,
            seq?.MomentumScore ?? 0.0,
            traj?.StabilityScore ?? 1.0,

            // indices 11–13: trajectory flags
            traj?.IsImproving == true ? 1.0 : 0.0,
            traj?.IsDeteriorating == true ? 1.0 : 0.0,
            traj?.IsVolatile == true ? 1.0 : 0.0,

            // indices 14–16: risk one-hot
            risk == "low"    ? 1.0 : 0.0,
            risk == "medium" ? 1.0 : 0.0,
            risk == "high"   ? 1.0 : 0.0,

            // indices 17–21: state one-hot
            state == "struggling" ? 1.0 : 0.0,
            state == "recovering" ? 1.0 : 0.0,
            state == "stable"     ? 1.0 : 0.0,
            state == "mastering"  ? 1.0 : 0.0,
            state == "conflicted" ? 1.0 : 0.0,

            // indices 22–24: adaptive parameters
            fv?.CurrentDifficulty ?? 0.0,
            fv?.CurrentTimePressure ?? 0.0,
            fv?.CurrentErrorTolerance ?? 0.0,
        ];
    }
}
