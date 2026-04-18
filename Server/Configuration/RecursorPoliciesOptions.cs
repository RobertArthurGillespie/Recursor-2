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
    /// Default matches offline evaluation recommendation: 0.10.
    /// </summary>
    public float HintDependenceNextModerateThreshold { get; set; } = 0.10f;

    /// <summary>
    /// Probability threshold above which the guardrail actively preserves hint support
    /// (vetoes reductions, adds scaffold-hints or hint-restore-minimal, adds pace-support).
    /// Default matches offline evaluation recommendation: 0.20.
    /// </summary>
    public float HintDependenceNextHighThreshold { get; set; } = 0.20f;

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
}
