namespace NCATAIBlazorFrontendTest.Server.Configuration;

public class RecursorPolicyReliabilityOptions
{
    // "disabled" — no effect (default)
    // "shadow"   — log what would be suppressed; do not modify the adaptation
    // "active"   — remove risky intervention families and their parameter changes
    public string Mode { get; set; } = "disabled";

    // Maps intervention family name → reliability tier from Phase 8D Q14 output.
    // Valid tier values: "promising" | "neutral" | "risky" | "insufficient-data"
    // Only "risky" entries are acted upon; others are ignored but recorded.
    public Dictionary<string, string> FamilyReliabilityTiers { get; set; } = new();

    // Phase 9D: Allow conditional risky tiers to suppress families in active mode.
    // When true AND Mode == "active", families whose matched conditional tier is "risky" are removed.
    // Default is false. Set to true only after shadow-mode validation of conditional tiers.
    // Has no effect when Mode == "shadow" or "disabled".
    public bool EnableConditionalSuppression { get; set; } = false;

    // Phase 9E: Protect globally-risky families from suppression when a conditional "promising" tier matches.
    // When true AND Mode == "active", a globally-risky family whose matched conditional tier is "promising"
    // (and NOT also "risky") is preserved rather than removed by global reliability suppression.
    // "Risky" always wins: if both a promising and risky conditional tier match the same family, the family
    // is suppressed. Protection does not apply to Phase 8A guardrail vetoes, conditional risky suppression,
    // or hard safety rules. Protection never creates new families or parameter changes.
    // Default is false. Has no effect when Mode != "active".
    public bool EnableConditionalPromotionProtection { get; set; } = false;

    // Phase 9C: State-conditional reliability tiers.
    // Maps family → condition-key → tier, where condition keys are:
    //   "GuardrailOverallBehaviorState=<value>"   (e.g. "struggling", "conflicted", "stable")
    //   "PredictedConfusionRisk=<value>"          (e.g. "none", "low", "moderate", "high")
    //   "GuardrailHintDependenceRiskLevel=<value>" (e.g. "none", "low", "moderate", "high")
    // Note: keys use "=" not ":" because ":" is a .NET config section separator.
    // In shadow mode, matching conditions are recorded in Phase8EReliabilityNotes and logs only.
    // In active mode with EnableConditionalSuppression=true, risky conditional matches suppress families.
    public Dictionary<string, Dictionary<string, string>> ConditionalFamilyReliabilityTiers { get; set; } = new();
}
