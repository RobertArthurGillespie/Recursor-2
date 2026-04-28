using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 9C tests for state-conditional policy reliability evaluation.
///
/// Coverage:
///   - Conditional tier notes are populated in shadow mode when a condition key matches
///   - Multiple condition keys can match for a single family
///   - Multiple families can each produce conditional matches
///   - Conditional evaluation is skipped when ConditionalFamilyReliabilityTiers is empty
///   - Conditional evaluation is skipped when no condition keys can be built (both inputs null)
///   - Conditional notes are absent when no configured keys match the active learner state
///   - Global risky suppression (Phase 8E shadow) is unaffected by conditional config
///   - Global active-mode suppression still removes families and params correctly
///   - Conditional rules never remove intervention families or parameter changes in shadow mode
///   - Notes from global risky detection and conditional matches are both present
///   - mode="disabled" skips everything including conditional evaluation
///
/// Note: condition keys use "=" as separator (not ":") because ":" is a .NET config section
/// separator and would not survive appsettings.json binding as a flat dictionary key.
/// </summary>
public class Phase9CConditionalReliabilityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PolicyReliabilityWeightingService Build(RecursorPolicyReliabilityOptions options)
        => new(Options.Create(options), NullLogger<PolicyReliabilityWeightingService>.Instance);

    private static AdaptationDecisionDocument MakeDecision(params string[] families)
        => new()
        {
            SessionId            = "session-test",
            InterventionFamilies = families.ToList(),
            ParameterChanges     = families
                .Select(f => new ParameterChange { Parameter = FamilyToParam(f), Operation = FamilyToOp(f) })
                .Where(p => p.Parameter != "")
                .ToList()
        };

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

    private static MultiSignalGuardrailSummary MakeSummary(
        string overallState = "struggling",
        string hintRisk = "none")
        => new()
        {
            OverallBehaviorState    = overallState,
            HintDependenceRiskLevel = hintRisk
        };

    private static BehaviorStatePrediction MakePrediction(string confusionRisk = "high")
        => new() { PredictedConfusionRisk = confusionRisk };

    // ── Conditional match — shadow mode ──────────────────────────────────────

    [Fact]
    public void Apply_ShadowMode_PopulatesConditionalNotes_WhenConditionKeyMatches()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
        Assert.Contains("difficulty-reduction=promising[GuardrailOverallBehaviorState=struggling]",
            result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ShadowMode_ConditionalNotes_AbsentWhenNoKeyMatches()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=mastering"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // "mastering" was configured but "struggling" is the active state — no match.
        Assert.Null(result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ShadowMode_MultipleConditionKeys_AllMatchingKeysRecorded()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["pace-increase"] = new()
                {
                    ["PredictedConfusionRisk=high"]              = "risky",
                    ["GuardrailOverallBehaviorState=struggling"] = "risky"
                }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("PredictedConfusionRisk=high", result.Phase8EReliabilityNotes);
        Assert.Contains("GuardrailOverallBehaviorState=struggling", result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ShadowMode_MultipleFamilies_EachCanMatchSeparately()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" },
                ["pace-increase"]        = new() { ["PredictedConfusionRisk=high"] = "risky" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction", "pace-increase");

        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("difficulty-reduction=promising", result.Phase8EReliabilityNotes);
        Assert.Contains("pace-increase=risky", result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ShadowMode_HintDependenceRiskLevel_UsedAsConditionKey()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["hint-fade"] = new() { ["GuardrailHintDependenceRiskLevel=high"] = "risky" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("hint-fade");

        var result = svc.Apply(decision, MakeSummary(hintRisk: "high"), null);

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("hint-fade=risky[GuardrailHintDependenceRiskLevel=high]",
            result.Phase8EReliabilityNotes);
    }

    // ── Conditional rules never suppress families or params in shadow mode ────

    [Fact]
    public void Apply_ShadowMode_ConditionalRiskyMatch_DoesNotRemoveFamilyOrParams()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=conflicted"] = "risky" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");
        int originalFamilies = decision.InterventionFamilies.Count;
        int originalParams   = decision.ParameterChanges.Count;

        var result = svc.Apply(decision, MakeSummary("conflicted"), null);

        Assert.Equal(originalFamilies, result.InterventionFamilies.Count);
        Assert.Equal(originalParams, result.ParameterChanges.Count);
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
    }

    // ── Conditional skipped when no learner state available ───────────────────

    [Fact]
    public void Apply_ShadowMode_NoConditionalNotes_WhenBothInputsNull()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, null, null);

        // No condition keys can be built from null inputs — no conditional notes.
        Assert.Null(result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ShadowMode_NoConditionalNotes_WhenConditionalConfigEmpty()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        Assert.Null(result.Phase8EReliabilityNotes);
    }

    // ── Global + conditional notes coexist ───────────────────────────────────

    [Fact]
    public void Apply_ShadowMode_GlobalRiskyAndConditional_BothAppearInNotes()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase", "difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("risky-families-detected", result.Phase8EReliabilityNotes);
        Assert.Contains("mode=shadow; no changes applied", result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
        Assert.Contains("difficulty-reduction=promising", result.Phase8EReliabilityNotes);

        // Global shadow — no families removed.
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
    }

    // ── Global active mode — Phase 8E behavior preserved exactly ─────────────

    [Fact]
    public void Apply_ActiveMode_GlobalRiskyFamily_IsRemovedRegardlessOfConditionalConfig()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase", "difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        // pace-increase was globally risky → removed.
        Assert.DoesNotContain("pace-increase", result.InterventionFamilies);
        // difficulty-reduction had no global risky tier → stays.
        Assert.Contains("difficulty-reduction", result.InterventionFamilies);
        // timePressure/increase owned by pace-increase → removed.
        Assert.DoesNotContain(result.ParameterChanges,
            p => p.Parameter == "timePressure" && p.Operation == "increase");
        // Active mode notes present.
        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("mode=active", result.Phase8EReliabilityNotes);
    }

    [Fact]
    public void Apply_ActiveMode_ConditionalNotes_AppendedToActiveNotes()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "active",
            FamilyReliabilityTiers = new() { ["pace-increase"] = "risky" },
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("pace-increase", "difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), null);

        Assert.NotNull(result.Phase8EReliabilityNotes);
        Assert.Contains("conditional-matches", result.Phase8EReliabilityNotes);
        Assert.Contains("difficulty-reduction=promising", result.Phase8EReliabilityNotes);
    }

    // ── mode="disabled" skips everything ──────────────────────────────────────

    [Fact]
    public void Apply_DisabledMode_SkipsConditionalEvaluation()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "disabled",
            ConditionalFamilyReliabilityTiers = new()
            {
                ["difficulty-reduction"] = new() { ["GuardrailOverallBehaviorState=struggling"] = "promising" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, MakeSummary("struggling"), MakePrediction("high"));

        Assert.Null(result.Phase8EReliabilityNotes);
        Assert.Null(result.Phase8EReliabilityMode);
    }

    // ── Family not in adaptation — no conditional match ───────────────────────

    [Fact]
    public void Apply_ShadowMode_FamilyNotInAdaptation_DoesNotGenerateConditionalNote()
    {
        var options = new RecursorPolicyReliabilityOptions
        {
            Mode = "shadow",
            ConditionalFamilyReliabilityTiers = new()
            {
                // Configured for a family that is not in this adaptation's InterventionFamilies.
                ["pace-increase"] = new() { ["PredictedConfusionRisk=high"] = "risky" }
            }
        };
        var svc      = Build(options);
        var decision = MakeDecision("difficulty-reduction");

        var result = svc.Apply(decision, null, MakePrediction("high"));

        Assert.Null(result.Phase8EReliabilityNotes);
    }
}
