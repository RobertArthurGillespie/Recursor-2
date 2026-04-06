using System.Text.Json;

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
    public int DecisionIndex { get; set; }
    public string SourceHypothesisSetId { get; set; } = "";
    public string PolicyVersion { get; set; } = "1.0";
    public JsonElement InterventionFamilies { get; set; }
    public JsonElement ParameterChanges { get; set; }
    public string ReasoningSummary { get; set; } = "";
    public int ExpiresAfterWindow { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class BehaviorStateTrainingRow
{
    // Identity / metadata
    public string SessionId { get; set; } = "";
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
}
