namespace NCATAIBlazorFrontendTest.Server.Configuration;

/// <summary>
/// Typed options for the "Recursor:Policies" section in appsettings.json.
/// Bound via builder.Services.Configure&lt;RecursorPoliciesOptions&gt;(config.GetSection("Recursor:Policies")).
/// </summary>
public class RecursorPoliciesOptions
{
    /// <summary>
    /// When true, the next-window hint-dependence guardrail is active.
    /// Set to false to disable without removing the model file.
    /// </summary>
    public bool EnableNextWindowHintDependenceGuardrail { get; set; } = false;

    /// <summary>
    /// Probability threshold above which the guardrail holds hint support steady
    /// (vetoes hint-fade, hint-remove, hint-reduction).
    /// Raised from 0.35 → 0.45 to reduce over-conservatism in strong/stable-mastery sessions.
    /// </summary>
    public float HintDependenceNextModerateThreshold { get; set; } = 0.45f;

    /// <summary>
    /// Probability threshold above which the guardrail actively preserves hint support
    /// (vetoes reductions, adds scaffold-hints or hint-restore-minimal, adds pace-support).
    /// Raised from 0.60 → 0.70 to reduce over-conservatism in strong/stable-mastery sessions.
    /// </summary>
    public float HintDependenceNextHighThreshold { get; set; } = 0.70f;

    // ── Phase 8: personalized hint-fade rule ──────────────────────────────────

    /// <summary>
    /// When true, enables the Phase 8 personalized hint-fade rule.
    /// When false (default), the rule is entirely inactive and no ADX signal
    /// fetch is performed. Set to true only for controlled Phase 8 experiments.
    /// </summary>
    public bool EnablePersonalizedHintFadeRule { get; set; } = false;

    /// <summary>
    /// ConfusionDelta must be at or below this value for the hint-fade rule to fire.
    /// Negative = user is less confused than their personal baseline.
    /// Default: -0.05 (5 points below baseline).
    /// </summary>
    public double PersonalizedHintFadeConfusionDeltaThreshold { get; set; } = -0.05;

    /// <summary>
    /// HintDependenceDelta must be at or below this value for the hint-fade rule to fire.
    /// Negative = user is less hint-dependent than their personal baseline.
    /// Default: -0.05 (5 points below baseline).
    /// </summary>
    public double PersonalizedHintFadeHintDependenceDeltaThreshold { get; set; } = -0.05;

    /// <summary>
    /// Absolute-performance gate: current confusion score must be at or below this value
    /// for the personalized hint-fade rule to fire. Prevents the rule firing for users who
    /// are improving relative to self but still performing poorly in absolute terms.
    /// Default: 0.30.
    /// </summary>
    public double PersonalizedHintFadeMaxCurrentConfusionScore { get; set; } = 0.30;

    /// <summary>
    /// Absolute-performance gate: current goal-understanding score must be at or above this
    /// value for the personalized hint-fade rule to fire.
    /// Default: 0.60.
    /// </summary>
    public double PersonalizedHintFadeMinCurrentGoalUnderstanding { get; set; } = 0.60;

    // ── Phase 8 personalization expansion: confusion-based difficulty-increase block ──

    /// <summary>
    /// When true, blocks difficulty increases when the user's confusion score
    /// is above their personal baseline by more than the configured threshold.
    /// Default: false.
    /// </summary>
    public bool EnablePersonalizedConfusionBlockDifficultyRule { get; set; } = false;

    /// <summary>
    /// ConfusionDelta must exceed this value for the difficulty-increase block to fire.
    /// Positive = user is more confused than their personal baseline.
    /// Default: 0.05 (5 points above baseline).
    /// </summary>
    public double PersonalizedConfusionBlockDifficultyIncreaseThreshold { get; set; } = 0.05;

    // ── Phase 8 personalization expansion: difficulty acceleration rule ────────

    /// <summary>
    /// When true, allows a slightly stronger difficulty increase when the learner
    /// is performing significantly above their personal baseline.
    /// Only fires when a difficulty-increase is already proposed; never creates one.
    /// Default: false.
    /// </summary>
    public bool EnablePersonalizedDifficultyAccelerationRule { get; set; } = false;

    /// <summary>
    /// ConfusionDelta must be at or below this value for the acceleration rule to fire.
    /// Negative = user is less confused than their personal baseline.
    /// Default: -0.08 (8 points below baseline).
    /// </summary>
    public double PersonalizedDifficultyAccelerationConfusionDeltaThreshold { get; set; } = -0.08;

