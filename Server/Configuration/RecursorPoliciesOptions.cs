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
