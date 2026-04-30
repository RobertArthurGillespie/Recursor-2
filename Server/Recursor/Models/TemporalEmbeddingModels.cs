namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

// Phase 10B: fixed-length embedding vector summarising one learner window.
// Values and FeatureNames are parallel arrays — index i in Values corresponds to FeatureNames[i].
// EmbeddingVersion lets consumers detect schema changes if new features are added later.
public class TemporalEmbeddingVector
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public int SequenceWindowCount { get; set; }
    public double[] Values { get; set; } = [];
    public string FeatureNamesJson { get; set; } = "";
    public string EmbeddingVersion { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

// Phase 10B: training target row linking a source window to a later target window.
// Horizon 1 = next window, horizon 2 = two windows ahead, horizon 3 = three windows ahead.
// TargetBehaviorState and TargetNearTermRisk are the ground-truth labels for the target window.
public class TemporalPredictionTarget
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
