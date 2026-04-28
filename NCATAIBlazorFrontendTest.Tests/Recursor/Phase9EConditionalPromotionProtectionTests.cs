using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 9E tests for controlled conditional promotion protection.
///
/// Phase 9E allows conditional "promising" tiers to protect globally-risky intervention families
/// from global reliability suppression. It does NOT create new adaptations, add new families, or
/// intensify parameters. Risky always wins over promising.
///
/// Activation gates:
///   - Mode = "active"
///   - EnableConditionalPromotionProtection = true
///   - The matched conditional tier is "promising"
///   - The same family does NOT also have a matching conditional "risky" tier
///
/// Coverage:
///   - Shadow mode: globally-risky family with conditional promising match is observed but not protected
///   - Active mode + protection disabled: globally-risky family still suppressed despite promising match
///   - Active mode + protection enabled + condition matches: family preserved (protected from global suppression)
///   - Active mode + protection enabled + no condition match: global risky suppression still applies
///   - Active mode + both promising and risky conditional match: risky wins, family is suppressed
///   - Conditional promising does not add new families to the adaptation
///   - Conditional promising does not protect a family unrelated to the globally-risky family
///   - Disabled mode skips all reliability behavior
///   - Multiple families: one protected, one not (no conditional promising)
/// </summary>
public class Phase9EConditionalPromotionProtectionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PolicyReliabilityWeightingService Build(RecursorPolicyReliabilityOptions options)
        => new(Options.Create(options), NullLogger<PolicyReliabilityWeightingService>.Instance);

    private static AdaptationDecisionDocument MakeDecision(params string[] families)
    {
        var paramChanges = families
            .Select(f => new ParameterChange { Parameter = FamilyToParam(f), Operation = FamilyToOp(f) })
            .Where(p => p.Parameter != "")
            .ToList();

        return new AdaptationDecisionDocument
        {
            SessionId            = "session-9e",
            InterventionFamilies = families.ToList(),
            ParameterChanges     = paramChanges
        };
    }

    private static string FamilyToParam(string family) => family switch
    {
        "difficulty-increase"  => "difficulty",
        "difficulty-reduction" => "difficulty",
        "pace-increase"        => "timePressure",
        "pace-support"         => "timePressure",
        _                      => ""
    };

    private static string FamilyToOp(string family) => family switch
    {
        "difficulty-increase"  => "increase",
        "difficulty-reduction" => "decrease",
        "pace-increase"        => "increase",
        "pace-support"         => "decrease",
        _                      => ""
    };

    private static MultiSignalGuardrailSummary MakeSummary(string overallState)
        => new() { OverallBehaviorState = overallState };

    private static BehaviorStatePrediction MakePrediction(string confusionRisk)
        => new() { PredictedConfusionRisk = confusionRisk };

    // ── Shadow mode: promising match is observed but does not protect ──────────

    [Fact]
    public void Apply_ShadowMode_PromisingMatchOnGlobalRiskyFamily_ObservedButNotProtected()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            EnableConditionalPromotionProtection = true,  // flag is true but mode is shadow — no effect
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Shadow mode never removes families — family still present.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // Observation note present.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-observed: [difficulty-reduction]", result.Phase8EReliabilityNotes);

        // Not protected (shadow mode).
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + protection disabled: globally risky family still suppressed ──

    [Fact]
    public void Apply_ActiveMode_ProtectionDisabled_GlobalRiskyFamilyStillSuppressed()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = false,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Protection is off — family is still suppressed by global risky tier.
        Assert.DoesNotContain("difficulty-reduction", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // Observed but not acted upon.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-observed: [difficulty-reduction]", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + protection enabled + condition matches: family protected ──

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_ConditionMatches_FamilyPreserved()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Globally risky but conditionally promising and protection enabled — family preserved.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // Protection notes present.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-protection-candidates: [difficulty-reduction]",
            result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-protected-families: [difficulty-reduction]",
            result.Phase8EReliabilityNotes);
        Assert.Contains("risky-families-detected: [difficulty-reduction]", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);

        // No params suppressed (family was protected).
        Assert.DoesNotContain("suppressed-params:", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("risky-overrides-promising", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + protection enabled + no condition match: global suppression applies ──

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_NoConditionMatch_FamilySuppressed()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    // Configured for "mastering" but learner state is "struggling" — no match.
                    ["GuardrailOverallBehaviorState=mastering"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // No conditional match — no protection, global risky suppression applies.
        Assert.DoesNotContain("difficulty-reduction", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // No protection notes (no match occurred).
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protection-candidates", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-observed", result.Phase8EReliabilityNotes);
        Assert.Contains("risky-families-detected: [difficulty-reduction]", result.Phase8EReliabilityNotes);
        Assert.Contains("suppressed-params: [difficulty/decrease]", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + both promising AND risky conditional match: risky wins ──

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_BothPromisingAndRiskyMatch_RiskyWins()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising",
                    ["PredictedConfusionRisk=high"]              = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        // Both conditions active: struggling (promising) and confusion=high (risky).
        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        // Risky wins — family is suppressed.
        Assert.DoesNotContain("difficulty-reduction", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // Risky-overrides-promising note present.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-protection-candidates: [difficulty-reduction]",
            result.Phase8EReliabilityNotes);
        Assert.Contains("risky-overrides-promising: [difficulty-reduction]", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);
    }

    // ── Conditional promising does not add new families to the adaptation ─────

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_PromisingFamilyNotInAdaptation_NoEffect()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                // Configured for a family that is NOT in this adaptation's InterventionFamilies.
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // pace-increase suppressed (globally risky, no protection for it).
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);

        // difficulty-reduction was NOT added (it wasn't in the adaptation).
        Assert.DoesNotContain("difficulty-reduction", result.InterventionFamilies);

        // No protection notes for the promising family not in adaptation.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protection-candidates", result.Phase8EReliabilityNotes);
    }

    // ── Conditional promising does not protect an unrelated family ────────────

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_PromisingOnDifferentFamily_DoesNotProtectGlobalRisky()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                // Promising applies to pace-support, not to difficulty-reduction.
                ["pace-support"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction", "pace-support");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // difficulty-reduction is globally risky — it is suppressed regardless of pace-support's promising match.
        Assert.DoesNotContain("difficulty-reduction", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // pace-support is not globally risky — it is NOT suppressed.
        Assert.Contains("pace-support", result.InterventionFamilies);

        // No protection candidates (difficulty-reduction has no promising match; pace-support is not globally risky).
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protection-candidates", result.Phase8EReliabilityNotes);
    }

    // ── Disabled mode skips all reliability behavior ──────────────────────────

    [Fact]
    public void Apply_DisabledMode_SkipsAllReliabilityBehavior()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "disabled",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new() { ["difficulty-reduction"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Disabled — no changes at all.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "difficulty" && p.Operation == "decrease");
        Assert.Null(result.Phase8EReliabilityNotes);
        Assert.Null(result.Phase8EReliabilityMode);
    }

    // ── Multiple families: one protected, one not ─────────────────────────────

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_PartialProtection_OneProtectedOneNotSuppressed()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            FamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = "risky",
                ["pace-increase"]        = "risky"
            },
            ConditionalFamilyReliabilityTiers = new()
            {
                // difficulty-reduction is conditionally promising — protected in struggling state.
                // pace-increase has no conditional promising — still suppressed.
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction", "pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // difficulty-reduction is protected — it survives.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // pace-increase is globally risky with no conditional promising — suppressed.
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "timePressure" && p.Operation == "increase");

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-promising-protected-families: [difficulty-reduction]",
            result.Phase8EReliabilityNotes);
        // pace-increase was removed (not protected).
        Assert.Contains("suppressed-params: [timePressure/increase]", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);
    }

    // ── Protection does not apply to non-globally-risky families ─────────────

    [Fact]
    public void Apply_ActiveMode_ProtectionEnabled_FamilyNotGloballyRisky_ProtectionDoesNotApply()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalPromotionProtection = true,
            // difficulty-reduction is NOT globally risky.
            FamilyReliabilityTiers = new(),
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Family was never going to be suppressed (not globally risky) — still present.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "difficulty" && p.Operation == "decrease");

        // Conditional match note appears, but no protection candidates (family not globally risky).
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protection-candidates", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-promising-protected-families", result.Phase8EReliabilityNotes);
    }
}
