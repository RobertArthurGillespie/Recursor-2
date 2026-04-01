namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class FeatureWindowDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "FeatureWindow";
    public string SessionId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string WindowType { get; set; } = "batch";
    public long WindowStartSequence { get; set; }
    public long WindowEndSequence { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public BehavioralFeatureSet Features { get; set; } = new();
}

public class BehavioralFeatureSet
{
    public double AttentionDetection { get; set; }
    public double GoalUnderstanding { get; set; }
    public double ProcedureSequencing { get; set; }
    public double PaceRegulation { get; set; }
    public double SelfCorrection { get; set; }
    public double FeedbackResponsiveness { get; set; }
    public double SafetyCompliance { get; set; }
    public double TaskContinuity { get; set; }
}
