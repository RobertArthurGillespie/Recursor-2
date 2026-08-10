using System.Text.Json;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// Row DTOs map 1:1 to ADX table columns defined in recursor-adx-plan.txt.
// Dynamic ADX columns use JsonElement so System.Text.Json serializes them
// as nested JSON objects rather than escaped strings.

public class RawEventRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public int BatchSequence { get; set; }
    public string EventId { get; set; } = "";
    public long SequenceNumber { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Target { get; set; } = "";
    public JsonElement Metrics { get; set; }
    public JsonElement Context { get; set; }
    public JsonElement Payload { get; set; }
}

public class FeatureWindowRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string WindowType { get; set; } = "";
    public long WindowStartSequence { get; set; }
    public long WindowEndSequence { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string FeatureExtractorVersion { get; set; } = "1.0";
    public JsonElement Features { get; set; }
}

public class BehaviorProfileRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string SourceFeatureWindowId { get; set; } = "";
    public string InterpreterVersion { get; set; } = "1.0";
    public JsonElement DimensionScores { get; set; }
    public JsonElement BehaviorScores { get; set; }
    public DateTime CreatedAtUtc { get; set; }
   
}

public class HypothesisSetRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string SourceBehaviorProfileId { get; set; } = "";
    public string InterpreterMode { get; set; } = "";
    public string InterpreterVersion { get; set; } = "";
    public JsonElement Hypotheses { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AdaptationDecisionRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AdaptationDecisionId { get; set; } = "";
    public int DecisionIndex { get; set; }
    public string SourceHypothesisSetId { get; set; } = "";
    public string PolicyVersion { get; set; } = "1.0";
    public JsonElement InterventionFamilies { get; set; }
    public JsonElement ParameterChanges { get; set; }
    public string ReasoningSummary { get; set; } = "";
    public int ExpiresAfterWindow { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // Next-window guardrail analytics — nullable for backward compatibility with existing ADX rows.
    public double? HintDependenceNextProbability { get; set; }
    public bool NextHintDependenceGuardrailTriggered { get; set; }
    public string? NextHintDependenceGuardrailLevel { get; set; }

    // Phase 8A guardrail notes — null when the modifier did not fire or was disabled.
    public string? Phase8AGuardrailNotes { get; set; }

    // Phase 8E reliability weighting — null when mode is "disabled".
    public string? Phase8EReliabilityNotes { get; set; }
    public string? Phase8EReliabilityMode { get; set; }

    // Phase 10A trajectory intelligence — null when fewer than 2 windows available.
    public string? Phase10TrajectoryNotes { get; set; }

    // Explanation fields (ordinals 18-23) — null for rows written before this phase.
    public string? HypothesisLabelsJson { get; set; }
    public string? LearnerStateSummary { get; set; }
    public string? WhySupportChanged { get; set; }
    public string? CoachMessage { get; set; }
    public string? ConfidenceNote { get; set; }
    public string? ExplanationJson { get; set; }
}

public class BehaviorStateTrainingRow
{
    // Identity / metadata
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string TaskType { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    // Feature columns — dimension scores
    public double AttentionDetection { get; set; }
    public double GoalUnderstanding { get; set; }
    public double ProcedureSequencing { get; set; }
    public double PaceRegulation { get; set; }
    public double SelfCorrection { get; set; }
    public double FeedbackResponsiveness { get; set; }
    public double SafetyCompliance { get; set; }
    public double TaskContinuity { get; set; }

    // Feature columns — higher-order behavior scores
    public double ConfusionScore { get; set; }
    public double HesitationScore { get; set; }
    public double ImpulsivityScore { get; set; }
    public double HintDependenceScore { get; set; }

    // Feature columns — trajectory
    public double GoalTrend { get; set; }
    public double AttentionTrend { get; set; }
    public double ConfusionTrend { get; set; }
    public double HintDependenceTrend { get; set; }

    // Feature columns — adaptive state
    public string CurrentHintMode { get; set; } = "";
    public double CurrentDifficulty { get; set; }
    public double CurrentTimePressure { get; set; }
    public double CurrentErrorTolerance { get; set; }

    // Feature columns — counters
    public int ConsecutiveStableMasteryWindows { get; set; }
    public int ConsecutiveRelapseWindows { get; set; }

    // Feature columns — window summary
    public int EventCountInWindow { get; set; }
    public int ErrorCountInWindow { get; set; }
    public int HintCountInWindow { get; set; }
    public int StepCompleteCountInWindow { get; set; }

    // Weak-label columns
    public int LabelConfusion { get; set; }
    public int LabelHintDependence { get; set; }
    public int LabelStableMastery { get; set; }

    // Shadow prediction columns
    public double PredConfusionProbability { get; set; }
    public double PredHintDependenceProbability { get; set; }
    public double PredStableMasteryProbability { get; set; }
    public string ModelVersion { get; set; } = "";
    public string InferenceMode { get; set; } = "shadow";

    // Confusion shadow prediction detail (Phase 7A — shadow/observability only).
    public string PredictedConfusionRisk { get; set; } = "";
    public string ConfusionModelVersion { get; set; } = "";
    public DateTime? ConfusionPredictionUtc { get; set; }

    // Stable mastery shadow prediction detail (Phase 7B — shadow/observability only).
    public string PredictedStableMasteryState { get; set; } = "";
    public string StableMasteryModelVersion { get; set; } = "";
    public DateTime? StableMasteryPredictionUtc { get; set; }

    // Multi-signal guardrail summary (Phase 7C — shadow/observability only).
    public string GuardrailOverallBehaviorState { get; set; } = "";
    public bool GuardrailConfusionSignalAgreement { get; set; }
    public bool GuardrailStableMasterySignalAgreement { get; set; }
    public string GuardrailHintDependenceRiskLevel { get; set; } = "";
    public int GuardrailConflictCount { get; set; }
    public string GuardrailWarnings { get; set; } = "";  // pipe-separated; use split(" | ") in KQL

    // Phase 10A: sequence-aware features — zero/empty when fewer than 2 snapshots available.
    public int SequenceWindowCount { get; set; }
    public double MeanConfusionScore { get; set; }
    public double ConfusionTrendSlope { get; set; }
    public double GoalTrendSlope { get; set; }
    public double HintTrendSlope { get; set; }
    public double VolatilityScore { get; set; }
    public double MomentumScore { get; set; }
    public double StabilityScore { get; set; }
    public string PredictedNearTermRisk { get; set; } = "";
}

// ── AdaptationEffectiveness row — Phase 8B ────────────────────────────────────
// One row per evaluated adaptation window pair.  Written the window after an
// adaptation fires; never used to influence adaptation decisions.
public class AdaptationEffectivenessRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AdaptationDecisionId { get; set; } = "";
    public int AdaptationDecisionIndex { get; set; }
    public JsonElement InterventionFamilies { get; set; }  // dynamic — JSON array
    public JsonElement ParameterChanges { get; set; }      // dynamic — JSON array
    public int AppliedAtWindowIndex { get; set; }
    public int EvaluatedAtWindowIndex { get; set; }
    public double PreConfusionScore { get; set; }
    public double PreHintDependenceScore { get; set; }
    public double PreGoalUnderstanding { get; set; }
    public double NextConfusionScore { get; set; }
    public double NextHintDependenceScore { get; set; }
    public double NextGoalUnderstanding { get; set; }
    public double ConfusionDelta { get; set; }
    public double HintDependenceDelta { get; set; }
    public double GoalUnderstandingDelta { get; set; }
    public string Outcome { get; set; } = "";  // "improved" | "neutral" | "worsened"
    public DateTime CreatedAtUtc { get; set; }
}

// ── UserBehaviorProfiles row ──────────────────────────────────────────────────
// Full profile snapshot. ADX is append-only; use arg_max(UpdatedAtUtc, *) to
// retrieve the current profile for a given UserId.
public class UserBehaviorProfileRow
{
    public string UserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string LastSessionId { get; set; } = "";
    public int TotalSessions { get; set; }
    public int TotalWindows { get; set; }

    public double AvgConfusionScore { get; set; }
    public double AvgHintDependenceScore { get; set; }
    public double AvgHesitationScore { get; set; }
    public double AvgGoalUnderstanding { get; set; }
    public double AvgAttentionDetection { get; set; }
    public double AvgProcedureSequencing { get; set; }
    public double AvgSelfCorrection { get; set; }
    public double AvgTaskContinuity { get; set; }
    public double StableMasteryRate { get; set; }
    public double ConfusionRate { get; set; }
    public double HintDependenceRate { get; set; }

    // Nullable — null means "use global default" for that threshold.
    public double? ConfusionBlockDifficultyIncreaseThreshold { get; set; }
    public double? HintFadeConfusionDeltaThreshold { get; set; }
    public double? HintFadeHintDependenceDeltaThreshold { get; set; }
    public double? HintFadeMaxCurrentConfusionScore { get; set; }
    public double? HintFadeMinCurrentGoalUnderstanding { get; set; }
    public double? DifficultyAccelerationConfusionDeltaThreshold { get; set; }
    public double? DifficultyAccelerationHintDependenceDeltaThreshold { get; set; }
    public double? DifficultyAccelerationMaxCurrentConfusionScore { get; set; }
    public double? DifficultyAccelerationMinCurrentGoalUnderstanding { get; set; }
    public double? DifficultyAccelerationDelta { get; set; }
}

// ── UserBehaviorProfileUpdates row ────────────────────────────────────────────
// Per-window behavioral observation used to reconstruct profile history
// and to audit incremental mean updates.
public class UserBehaviorProfileUpdateRow
{
    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int WindowIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public double ConfusionScore { get; set; }
    public double HintDependenceScore { get; set; }
    public double HesitationScore { get; set; }
    public double GoalUnderstanding { get; set; }
    public double AttentionDetection { get; set; }
    public double ProcedureSequencing { get; set; }
    public double SelfCorrection { get; set; }
    public double TaskContinuity { get; set; }

    public bool HasStableMastery { get; set; }
    public bool HasConfusionPattern { get; set; }
    public bool HasHintDependencePattern { get; set; }
}

// ── Phase 10B rows ────────────────────────────────────────────────────────────

// One row per window: fixed-length interpretable embedding.
// Values and FeatureNames are JSON arrays stored as ADX dynamic columns.
public class TemporalEmbeddingRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public int SequenceWindowCount { get; set; }
    public string EmbeddingVersion { get; set; } = "";
    public string Values { get; set; } = "";       // JSON array of doubles
    public string FeatureNames { get; set; } = ""; // JSON array of strings
    public DateTime CreatedAtUtc { get; set; }
}

