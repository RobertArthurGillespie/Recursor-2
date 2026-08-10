namespace NCATAIBlazorFrontendTest.Shared;

/// <summary>
/// Why a raw behavior-state label literal did or did not resolve to a canonical class.
/// </summary>
public enum BehaviorStateLabelStatus
{
    /// <summary>Resolved to one of the five canonical classes.</summary>
    Valid,

    /// <summary>Null/blank — the guardrail never stamped a state for this window.</summary>
    Missing,

    /// <summary>Explicit "unknown" — the guardrail ran but had no hypotheses to report.</summary>
    NoSignal,

    /// <summary>A non-blank literal that does not match any canonical label or known alias.</summary>
    Invalid,
}

/// <summary>
/// Normalized result of classifying a raw behavior-state label literal.
/// </summary>
public readonly record struct BehaviorStateLabelResult(
    string? CanonicalLabel,
    BehaviorStateLabelStatus Status,
    string Reason)
{
    public bool IsTrainable => Status == BehaviorStateLabelStatus.Valid;
}

/// <summary>
/// Single source of truth for behavior-state label literals: canonical class names,
/// casing/whitespace normalization, legacy alias mapping, and classification of why a
/// label is not usable for training. Reused by target generation, training, inference
/// evaluation, and dashboard rendering (Server AND Client) so no consumer hardcodes its own
/// literal list or its own ad-hoc "!= unknown" check.
///
/// Stage 5 (corrective pass): lives in the Shared project — not Server — specifically so the
/// Blazor Client's Razor components can reference the exact same normalization rules the server
/// uses for target persistence, training exclusion, and dashboard correctness, instead of
/// re-implementing (and inevitably drifting from) their own comparison logic. Client cannot
/// reference Server (only Server -> Client and both -> Shared), so this is the only project
/// both sides can share.
///
/// The canonical set matches the runtime output of MultiSignalGuardrailService.Evaluate
/// (see Server/Recursor/Models/GuardrailModels.cs, MultiSignalGuardrailSummary.OverallBehaviorState)
/// — this policy does not invent a different state set. "unknown" is explicitly not folded
/// into "stable": a window where the guardrail found nothing to say is a distinct outcome
/// from a window judged calm/on-track, and neither implies missing data. See
/// docs/recursor-phase10e-behavior-state-label-policy.md for the full rationale.
/// </summary>
public static class BehaviorStateLabelPolicy
{
    public const string Struggling = "struggling";
    public const string Recovering = "recovering";
    public const string Stable = "stable";
    public const string Mastering = "mastering";
    public const string Conflicted = "conflicted";

    private const string UnknownLiteral = "unknown";

    public static readonly IReadOnlyList<string> CanonicalLabels =
    [
        Struggling,
        Recovering,
        Stable,
        Mastering,
        Conflicted,
    ];

    /// <summary>
    /// Legacy/alternate literals that map onto a canonical label. Nothing in the current
    /// runtime produces these — MultiSignalGuardrailService only ever emits the five
    /// canonical labels or "unknown" — but the roadmap's candidate list used "steady"
    /// where the runtime uses "stable", so it is mapped defensively rather than left to
    /// become an Invalid row if it ever appears in historical data or a future producer.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["steady"] = Stable,
        };

    /// <summary>
    /// Normalizes a raw label literal (as stored in TargetBehaviorState or
    /// MultiSignalGuardrailSummary.OverallBehaviorState) and classifies it.
    /// Never fabricates a canonical label for a row that lacks evidence — Missing and
    /// NoSignal both resolve to a null CanonicalLabel.
    /// </summary>
    public static BehaviorStateLabelResult Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BehaviorStateLabelResult(
                null,
                BehaviorStateLabelStatus.Missing,
                "value was null/blank — the guardrail never stamped a state for this window " +
                "(pipeline exception was swallowed, or the window has not been processed yet)");
        }

        var trimmed = raw.Trim();

        if (string.Equals(trimmed, UnknownLiteral, StringComparison.OrdinalIgnoreCase))
        {
            return new BehaviorStateLabelResult(
                null,
                BehaviorStateLabelStatus.NoSignal,
                "guardrail evaluated the window but produced zero hypotheses — a legitimate " +
                "'nothing notable happened' outcome, not missing data and not fabricated as stable");
        }

        if (LegacyAliases.TryGetValue(trimmed, out var aliased))
        {
            return new BehaviorStateLabelResult(
                aliased,
                BehaviorStateLabelStatus.Valid,
                $"legacy alias '{trimmed}' normalized to canonical '{aliased}'");
        }

        foreach (var canonical in CanonicalLabels)
        {
            if (string.Equals(canonical, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return new BehaviorStateLabelResult(canonical, BehaviorStateLabelStatus.Valid, "canonical label");
            }
        }

        return new BehaviorStateLabelResult(
            null,
            BehaviorStateLabelStatus.Invalid,
            $"unrecognized label literal '{trimmed}' — not a canonical label, alias, or 'unknown'");
    }

    public static bool IsCanonical(string label) =>
        CanonicalLabels.Contains(label, StringComparer.Ordinal);
}
