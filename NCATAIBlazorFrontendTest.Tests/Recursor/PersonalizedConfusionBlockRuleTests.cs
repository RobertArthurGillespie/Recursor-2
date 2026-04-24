using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Unit tests for the Phase 8 personalized confusion-based difficulty-increase block rule
/// in AdaptationPolicyService.
///
/// Isolation: EnablePersonalizedHintFadeRule is always false in these tests so only the
/// confusion block rule can affect the result. No shadowPrediction is passed, so the ML
/// guardrail and next-window guardrail are both inactive.
/// </summary>
public class PersonalizedConfusionBlockRuleTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdaptationPolicyService CreateService(
        bool confusionBlockEnabled,
        double confusionBlockThreshold = 0.05)
    {
        var options = Options.Create(new RecursorPoliciesOptions
        {
            EnablePersonalizedHintFadeRule = false,
            EnablePersonalizedConfusionBlockDifficultyRule = confusionBlockEnabled,
            PersonalizedConfusionBlockDifficultyIncreaseThreshold = confusionBlockThreshold,
            HintFadeRequiresSupportFadeEligibleWindows = 2,
            HintRemoveRequiresStableMasteryWindows = 2,
            EnableNextWindowHintDependenceGuardrail = false,
        });
        return new AdaptationPolicyService(options, NullLogger<AdaptationPolicyService>.Instance);
    }

    private static SimCatalogDocument CreateCatalog() => new()
    {
        SimId = "test-sim",
        AdaptiveParameters = new List<AdaptiveParameterDefinition>
        {
            new() { Name = "difficulty",    Type = "float", Min = 0.0, Max = 1.0 },
            new() { Name = "timePressure",  Type = "float", Min = 0.0, Max = 1.0 },
            new() { Name = "hintMode",      Type = "enum",
                    AllowedValues = new List<string> { "guided", "minimal", "off" } },
        }
    };

    // Session in guided hint mode with a streak of mastery/improving windows.
    private static SessionDocument CreateMasterySession() => new()
    {
        SessionId = "s1",
        UserId = "u1",
        SimId = "test-sim",
        ConsecutiveSupportFadeEligibleWindows = 3,
        ConsecutiveStableMasteryWindows = 3,
        ConsecutiveRelapseWindows = 0,
        CurrentDifficultyProfile = new Dictionary<string, string> { ["hintMode"] = "guided" }
    };

    private static HypothesisSetDocument MasteryHypothesisSet() => new()
    {
        Hypotheses = new List<BehavioralHypothesis>
        {
            new() { Label = "stable_mastery_pattern", Confidence = 0.90 }
        }
    };

    // A hypothesis set that never produces difficulty-increase.
    private static HypothesisSetDocument HintDependenceHypothesisSet() => new()
    {
        Hypotheses = new List<BehavioralHypothesis>
        {
            new() { Label = "hint_dependence_pattern", Confidence = 0.85 }
        }
    };

    private static PersonalizedPolicySignals SignalsWithConfusionDelta(double delta) =>
        new() { ConfusionDelta = delta, CurrentConfusionScore = 0.40 + delta };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConfusionBelowBaseline_DifficultyIncreaseAllowed()
    {
        // confusion is below personal baseline — rule must not fire
        var sut = CreateService(confusionBlockEnabled: true);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(-0.10));

        Assert.NotNull(result);
        Assert.Contains("difficulty-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
    }

    [Fact]
    public void ConfusionAboveBaseline_DifficultyIncreaseBlocked()
    {
        // confusion is above personal baseline — rule must fire and remove difficulty-increase
        var sut = CreateService(confusionBlockEnabled: true);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(+0.10));

        Assert.NotNull(result);
        Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
        Assert.Contains("ConfusionDelta=0.10", result.ReasoningSummary);
    }

    [Fact]
    public void NoDifficultyIncreasePresent_RuleHasNoEffect()
    {
        // hint_dependence_pattern produces hint-reduction only — no difficulty-increase to block
        var sut = CreateService(confusionBlockEnabled: true);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            HintDependenceHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(+0.10));

        // hint-reduction → hintMode set to minimal; result may or may not be null depending
        // on whether hint-reduction produces a change with the current hintMode, but the
        // confusion block note must never appear because the precondition (difficulty-increase
        // present) was not met.
        if (result is not null)
        {
            Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
        }
    }

    [Fact]
    public void FeatureFlagOff_RuleDoesNothing()
    {
        // flag disabled — confusion above baseline must not block difficulty-increase
        var sut = CreateService(confusionBlockEnabled: false);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(+0.20));

        Assert.NotNull(result);
        Assert.Contains("difficulty-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
    }

    [Fact]
    public void MultiActionAdaptation_OnlyDifficultyIncreaseRemoved()
    {
        // stable_mastery in guided mode with eligible streak produces:
        // difficulty-increase, pace-increase, hint-fade (after state machine)
        // The confusion block must remove only difficulty-increase.
        var sut = CreateService(confusionBlockEnabled: true);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(+0.10));

        Assert.NotNull(result);
        Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
        Assert.Contains("pace-increase", result.InterventionFamilies);
        Assert.Contains("hint-fade", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "timePressure" && c.Operation == "increase");
        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "hintMode");
    }

    [Fact]
    public void ConfusionDeltaAtExactThreshold_RuleDoesNotFire()
    {
        // ConfusionDelta == threshold exactly — the condition is strictly ">", so the rule
        // must NOT fire and difficulty-increase must remain in the result.
        const double threshold = 0.05;
        var sut = CreateService(confusionBlockEnabled: true, confusionBlockThreshold: threshold);
        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: SignalsWithConfusionDelta(threshold));

        Assert.NotNull(result);
        Assert.Contains("difficulty-increase", result.InterventionFamilies);
        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
    }
}
