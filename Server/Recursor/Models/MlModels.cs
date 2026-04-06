namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class BehaviorStateFeatureVector
{
    // Identity / context
    public string SessionId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string TaskType { get; set; } = "";

    // Current dimension scores
    public double AttentionDetection { get; set; }
    public double GoalUnderstanding { get; set; }
    public double ProcedureSequencing { get; set; }
    public double PaceRegulation { get; set; }
    public double SelfCorrection { get; set; }
    public double FeedbackResponsiveness { get; set; }
    public double SafetyCompliance { get; set; }
    public double TaskContinuity { get; set; }

    // Current higher-order behavior scores
    public double ConfusionScore { get; set; }
    public double HesitationScore { get; set; }
    public double ImpulsivityScore { get; set; }
    public double HintDependenceScore { get; set; }

    // Trajectory features
    public double GoalTrend { get; set; }
    public double AttentionTrend { get; set; }
    public double ConfusionTrend { get; set; }
    public double HintDependenceTrend { get; set; }

    // Support / adaptive state
    public string CurrentHintMode { get; set; } = "";
    public double CurrentDifficulty { get; set; }
    public double CurrentTimePressure { get; set; }
    public double CurrentErrorTolerance { get; set; }

    // Trajectory / support counters
    public int ConsecutiveStableMasteryWindows { get; set; }
    public int ConsecutiveRelapseWindows { get; set; }

    // Window summary features
    public int EventCountInWindow { get; set; }
    public int ErrorCountInWindow { get; set; }
    public int HintCountInWindow { get; set; }
    public int StepCompleteCountInWindow { get; set; }
}

public class BehaviorStatePrediction
{
    public double ConfusionProbability { get; set; }
    public double HintDependenceProbability { get; set; }
    public double StableMasteryProbability { get; set; }
    public string ModelVersion { get; set; } = "";
    public string InferenceMode { get; set; } = "shadow";
}
