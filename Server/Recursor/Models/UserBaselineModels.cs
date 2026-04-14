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

// ── Latest Training Row (minimal projection for signal comparison) ────────────
// Holds the columns needed to compute UserRelativeSignals from the most recent
// BehaviorStateTrainingRow for a given user.
// ─────────────────────────────────────────────────────────────────────────────
public class LatestUserTrainingRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int WindowIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // Higher-order behavior scores
    public double ConfusionScore { get; set; }
    public double HintDependenceScore { get; set; }

    // Dimension scores
    public double GoalUnderstanding { get; set; }
    public double AttentionDetection { get; set; }
    public double ProcedureSequencing { get; set; }
    public double SelfCorrection { get; set; }
    public double TaskContinuity { get; set; }
}

// ── User Relative Signals ─────────────────────────────────────────────────────
// Compares a user's most recent behavior-state window against their historical
// baseline. Read-only diagnostic layer — does NOT affect live adaptation.
//
// Current values come from the latest BehaviorStateTrainingRow for the user.
// Baseline values come from the UserBaselineSnapshot lifetime averages.
// ─────────────────────────────────────────────────────────────────────────────
public class UserRelativeSignals
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int WindowIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // ── Current window values ─────────────────────────────────────────────────
    public double CurrentConfusionScore { get; set; }
    public double CurrentHintDependenceScore { get; set; }
    public double CurrentGoalUnderstanding { get; set; }
    public double CurrentAttentionDetection { get; set; }
    public double CurrentProcedureSequencing { get; set; }
    public double CurrentSelfCorrection { get; set; }
    public double CurrentTaskContinuity { get; set; }

    // ── Baseline values (lifetime averages from UserBaselineSnapshot) ─────────
    public double BaselineConfusionScore { get; set; }
    public double BaselineHintDependenceScore { get; set; }
    public double BaselineGoalUnderstanding { get; set; }
    public double BaselineAttentionDetection { get; set; }
    public double BaselineProcedureSequencing { get; set; }
    public double BaselineSelfCorrection { get; set; }
    public double BaselineTaskContinuity { get; set; }

    // ── Deltas (current minus baseline) ──────────────────────────────────────
    // Positive = above baseline, negative = below baseline.
    public double ConfusionDelta { get; set; }
    public double HintDependenceDelta { get; set; }
    public double GoalUnderstandingDelta { get; set; }
    public double AttentionDelta { get; set; }
    public double ProcedureSequencingDelta { get; set; }
    public double SelfCorrectionDelta { get; set; }
    public double TaskContinuityDelta { get; set; }

    // ── Simple relative flags ─────────────────────────────────────────────────
    // Thresholds: ±0.10 to avoid noise from small fluctuations.
    public bool IsAboveBaselineConfusion { get; set; }
    public bool IsAboveBaselineHintDependence { get; set; }
    public bool IsBelowBaselineGoalUnderstanding { get; set; }
    public bool IsBelowBaselineAttention { get; set; }

    // ── Human-readable summary ────────────────────────────────────────────────
    public string Summary { get; set; } = "";
}

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
