using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Mirrors the exported CSV used for offline training of Recursor ML models.
/// Each row corresponds to one behavior-state feature vector with ground-truth labels.
/// </summary>
public class BehaviorStateTrainingExample
{
    [LoadColumn(0)]
    public string SessionId { get; set; } = "";

    [LoadColumn(1)]
    public string SimId { get; set; } = "";

    [LoadColumn(2)]
    public string ScenarioId { get; set; } = "";

    [LoadColumn(3)]
    public float WindowIndex { get; set; }

    [LoadColumn(4)]
    public string TaskType { get; set; } = "";

    [LoadColumn(5)]
    public string CreatedAtUtc { get; set; } = "";

    [LoadColumn(6)]
    public float AttentionDetection { get; set; }

    [LoadColumn(7)]
    public float GoalUnderstanding { get; set; }

    [LoadColumn(8)]
    public float ProcedureSequencing { get; set; }

    [LoadColumn(9)]
    public float PaceRegulation { get; set; }

    [LoadColumn(10)]
    public float SelfCorrection { get; set; }

    [LoadColumn(11)]
    public float FeedbackResponsiveness { get; set; }

    [LoadColumn(12)]
    public float SafetyCompliance { get; set; }

    [LoadColumn(13)]
    public float TaskContinuity { get; set; }

    [LoadColumn(14)]
    public float ConfusionScore { get; set; }

    [LoadColumn(15)]
    public float HesitationScore { get; set; }

    [LoadColumn(16)]
    public float ImpulsivityScore { get; set; }

    [LoadColumn(17)]
    public float HintDependenceScore { get; set; }

    [LoadColumn(18)]
    public float GoalTrend { get; set; }

    [LoadColumn(19)]
    public float AttentionTrend { get; set; }

    [LoadColumn(20)]
    public float ConfusionTrend { get; set; }

    [LoadColumn(21)]
    public float HintDependenceTrend { get; set; }

    [LoadColumn(22)]
    public string CurrentHintMode { get; set; } = "";

    [LoadColumn(23)]
    public float CurrentDifficulty { get; set; }

    [LoadColumn(24)]
    public float CurrentTimePressure { get; set; }

    [LoadColumn(25)]
    public float CurrentErrorTolerance { get; set; }

    [LoadColumn(26)]
    public float ConsecutiveStableMasteryWindows { get; set; }

    [LoadColumn(27)]
    public float ConsecutiveRelapseWindows { get; set; }

    [LoadColumn(28)]
    public float EventCountInWindow { get; set; }

    [LoadColumn(29)]
    public float ErrorCountInWindow { get; set; }

    [LoadColumn(30)]
    public float HintCountInWindow { get; set; }

    [LoadColumn(31)]
    public float StepCompleteCountInWindow { get; set; }

    [LoadColumn(32)]
    public bool LabelConfusion { get; set; }

    [LoadColumn(33)]
    public bool LabelHintDependence { get; set; }

    [LoadColumn(34)]
    public bool LabelStableMastery { get; set; }
}
