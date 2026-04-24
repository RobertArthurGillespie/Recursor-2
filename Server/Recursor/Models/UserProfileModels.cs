namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

// ── User Behavior Profile (Phase 6A) ─────────────────────────────────────────
// Persistent user-level record that combines:
//   1. Behavioral baseline aggregates (lifetime averages across sessions)
//   2. Per-user policy threshold overrides (nullable — null falls back to globals)
//
// This is the Phase 6A foundation for the Longitudinal User Layer.
// For the basic slice it lives in InMemoryUserProfileRepository.
//
// ADX migration path: implement IUserProfileRepository against a UserProfiles
// ADX table using the same ICslQueryProvider / IKustoQueuedIngestClient pattern
// used by all other Recursor ADX services, then swap the DI registration in
// Program.cs. No changes needed to callers.
// ─────────────────────────────────────────────────────────────────────────────

public class UserBehaviorProfile
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string UserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string LastSessionId { get; set; } = "";

    // ── Session / window coverage ─────────────────────────────────────────────
    public int TotalSessions { get; set; }
    public int TotalWindows { get; set; }

    // ── Lifetime behavioral baseline (averaged across all contributing windows) ─
    public double AvgConfusionScore { get; set; }
    public double AvgHintDependenceScore { get; set; }
    public double AvgHesitationScore { get; set; }
    public double AvgGoalUnderstanding { get; set; }
    public double AvgAttentionDetection { get; set; }
    public double AvgProcedureSequencing { get; set; }
    public double AvgSelfCorrection { get; set; }
    public double AvgTaskContinuity { get; set; }
    public double StableMasteryRate { get; set; }
    public double ConfusionRate { get; set; }
    public double HintDependenceRate { get; set; }

    // ── Per-user policy threshold overrides ───────────────────────────────────
    // All fields are nullable. Null means "use the global RecursorPoliciesOptions
    // default." Only set fields that have been tuned for this user.

    // Confusion-block difficulty-increase rule
    public double? ConfusionBlockDifficultyIncreaseThreshold { get; set; }

    // Personalized hint-fade rule
    public double? HintFadeConfusionDeltaThreshold { get; set; }
    public double? HintFadeHintDependenceDeltaThreshold { get; set; }
    public double? HintFadeMaxCurrentConfusionScore { get; set; }
    public double? HintFadeMinCurrentGoalUnderstanding { get; set; }

    // Personalized difficulty-acceleration rule
    public double? DifficultyAccelerationConfusionDeltaThreshold { get; set; }
    public double? DifficultyAccelerationHintDependenceDeltaThreshold { get; set; }
    public double? DifficultyAccelerationMaxCurrentConfusionScore { get; set; }
    public double? DifficultyAccelerationMinCurrentGoalUnderstanding { get; set; }
    public double? DifficultyAccelerationDelta { get; set; }
}
