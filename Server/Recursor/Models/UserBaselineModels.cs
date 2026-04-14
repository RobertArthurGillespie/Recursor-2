namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

// ── User Baseline Snapshot ────────────────────────────────────────────────────
// A compact longitudinal summary for a single user, computed from
// BehaviorStateTrainingRows across all of their sessions.
//
// This is a read-only analytics/context model — it does NOT feed into the live
// adaptation pipeline yet.
//
// Future: compare current-window behavior against UserBaselineSnapshot to
// enable personalized calibration (e.g., adjusted guardrail thresholds,
// difficulty progressions relative to the user's personal history).
// ─────────────────────────────────────────────────────────────────────────────

public class UserBaselineSnapshot
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string UserId { get; set; } = "";

    // ── Coverage ──────────────────────────────────────────────────────────────
    // Total number of behavior-state windows contributing to this snapshot.
    public long SampleWindowCount { get; set; }

    // Earliest and latest window timestamps across all sessions.
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    // ── Lifetime averages — higher-order behavior scores ──────────────────────
    public double AvgConfusionScore { get; set; }
    public double AvgHintDependenceScore { get; set; }
    public double AvgHesitationScore { get; set; }

    // ── Lifetime averages — dimension scores ──────────────────────────────────
    public double AvgGoalUnderstanding { get; set; }
    public double AvgAttentionDetection { get; set; }
    public double AvgProcedureSequencing { get; set; }
    public double AvgSelfCorrection { get; set; }
    public double AvgTaskContinuity { get; set; }

    // ── Lifetime weak-label rates ─────────────────────────────────────────────
    // Fraction of windows that triggered each weak label (0.0–1.0).
    public double StableMasteryRate { get; set; }
    public double ConfusionRate { get; set; }
    public double HintDependenceRate { get; set; }

    // ── Recent-window averages (most recent N windows) ────────────────────────
    // Computed over the N most recent windows by CreatedAtUtc.
    // Useful for detecting current vs. historical behavioral shifts.
    public double RecentAvgConfusionScore { get; set; }
    public double RecentAvgHintDependenceScore { get; set; }
    public double RecentAvgGoalUnderstanding { get; set; }
    public double RecentAvgAttentionDetection { get; set; }
}