// One row per (source window, target window, horizon) pair.
// Written when window K is processed; source windows are K-1, K-2, K-3 when available.
public class TemporalPredictionTargetRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int SourceWindowIndex { get; set; }
    public int TargetWindowIndex { get; set; }
    public int Horizon { get; set; }
    public string TargetBehaviorState { get; set; } = "";
    public string TargetNearTermRisk { get; set; } = "";
    public double TargetConfusionScore { get; set; }
    public double TargetGoalUnderstanding { get; set; }
    public double TargetHintDependenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// ── Phase 10C-2: TemporalRiskPredictions row ──────────────────────────────────
// One row per (session, window, horizon). Written as a shadow/observability record;
// never used to gate or modify adaptation decisions.
public class TemporalRiskPredictionRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public int Horizon { get; set; }
    public string PredictedNearTermRisk { get; set; } = "";
    public double Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

// ── Phase 10D-1: TemporalElevatedRiskPredictions row ──────────────────────────
// Separate table from TemporalRiskPredictions to avoid analytics ambiguity:
// PredictedElevatedRisk uses "low" / "elevated" vocabulary, not "low" / "medium" / "high".
public class TemporalElevatedRiskPredictionRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public int Horizon { get; set; }
    public string PredictedElevatedRisk { get; set; } = "";
    public double Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

// ── Phase 10E: TemporalBehaviorStatePredictions row ───────────────────────────
// Shadow/observability-only. Multiclass over the five canonical behavior-state
// labels (see Server/Recursor/Services/BehaviorStateLabelPolicy.cs). Never used
// to gate or modify adaptation decisions.
public class TemporalBehaviorStatePredictionRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public int Horizon { get; set; }
    public string PredictedBehaviorState { get; set; } = "";
    public double Confidence { get; set; }
    public string ClassProbabilities { get; set; } = ""; // dynamic — JSON object of {label: probability}
    public string ModelVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    // Stage 4 (corrective pass): deterministic logical identity — see
    // BehaviorStatePredictionKeys.ComputePredictionId. Appended as the last column (ordinal 11)
    // rather than reordered in, so this is an additive schema change against any already-ingested
    // rows / existing CSV mapping ordinals.
    public string PredictionId { get; set; } = "";
}
