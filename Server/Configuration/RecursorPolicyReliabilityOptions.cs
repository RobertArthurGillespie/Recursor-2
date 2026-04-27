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
}
