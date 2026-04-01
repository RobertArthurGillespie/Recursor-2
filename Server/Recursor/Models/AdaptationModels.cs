namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class AdaptationDecisionDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "AdaptationDecision";
    public string SessionId { get; set; } = "";
    public int DecisionIndex { get; set; }
    public string SourceHypothesisSetId { get; set; } = "";
    public List<string> InterventionFamilies { get; set; } = new();
    public List<ParameterChange> ParameterChanges { get; set; } = new();
    public string ReasoningSummary { get; set; } = "";
    public int ExpiresAfterWindow { get; set; } = 1;
}

public class ParameterChange
{
    public string Parameter { get; set; } = "";
    public string Operation { get; set; } = "";
    public object? Value { get; set; }
}
