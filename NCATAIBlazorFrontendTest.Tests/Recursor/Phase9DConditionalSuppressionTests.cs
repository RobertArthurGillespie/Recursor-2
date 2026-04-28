using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 9D tests for risk-only conditional suppression.
///
/// Phase 9D allows conditional reliability rules to suppress intervention families when:
///   - Mode = "active"
///   - EnableConditionalSuppression = true
///   - The matched conditional tier is "risky"
///   - The current learner state matches the configured condition key
///
/// Only "risky" conditional tiers suppress. "promising" and "neutral" are always observational.
/// Global Phase 8E suppression (FamilyReliabilityTiers) is not changed.
/// Global and conditional suppression compose without double-removal.
///
/// Coverage:
///   - Shadow mode: risky conditional match is noted as candidate but nothing is removed
///   - Active mode + EnableConditionalSuppression=false: risky conditional match is a candidate only
///   - Active mode + EnableConditionalSuppression=true: risky conditional match removes family and owned params
///   - "promising" conditional tier never removes anything (active + enabled)
///   - "neutral" conditional tier never removes anything (active + enabled)
///   - Global risky + conditional risky on same family: family removed once, no double-removal
///   - No condition match means no suppression
///   - Disabled mode skips all reliability behavior
/// </summary>
public class Phase9DConditionalSuppressionTests
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
            SessionId            = "session-9d",
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

    // ── Shadow mode: risky conditional match is noted but never removes ───────

    [Fact]
    public void Apply_ShadowMode_ConditionalRiskyMatch_LogsCandidateButDoesNotRemove()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            EnableConditionalSuppression = false,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Shadow mode: no removal.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");

        // Candidate noted for audit.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-risky-suppression-candidates: [pace-increase]", result.Phase8EReliabilityNotes);

        // Nothing was actually suppressed.
        Assert.DoesNotContain("conditional-suppressed-families", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + EnableConditionalSuppression=false: no conditional removal ──

    [Fact]
    public void Apply_ActiveMode_EnableConditionalSuppressionFalse_DoesNotRemoveConditionalRiskyFamily()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = false,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // EnableConditionalSuppression=false → no removal.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");

        // Candidate is recorded in notes so operator can see what would be suppressed if enabled.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-risky-suppression-candidates: [pace-increase]", result.Phase8EReliabilityNotes);
        Assert.DoesNotContain("conditional-suppressed-families", result.Phase8EReliabilityNotes);
    }

    // ── Active mode + EnableConditionalSuppression=true: risky conditional removes family + params ──

    [Fact]
    public void Apply_ActiveMode_EnableConditionalSuppressionTrue_RemovesConditionalRiskyFamily()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // pace-increase conditionally suppressed.
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "timePressure" && p.Operation == "increase");

        // Audit notes record the suppression.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-suppressed-families: [pace-increase]", result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-suppressed-params: [timePressure/increase]", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ActiveMode_ConditionalSuppression_MultipleConditionKeysBothMatchRisky()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky",
                    ["PredictedConfusionRisk=high"]              = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        // Suppressed once despite two matching condition keys.
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "timePressure" && p.Operation == "increase");

        // Both keys appear in conditional-matches.
        Assert.Contains("GuardrailOverallBehaviorState=struggling", result.Phase8EReliabilityNotes);
        Assert.Contains("PredictedConfusionRisk=high", result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-suppressed-families: [pace-increase]", result.Phase8EReliabilityNotes);
    }

    // ── "promising" and "neutral" tiers never suppress ────────────────────────

    [Fact]
    public void Apply_ActiveMode_ConditionalPromisingTier_NeverRemovesFamily()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=stable"] = "promising"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("stable"), null);

        // "promising" tier never suppresses.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");

        // No suppression notes.
        if (result.Phase8EReliabilityNotes is not null)
        {
            Assert.DoesNotContain("conditional-suppressed-families", result.Phase8EReliabilityNotes);
            Assert.DoesNotContain("conditional-risky-suppression-candidates", result.Phase8EReliabilityNotes);
        }
    }

    [Fact]
    public void Apply_ActiveMode_ConditionalNeutralTier_NeverRemovesFamily()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=recovering"] = "neutral"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("recovering"), null);

        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");

        if (result.Phase8EReliabilityNotes is not null)
            Assert.DoesNotContain("conditional-suppressed-families", result.Phase8EReliabilityNotes);
    }

    // ── Global risky + conditional risky on same family: single removal ───────

    [Fact]
    public void Apply_ActiveMode_GlobalRiskyAndConditionalRisky_SameFamily_DoesNotDoubleRemove()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Removed exactly once (by global suppression).
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);
        var timePressureIncreaseCount = result.ParameterChanges
            .Count(p => p.Parameter == "timePressure" && p.Operation == "increase");
        Assert.Equal(0, timePressureIncreaseCount);

        // Globally suppressed families note (Phase 8E behavior preserved).
        Assert.Contains("risky-families-detected: [pace-increase]", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);

        // pace-increase was globally risky so it is NOT in conditional candidates or suppressed families.
        Assert.DoesNotContain("conditional-suppressed-families: [pace-increase]", result.Phase8EReliabilityNotes);

        // Conditional match IS noted for audit (even though the family is globally handled).
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
    }

    // ── No condition match means no conditional suppression ───────────────────

    [Fact]
    public void Apply_ActiveMode_NoConditionMatch_NoConditionalSuppression()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            EnableConditionalSuppression = true,
            ConditionalFamilyReliabilityTiers = new()
            {
                // Configured for "stable" but learner state is "struggling".
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=stable"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // No match → no removal.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");
        Assert.Null(result.Phase8EReliabilityNotes);
    }

    // ── Disabled mode skips all reliability behavior ──────────────────────────

    [Fact]
    public void Apply_DisabledMode_SkipsAllReliabilityBehavior()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "disabled",
            EnableConditionalSuppression = true,
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // Disabled → no changes at all.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges, p => p.Parameter == "timePressure" && p.Operation == "increase");
        Assert.Null(result.Phase8EReliabilityNotes);
        Assert.Null(result.Phase8EReliabilityMode);
    }
}
