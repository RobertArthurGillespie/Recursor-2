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

    // Next-window guardrail analytics — null when model was not loaded or did not fire.
    public double? HintDependenceNextProbability { get; set; }
    public bool NextHintDependenceGuardrailTriggered { get; set; }
    public string? NextHintDependenceGuardrailLevel { get; set; }
}

public class ParameterChange
{
    public string Parameter { get; set; } = "";
    public string Operation { get; set; } = "";
    public object? Value { get; set; }
}
