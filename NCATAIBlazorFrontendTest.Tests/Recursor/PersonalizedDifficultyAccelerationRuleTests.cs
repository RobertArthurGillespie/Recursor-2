using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Unit tests for the Phase 8 personalized difficulty acceleration rule
/// in AdaptationPolicyService.
///
/// Isolation strategy: only EnablePersonalizedDifficultyAccelerationRule changes
/// across tests; EnablePersonalizedHintFadeRule and EnablePersonalizedConfusionBlockDifficultyRule
/// are always false unless a test explicitly combines them. No shadowPrediction is passed,
/// so the ML guardrail and next-window guardrail are inactive.
/// </summary>
public class PersonalizedDifficultyAccelerationRuleTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdaptationPolicyService CreateService(
        bool accelerationEnabled,
        double globalConfusionDeltaThreshold = -0.08,
        double globalHintDependenceDeltaThreshold = -0.08,
        double globalMaxConfusion = 0.25,
        double globalMinGoalUnderstanding = 0.70,
        double globalAccelDelta = 0.08,
        bool confusionBlockEnabled = false,
        double confusionBlockThreshold = 0.05)
    {
        var options = Options.Create(new RecursorPoliciesOptions
        {
            EnablePersonalizedDifficultyAccelerationRule               = accelerationEnabled,
            PersonalizedDifficultyAccelerationConfusionDeltaThreshold  = globalConfusionDeltaThreshold,
            PersonalizedDifficultyAccelerationHintDependenceDeltaThreshold = globalHintDependenceDeltaThreshold,
            PersonalizedDifficultyAccelerationMaxCurrentConfusionScore = globalMaxConfusion,
            PersonalizedDifficultyAccelerationMinCurrentGoalUnderstanding = globalMinGoalUnderstanding,
            PersonalizedDifficultyAccelerationDelta                    = globalAccelDelta,

            EnablePersonalizedHintFadeRule                             = false,
            EnablePersonalizedConfusionBlockDifficultyRule             = confusionBlockEnabled,
            PersonalizedConfusionBlockDifficultyIncreaseThreshold      = confusionBlockThreshold,

            HintFadeRequiresSupportFadeEligibleWindows = 2,
            HintRemoveRequiresStableMasteryWindows     = 2,
            EnableNextWindowHintDependenceGuardrail    = false,
        });
        return new AdaptationPolicyService(options);
    }

    private static SimCatalogDocument CreateCatalog() => new()
    {
        SimId = "test-sim",
        AdaptiveParameters = new List<AdaptiveParameterDefinition>
        {
            new() { Name = "difficulty",   Type = "float", Min = 0.0, Max = 1.0 },
            new() { Name = "timePressure", Type = "float", Min = 0.0, Max = 1.0 },
            new() { Name = "hintMode",     Type = "enum",
                    AllowedValues = new List<string> { "guided", "minimal", "off" } },
        }
    };

    // Mastery session in guided mode with a streak that satisfies hint-fade prereqs.
    private static SessionDocument CreateMasterySession(bool hasRelapse = false) => new()
    {
        SessionId = "s1",
        UserId    = "u1",
        SimId     = "test-sim",
        ConsecutiveSupportFadeEligibleWindows = hasRelapse ? 0 : 3,
        ConsecutiveStableMasteryWindows       = hasRelapse ? 0 : 3,
        ConsecutiveRelapseWindows             = hasRelapse ? 2 : 0,
        CurrentDifficultyProfile = new Dictionary<string, string> { ["hintMode"] = "guided" }
    };

    private static HypothesisSetDocument MasteryHypothesisSet() => new()
    {
        Hypotheses = new List<BehavioralHypothesis>
        {
            new() { Label = "stable_mastery_pattern", Confidence = 0.90 }
        }
    };

    private static HypothesisSetDocument RelapseHypothesisSet() => new()
    {
        Hypotheses = new List<BehavioralHypothesis>
        {
            new() { Label = "relapse_pattern", Confidence = 0.90 }
        }
    };

    // Strong above-baseline signals that satisfy default global thresholds.
    private static PersonalizedPolicySignals StrongAboveBaselineSignals() => new()
    {
        ConfusionDelta              = -0.12,
        HintDependenceDelta         = -0.12,
        IsBelowBaselineGoalUnderstanding = false,
        IsBelowBaselineAttention    = false,
        CurrentConfusionScore       = 0.15,
        CurrentGoalUnderstanding    = 0.82,
    };

    // Marginal signals that sit between the tolerant (-0.05) and global (-0.08) thresholds.
    private static PersonalizedPolicySignals MarginalSignals() => new()
    {
        ConfusionDelta              = -0.06,
        HintDependenceDelta         = -0.06,
        IsBelowBaselineGoalUnderstanding = false,
        IsBelowBaselineAttention    = false,
        CurrentConfusionScore       = 0.20,
        CurrentGoalUnderstanding    = 0.75,
    };

    // ── Test 1: flag off → no acceleration ───────────────────────────────────

    [Fact]
    public void FlagOff_StrongSignals_AccelerationDoesNotFire()
    {
        var sut = CreateService(accelerationEnabled: false);

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: StrongAboveBaselineSignals());

        Assert.NotNull(result);
        // Difficulty increase must be at the base delta, not accelerated.
        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.05, (double)diffChange.Value!);
        Assert.DoesNotContain("personalized difficulty acceleration", result.ReasoningSummary);
    }

    // ── Test 2: no user thresholds → global defaults used ────────────────────

    [Fact]
    public void GlobalDefaults_StrongSignals_AccelerationFires()
    {
        var sut = CreateService(accelerationEnabled: true, globalAccelDelta: 0.08);

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: StrongAboveBaselineSignals(),
            userThresholds: null);

        Assert.NotNull(result);
        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.08, (double)diffChange.Value!);
        Assert.Contains("personalized difficulty acceleration applied", result.ReasoningSummary);
        // No user-specific tag when no userThresholds provided.
        Assert.DoesNotContain("[user-specific thresholds]", result.ReasoningSummary);
    }

    [Fact]
    public void GlobalDefaults_WeakSignals_AccelerationDoesNotFire()
    {
        // Marginal signals (-0.06) are above the global threshold (-0.08) → rule must not fire.
        var sut = CreateService(accelerationEnabled: true);

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: MarginalSignals(),
            userThresholds: null);

        Assert.NotNull(result);
        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.05, (double)diffChange.Value!);
        Assert.DoesNotContain("personalized difficulty acceleration", result.ReasoningSummary);
    }

    // ── Test 3: tolerant user threshold → fires when global would not ─────────

    [Fact]
    public void TolerantThreshold_MarginalSignals_AccelerationFires()
    {
        // Signals at -0.06 pass tolerant threshold (-0.05) but NOT global (-0.08).
        var sut = CreateService(accelerationEnabled: true, globalAccelDelta: 0.08);

        var tolerantThresholds = new UserPolicyThresholds
        {
            UserId = "tolerant",
            DifficultyAccelerationConfusionDeltaThreshold      = -0.05,
            DifficultyAccelerationHintDependenceDeltaThreshold = -0.05,
            DifficultyAccelerationMaxCurrentConfusionScore     =  0.32,
            DifficultyAccelerationMinCurrentGoalUnderstanding  =  0.62,
            DifficultyAccelerationDelta                        =  0.10,
        };

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: MarginalSignals(),
            userThresholds: tolerantThresholds);

        Assert.NotNull(result);
        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.10, (double)diffChange.Value!);
        Assert.Contains("personalized difficulty acceleration applied", result.ReasoningSummary);
        Assert.Contains("[user-specific thresholds]", result.ReasoningSummary);
        Assert.Contains("AcceleratedDelta=0.10", result.ReasoningSummary);
    }

    // ── Test 4: sensitive threshold → does not fire for marginal signals ──────

    [Fact]
    public void SensitiveThreshold_MarginalSignals_AccelerationDoesNotFire()
    {
        // Signals at -0.06 do NOT pass sensitive threshold (-0.15).
        var sut = CreateService(accelerationEnabled: true);

        var sensitiveThresholds = new UserPolicyThresholds
        {
            UserId = "sensitive",
            DifficultyAccelerationConfusionDeltaThreshold      = -0.15,
            DifficultyAccelerationHintDependenceDeltaThreshold = -0.15,
            DifficultyAccelerationMaxCurrentConfusionScore     =  0.18,
            DifficultyAccelerationMinCurrentGoalUnderstanding  =  0.78,
            DifficultyAccelerationDelta                        =  0.06,
        };

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: MarginalSignals(),
            userThresholds: sensitiveThresholds);

        Assert.NotNull(result);
        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.05, (double)diffChange.Value!);
        Assert.DoesNotContain("personalized difficulty acceleration", result.ReasoningSummary);
    }

    // ── Test 5: confusion-block removes difficulty-increase → acceleration does not re-add ──

    [Fact]
    public void ConfusionBlockRemovesDifficultyIncrease_AccelerationDoesNotReAdd()
    {
        // Confusion delta is positive — confusion block fires and removes difficulty-increase.
        // Acceleration cannot fire (its confusion gate also fails, and existingDifficultyIncrease is null).
        var sut = CreateService(
            accelerationEnabled: true,
            confusionBlockEnabled: true,
            confusionBlockThreshold: 0.05);

        var signals = new PersonalizedPolicySignals
        {
            ConfusionDelta              = +0.10,   // above confusion-block threshold → block fires
            HintDependenceDelta         = -0.15,
            IsBelowBaselineGoalUnderstanding = false,
            IsBelowBaselineAttention    = false,
            CurrentConfusionScore       = 0.50,
            CurrentGoalUnderstanding    = 0.75,
        };

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: signals);

        Assert.NotNull(result);
        Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
        Assert.DoesNotContain(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.DoesNotContain("personalized difficulty acceleration", result.ReasoningSummary);
        Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
    }

    // ── Test 6: relapse active → acceleration does not fire ──────────────────

    [Fact]
    public void RelapseActive_StrongAboveBaselineSignals_AccelerationDoesNotFire()
    {
        var sut = CreateService(accelerationEnabled: true);

        var result = sut.ApplyPolicy(
            CreateMasterySession(hasRelapse: true),
            CreateCatalog(),
            RelapseHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: StrongAboveBaselineSignals());

        // Relapse hypothesis → difficulty-reduction, not difficulty-increase.
        if (result is not null)
        {
            Assert.DoesNotContain(result.ParameterChanges,
                c => c.Parameter == "difficulty" && c.Operation == "increase");
            Assert.DoesNotContain("personalized difficulty acceleration", result.ReasoningSummary);
        }
    }

    // ── Test 7: acceleration modifies only difficulty increase, not hint/pace ─

    [Fact]
    public void AccelerationFires_OnlyDifficultyDeltaChanged_HintAndPaceUnaffected()
    {
        // stable_mastery in guided mode with eligible streak produces:
        // difficulty-increase (0.05 base), pace-increase (0.05), hint-fade (guided→minimal).
        // Acceleration must boost only the difficulty value.
        var sut = CreateService(accelerationEnabled: true, globalAccelDelta: 0.08);

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: StrongAboveBaselineSignals());

        Assert.NotNull(result);

        var diffChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(diffChange);
        Assert.Equal(0.08, (double)diffChange.Value!);

        // timePressure must remain at the base 0.05 delta — not accelerated.
        var paceChange = result.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "timePressure" && c.Operation == "increase");
        Assert.NotNull(paceChange);
        Assert.Equal(0.05, (double)paceChange.Value!);

        // Hint mode transition must still be present (hint-fade → minimal).
        Assert.Contains("hint-fade", result.InterventionFamilies);
    }

    // ── Test 8: reasoning output includes required fields ─────────────────────

    [Fact]
    public void AccelerationNote_ContainsConfusionDeltaHintDependenceDeltaAndAcceleratedDelta()
    {
        var sut = CreateService(accelerationEnabled: true, globalAccelDelta: 0.08);

        var signals = new PersonalizedPolicySignals
        {
            ConfusionDelta              = -0.12,
            HintDependenceDelta         = -0.09,
            IsBelowBaselineGoalUnderstanding = false,
            IsBelowBaselineAttention    = false,
            CurrentConfusionScore       = 0.18,
            CurrentGoalUnderstanding    = 0.80,
        };

        var result = sut.ApplyPolicy(
            CreateMasterySession(),
            CreateCatalog(),
            MasteryHypothesisSet(),
            shadowPrediction: null,
            personalizedSignals: signals);

        Assert.NotNull(result);
        Assert.Contains("personalized difficulty acceleration applied", result.ReasoningSummary);
        Assert.Contains("ConfusionDelta=-0.12", result.ReasoningSummary);
        Assert.Contains("HintDependenceDelta=-0.09", result.ReasoningSummary);
        Assert.Contains("AcceleratedDelta=0.08", result.ReasoningSummary);
    }
}
