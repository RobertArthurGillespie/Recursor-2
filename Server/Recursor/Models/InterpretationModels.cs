namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class BehaviorProfileDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "BehaviorProfile";
    public string SessionId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string SourceFeatureWindowId { get; set; } = "";
    public Dictionary<string, DimensionScore> DimensionScores { get; set; } = new();
}

public class DimensionScore
{
    public double Score { get; set; }
    public double Confidence { get; set; }
    public string Evidence { get; set; } = "";
}

public class HypothesisSetDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "HypothesisSet";
    public string SessionId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string SourceBehaviorProfileId { get; set; } = "";
    public List<BehavioralHypothesis> Hypotheses { get; set; } = new();
    public string InterpreterMode { get; set; } = "rule-based";
    public string InterpreterVersion { get; set; } = "1.0";
}

public class BehavioralHypothesis
{
    public string Label { get; set; } = "";
    public List<string> Dimensions { get; set; } = new();
    public double Confidence { get; set; }
    public string Evidence { get; set; } = "";
}
