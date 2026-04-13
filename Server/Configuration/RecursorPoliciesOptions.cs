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
}
