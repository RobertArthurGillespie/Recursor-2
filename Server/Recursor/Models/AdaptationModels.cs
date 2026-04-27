namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class AdaptationDecisionDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "AdaptationDecision";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
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

    // Phase 8A guardrail notes — null when the modifier did not fire or was disabled.
    // Pipe-separated list of blocked actions and the triggering rule/field.
    public string? Phase8AGuardrailNotes { get; set; }

    // Phase 8E reliability weighting — null when mode is "disabled".
    public string? Phase8EReliabilityNotes { get; set; }
    // Mirrors RecursorPolicyReliabilityOptions.Mode at decision time; null when disabled.
    public string? Phase8EReliabilityMode { get; set; }
}

public class ParameterChange
{
    public string Parameter { get; set; } = "";
    public string Operation { get; set; } = "";
    public object? Value { get; set; }
}

// Phase 8B — adaptation effectiveness record.
// Persisted to ADX after the window immediately following an applied adaptation.
// Observability only; never read back to influence adaptation decisions.
public class AdaptationEffectivenessDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "AdaptationEffectiveness";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AdaptationDecisionId { get; set; } = "";
    public int AdaptationDecisionIndex { get; set; }
    public List<string> InterventionFamilies { get; set; } = new();
    public List<ParameterChange> ParameterChanges { get; set; } = new();
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
    public string Outcome { get; set; } = ""; // "improved" | "neutral" | "worsened"
}
