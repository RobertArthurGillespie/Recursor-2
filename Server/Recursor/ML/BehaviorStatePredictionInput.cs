using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Runtime inference input for ML.NET prediction engines.
/// All numeric fields are float (ML.NET convention); strings are passed as-is.
/// </summary>
public class BehaviorStatePredictionInput
{
    // ── Dimension scores ──────────────────────────────────────────────────────

    [ColumnName("AttentionDetection")]
    public float AttentionDetection { get; set; }

    [ColumnName("GoalUnderstanding")]
    public float GoalUnderstanding { get; set; }

    [ColumnName("ProcedureSequencing")]
    public float ProcedureSequencing { get; set; }

    [ColumnName("PaceRegulation")]
    public float PaceRegulation { get; set; }

    [ColumnName("SelfCorrection")]
    public float SelfCorrection { get; set; }

    [ColumnName("FeedbackResponsiveness")]
    public float FeedbackResponsiveness { get; set; }

    [ColumnName("SafetyCompliance")]
    public float SafetyCompliance { get; set; }

    [ColumnName("TaskContinuity")]
    public float TaskContinuity { get; set; }

    // ── Higher-order behavior scores ──────────────────────────────────────────

    [ColumnName("ConfusionScore")]
    public float ConfusionScore { get; set; }

    [ColumnName("HesitationScore")]
    public float HesitationScore { get; set; }

    [ColumnName("ImpulsivityScore")]
    public float ImpulsivityScore { get; set; }

    [ColumnName("HintDependenceScore")]
    public float HintDependenceScore { get; set; }

    // ── Trajectory features ───────────────────────────────────────────────────

    [ColumnName("GoalTrend")]
    public float GoalTrend { get; set; }

    [ColumnName("AttentionTrend")]
    public float AttentionTrend { get; set; }

    [ColumnName("ConfusionTrend")]
    public float ConfusionTrend { get; set; }

    [ColumnName("HintDependenceTrend")]
    public float HintDependenceTrend { get; set; }

    // ── Adaptive state ────────────────────────────────────────────────────────

    [ColumnName("CurrentHintMode")]
    public string CurrentHintMode { get; set; } = "";

    [ColumnName("CurrentDifficulty")]
    public float CurrentDifficulty { get; set; }

    [ColumnName("CurrentTimePressure")]
    public float CurrentTimePressure { get; set; }

    [ColumnName("CurrentErrorTolerance")]
    public float CurrentErrorTolerance { get; set; }

    // ── Trajectory counters ───────────────────────────────────────────────────

    [ColumnName("ConsecutiveStableMasteryWindows")]
    public float ConsecutiveStableMasteryWindows { get; set; }

    [ColumnName("ConsecutiveRelapseWindows")]
    public float ConsecutiveRelapseWindows { get; set; }

    // ── Window summary features ───────────────────────────────────────────────

    [ColumnName("EventCountInWindow")]
    public float EventCountInWindow { get; set; }

    [ColumnName("ErrorCountInWindow")]
    public float ErrorCountInWindow { get; set; }

    [ColumnName("HintCountInWindow")]
    public float HintCountInWindow { get; set; }

    [ColumnName("StepCompleteCountInWindow")]
    public float StepCompleteCountInWindow { get; set; }

    // ── Context ───────────────────────────────────────────────────────────────

    [ColumnName("TaskType")]
    public string TaskType { get; set; } = "";
}
