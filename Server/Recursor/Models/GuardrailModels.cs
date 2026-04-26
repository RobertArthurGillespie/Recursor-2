namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class MultiSignalGuardrailSummary
{
    // Synthesized learner state across rule, ML, and user-relative signals.
    // "struggling" | "recovering" | "stable" | "mastering" | "conflicted" | "unknown"
    public string OverallBehaviorState { get; init; } = "unknown";

    // True when the rule-engine confusion label and ML confusion prediction agree.
    public bool ConfusionSignalAgreement { get; init; }

    // True when the rule-engine stable-mastery label and ML mastery prediction agree.
    public bool StableMasterySignalAgreement { get; init; }

    // Synthesized hint-dependence risk across current-window and next-window probabilities.
    // "none" | "low" | "moderate" | "high"
    public string HintDependenceRiskLevel { get; init; } = "none";

    // Number of cross-signal disagreements or anomalies detected this window.
    public int GuardrailConflictCount { get; init; }

    // Human-readable descriptions of each detected conflict or anomaly.
    public List<string> GuardrailWarnings { get; init; } = [];
}
