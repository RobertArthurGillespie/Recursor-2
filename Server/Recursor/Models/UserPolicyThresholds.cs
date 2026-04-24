namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

// ── Per-User Policy Thresholds (Phase 8D) ─────────────────────────────────────
// Holds user-specific threshold overrides for the two Phase 8 personalized rules.
// Nullable fields fall back to the global RecursorPoliciesOptions defaults when null.
//
// Lives in the Longitudinal User Layer — distinct from UserBaselineSnapshot
// (behavioral averages) and from RecursorPoliciesOptions (global defaults).
//
// Stored in-memory for the basic slice via IUserThresholdRepository.
// Upgrade path: persist per-user records in ADX or a config store.
// ─────────────────────────────────────────────────────────────────────────────

public class UserPolicyThresholds
{
    public string UserId { get; set; } = "";

    // ── Confusion-block difficulty-increase rule ──────────────────────────────
    // ConfusionDelta must strictly exceed this value to block a difficulty increase.
    // Positive = user more confused than their baseline.
    // Null → falls back to RecursorPoliciesOptions.PersonalizedConfusionBlockDifficultyIncreaseThreshold.
    public double? ConfusionBlockDifficultyIncreaseThreshold { get; set; }

    // ── Personalized hint-fade rule ───────────────────────────────────────────
    // ConfusionDelta must be at or below this value for the hint-fade rule to fire.
    // Negative = user less confused than their baseline.
    // Null → falls back to RecursorPoliciesOptions.PersonalizedHintFadeConfusionDeltaThreshold.
    public double? HintFadeConfusionDeltaThreshold { get; set; }

    // HintDependenceDelta must be at or below this value for the hint-fade rule to fire.
    // Null → falls back to RecursorPoliciesOptions.PersonalizedHintFadeHintDependenceDeltaThreshold.
    public double? HintFadeHintDependenceDeltaThreshold { get; set; }

    // Absolute gate: current confusion must be at or below this for the rule to fire.
    // Null → falls back to RecursorPoliciesOptions.PersonalizedHintFadeMaxCurrentConfusionScore.
    public double? HintFadeMaxCurrentConfusionScore { get; set; }

    // Absolute gate: current goal understanding must be at or above this for the rule to fire.
    // Null → falls back to RecursorPoliciesOptions.PersonalizedHintFadeMinCurrentGoalUnderstanding.
    public double? HintFadeMinCurrentGoalUnderstanding { get; set; }
}