    /// <summary>
    /// HintDependenceDelta must be at or below this value for the acceleration rule to fire.
    /// Negative = user is less hint-dependent than their personal baseline.
    /// Default: -0.08 (8 points below baseline).
    /// </summary>
    public double PersonalizedDifficultyAccelerationHintDependenceDeltaThreshold { get; set; } = -0.08;

    /// <summary>
    /// Absolute gate: current confusion score must be at or below this value.
    /// Prevents acceleration from firing when the learner is improving relative
    /// to baseline but still performing poorly in absolute terms.
    /// Default: 0.25.
    /// </summary>
    public double PersonalizedDifficultyAccelerationMaxCurrentConfusionScore { get; set; } = 0.25;

    /// <summary>
    /// Absolute gate: current goal-understanding score must be at or above this value.
    /// Default: 0.70.
    /// </summary>
    public double PersonalizedDifficultyAccelerationMinCurrentGoalUnderstanding { get; set; } = 0.70;

    /// <summary>
    /// Difficulty delta applied when the acceleration rule fires.
    /// Should exceed the base difficulty-increase delta (0.05) but remain conservative.
    /// Default: 0.08.
    /// </summary>
    public double PersonalizedDifficultyAccelerationDelta { get; set; } = 0.08;

    // ── Phase 8A: ML-assisted gated guardrail modifier ───────────────────────

    /// <summary>
    /// When true, the Phase 8A post-policy guardrail modifier is active.
    /// The modifier reviews each proposed adaptation and removes parameter changes
    /// that violate the four Phase 8A safety rules (conflicted-state veto,
    /// high-confusion protection, strong-mastery permission, hint-dependence protection).
    /// It never creates new adaptations.  Default: false.
    /// </summary>
    public bool EnablePhase8AGuardrailModifier { get; set; } = false;

    // ── Baseline hint-progression streak thresholds ───────────────────────────

    /// <summary>
    /// Unused — superseded by HintFadeRequiresSupportFadeEligibleWindows.
    /// Retained to avoid breaking existing appsettings.json files that set this value.
    /// </summary>
    public int HintFadeRequiresStableMasteryWindows { get; set; } = 2;

    /// <summary>
    /// Minimum consecutive stable-mastery windows required before the baseline
    /// progression logic may remove hints entirely (minimal → off, hint-remove).
    /// Only stable_mastery_pattern contributes; improving_pattern does not.
    /// Default: 3.
    /// </summary>
    public int HintRemoveRequiresStableMasteryWindows { get; set; } = 2;

    // ── Support-fade-eligible streak thresholds ───────────────────────────────

    /// <summary>
    /// Minimum consecutive support-fade-eligible windows (stable_mastery or improving)
    /// required before the baseline progression logic may reduce hints guided → minimal.
    /// Default: 2.
    /// </summary>
    public int HintFadeRequiresSupportFadeEligibleWindows { get; set; } = 2;

    /// <summary>
    /// Unused — the minimal → off gate now uses HintRemoveRequiresStableMasteryWindows.
    /// Retained to avoid breaking existing appsettings.json files that set this value.
    /// </summary>
    public int HintRemoveRequiresSupportFadeEligibleWindows { get; set; } = 3;

    // ── Moderate guardrail recovery override ─────────────────────────────────

    /// <summary>
    /// When true, a MODERATE next-window guardrail can be downgraded when the state
    /// machine already computed a hint reduction AND sustained recovery evidence is
    /// present (consecutive support-fade-eligible windows at or above the threshold
    /// below, plus an active mastery / improving / recovery hypothesis).
    /// HIGH guardrail is not affected by this override.
    /// Default: true.
    /// </summary>
    public bool EnableModerateGuardrailRecoveryOverride { get; set; } = true;

    /// <summary>
    /// Minimum consecutive support-fade-eligible windows (stable_mastery or improving)
    /// required before a MODERATE guardrail can be overridden by recovery evidence.
    /// Should be >= HintFadeRequiresSupportFadeEligibleWindows (2) to ensure the
    /// evidence comes from sustained performance rather than a single good window.
    /// Default: 3.
    /// </summary>
    public int ModerateGuardrailRecoveryOverrideRequiresEligibleWindows { get; set; } = 3;
}
