using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Seeding;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Tests for RecursorTestThresholdSeeder.
///
/// Covers:
/// 1. Both test users are seeded with non-null records.
/// 2. Sensitive thresholds are strictly more conservative than tolerant thresholds.
/// 3. Global defaults sit between the two profiles on all axes.
/// 4. Policy-level behavioral split: at marginal signals, tolerant fires hint-fade
///    and sensitive does not; and at small confusion spikes, sensitive blocks
///    difficulty increase but tolerant does not.
/// </summary>
public class RecursorTestThresholdSeederTests
{
    // ── Shared repo seeded once per test ─────────────────────────────────────

    private static (UserPolicyThresholds sensitive, UserPolicyThresholds tolerant) SeedAndGet()
    {
        var repo = new InMemoryUserThresholdRepository();
        RecursorTestThresholdSeeder.Seed(repo);

        var sensitive = repo.Get(RecursorTestThresholdSeeder.SensitiveUserId)!;
        var tolerant  = repo.Get(RecursorTestThresholdSeeder.TolerantUserId)!;
        return (sensitive, tolerant);
    }

    // ── 1. Both users are seeded ──────────────────────────────────────────────

    [Fact]
    public void Seed_PopulatesBothUsers()
    {
        var repo = new InMemoryUserThresholdRepository();
        RecursorTestThresholdSeeder.Seed(repo);

        Assert.NotNull(repo.Get(RecursorTestThresholdSeeder.SensitiveUserId));
        Assert.NotNull(repo.Get(RecursorTestThresholdSeeder.TolerantUserId));
    }

    [Fact]
    public void Seed_SetsAllFieldsOnSensitiveUser()
    {
        var (s, _) = SeedAndGet();

        Assert.NotNull(s.ConfusionBlockDifficultyIncreaseThreshold);
        Assert.NotNull(s.HintFadeConfusionDeltaThreshold);
        Assert.NotNull(s.HintFadeHintDependenceDeltaThreshold);
        Assert.NotNull(s.HintFadeMaxCurrentConfusionScore);
        Assert.NotNull(s.HintFadeMinCurrentGoalUnderstanding);
    }

    [Fact]
    public void Seed_SetsAllFieldsOnTolerantUser()
    {
        var (_, t) = SeedAndGet();

        Assert.NotNull(t.ConfusionBlockDifficultyIncreaseThreshold);
        Assert.NotNull(t.HintFadeConfusionDeltaThreshold);
        Assert.NotNull(t.HintFadeHintDependenceDeltaThreshold);
        Assert.NotNull(t.HintFadeMaxCurrentConfusionScore);
        Assert.NotNull(t.HintFadeMinCurrentGoalUnderstanding);
    }

    // ── 2. Direction: sensitive is always stricter than tolerant ─────────────

    [Fact]
    public void ConfusionBlockThreshold_SensitiveLowerThanTolerant()
    {
        // Sensitive fires at smaller confusion spikes → lower threshold.
        var (s, t) = SeedAndGet();
        Assert.True(s.ConfusionBlockDifficultyIncreaseThreshold < t.ConfusionBlockDifficultyIncreaseThreshold,
            $"Expected sensitive ({s.ConfusionBlockDifficultyIncreaseThreshold}) < tolerant ({t.ConfusionBlockDifficultyIncreaseThreshold})");
    }

    [Fact]
    public void HintFadeConfusionDeltaThreshold_SensitiveMoreNegativeThanTolerant()
    {
        // Sensitive requires more improvement below baseline → more negative threshold.
        var (s, t) = SeedAndGet();
        Assert.True(s.HintFadeConfusionDeltaThreshold < t.HintFadeConfusionDeltaThreshold,
            $"Expected sensitive ({s.HintFadeConfusionDeltaThreshold}) < tolerant ({t.HintFadeConfusionDeltaThreshold})");
    }

    [Fact]
    public void HintFadeHintDependenceDeltaThreshold_SensitiveMoreNegativeThanTolerant()
    {
        var (s, t) = SeedAndGet();
        Assert.True(s.HintFadeHintDependenceDeltaThreshold < t.HintFadeHintDependenceDeltaThreshold);
    }

    [Fact]
    public void HintFadeMaxCurrentConfusionScore_SensitiveLowerThanTolerant()
    {
        // Sensitive requires lower absolute confusion to allow hint fade.
        var (s, t) = SeedAndGet();
        Assert.True(s.HintFadeMaxCurrentConfusionScore < t.HintFadeMaxCurrentConfusionScore);
    }

    [Fact]
    public void HintFadeMinCurrentGoalUnderstanding_SensitiveHigherThanTolerant()
    {
        // Sensitive requires higher goal understanding floor.
        var (s, t) = SeedAndGet();
        Assert.True(s.HintFadeMinCurrentGoalUnderstanding > t.HintFadeMinCurrentGoalUnderstanding);
    }

    // ── 3. Global defaults sit between the two profiles ──────────────────────
    // Global defaults: ConfusionBlock=0.05, HintFadeConfusionDelta=-0.05,
    //   HintFadeHintDependenceDelta=-0.05, MaxConfusion=0.30, MinGoal=0.60.

    [Fact]
    public void ConfusionBlockThreshold_GlobalBetweenSensitiveAndTolerant()
    {
        const double globalDefault = 0.05;
        var (s, t) = SeedAndGet();
        Assert.True(s.ConfusionBlockDifficultyIncreaseThreshold < globalDefault,
            "Sensitive confusion-block threshold should be below global default.");
        Assert.True(t.ConfusionBlockDifficultyIncreaseThreshold > globalDefault,
            "Tolerant confusion-block threshold should be above global default.");
    }

    [Fact]
    public void HintFadeConfusionDeltaThreshold_GlobalBetweenSensitiveAndTolerant()
    {
        const double globalDefault = -0.05;
        var (s, t) = SeedAndGet();
        Assert.True(s.HintFadeConfusionDeltaThreshold < globalDefault,
            "Sensitive hint-fade confusion threshold should be more negative than global.");
        Assert.True(t.HintFadeConfusionDeltaThreshold > globalDefault,
            "Tolerant hint-fade confusion threshold should be less negative than global.");
    }

    // ── 3b. Acceleration thresholds: global sits between sensitive and tolerant ──

    [Fact]
    public void AccelerationConfusionDeltaThreshold_GlobalBetweenSensitiveAndTolerant()
    {
        const double globalDefault = -0.08;
        var (s, t) = SeedAndGet();
        Assert.True(s.DifficultyAccelerationConfusionDeltaThreshold < globalDefault,
            "Sensitive acceleration confusion threshold should be more negative than global.");
        Assert.True(t.DifficultyAccelerationConfusionDeltaThreshold > globalDefault,
            "Tolerant acceleration confusion threshold should be less negative than global.");
    }

    [Fact]
    public void AccelerationDelta_SensitiveLowerThanTolerant()
    {
        var (s, t) = SeedAndGet();
        Assert.True(s.DifficultyAccelerationDelta < t.DifficultyAccelerationDelta,
            $"Sensitive delta ({s.DifficultyAccelerationDelta}) should be smaller than tolerant ({t.DifficultyAccelerationDelta}).");
    }

    // ── 4. Behavioral split at policy level ───────────────────────────────────
    // These tests drive AdaptationPolicyService directly with seeded thresholds.

    private static AdaptationPolicyService HintFadeService() =>
        new(Options.Create(new RecursorPoliciesOptions
        {
            EnablePersonalizedHintFadeRule = true,
            EnablePersonalizedConfusionBlockDifficultyRule = false,
            // Global defaults — chosen so they sit between the two profiles.
            PersonalizedHintFadeConfusionDeltaThreshold        = -0.05,
            PersonalizedHintFadeHintDependenceDeltaThreshold   = -0.05,
            PersonalizedHintFadeMaxCurrentConfusionScore       =  0.30,
            PersonalizedHintFadeMinCurrentGoalUnderstanding    =  0.60,
            HintFadeRequiresSupportFadeEligibleWindows         = 2,
            HintRemoveRequiresStableMasteryWindows             = 2,
            EnableNextWindowHintDependenceGuardrail            = false,
        }));

    private static AdaptationPolicyService ConfusionBlockService() =>
        new(Options.Create(new RecursorPoliciesOptions
        {
            EnablePersonalizedHintFadeRule = false,
            EnablePersonalizedConfusionBlockDifficultyRule = true,
            PersonalizedConfusionBlockDifficultyIncreaseThreshold = 0.05,
            HintFadeRequiresSupportFadeEligibleWindows            = 2,
            HintRemoveRequiresStableMasteryWindows                = 2,
            EnableNextWindowHintDependenceGuardrail               = false,
        }));

    private static SimCatalogDocument Catalog() => new()
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

    private static SessionDocument MasterySession() => new()
    {
        SessionId = "s1",
        UserId = "u1",
        SimId  = "test-sim",
        ConsecutiveSupportFadeEligibleWindows = 3,
        ConsecutiveStableMasteryWindows       = 3,
        ConsecutiveRelapseWindows             = 0,
        CurrentDifficultyProfile = new Dictionary<string, string> { ["hintMode"] = "guided" }
    };

    private static HypothesisSetDocument MasteryHypothesisSet() => new()
    {
        Hypotheses = new List<BehavioralHypothesis>
        {
            new() { Label = "stable_mastery_pattern", Confidence = 0.90 }
        }
    };

    /// <summary>
    /// Marginal improvement signals: ConfusionDelta=-0.03, HintDependenceDelta=-0.03.
    /// Tolerant threshold (-0.02) is satisfied; sensitive (-0.12) is not; global (-0.05) is not.
    /// Expected: only tolerant fires hint-fade.
    /// </summary>
    [Fact]
    public void HintFade_MarginalSignals_OnlyTolerantFires()
    {
        var (sensitive, tolerant) = SeedAndGet();
        var sut = HintFadeService();

        var marginalSignals = new PersonalizedPolicySignals
        {
            ConfusionDelta              = -0.03,
            HintDependenceDelta         = -0.03,
            IsBelowBaselineGoalUnderstanding = false,
            IsBelowBaselineAttention    = false,
            CurrentConfusionScore       = 0.22,   // satisfies both users' absolute gates
            CurrentGoalUnderstanding    = 0.75,   // satisfies both users' absolute gates
        };

        var resultSensitive = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: marginalSignals, userThresholds: sensitive);

        var resultTolerant = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: marginalSignals, userThresholds: tolerant);

        // Tolerant must fire the personalized hint-fade rule.
        Assert.NotNull(resultTolerant);
        Assert.Contains("personalized hint fade applied", resultTolerant.ReasoningSummary);
        Assert.Contains("[user-specific thresholds]", resultTolerant.ReasoningSummary);

        // Sensitive must NOT fire the personalized hint-fade rule.
        if (resultSensitive is not null)
            Assert.DoesNotContain("personalized hint fade applied", resultSensitive.ReasoningSummary);
    }

    private static AdaptationPolicyService AccelerationService() =>
        new(Options.Create(new RecursorPoliciesOptions
        {
            EnablePersonalizedDifficultyAccelerationRule               = true,
            // Global thresholds sit between sensitive and tolerant.
            PersonalizedDifficultyAccelerationConfusionDeltaThreshold  = -0.08,
            PersonalizedDifficultyAccelerationHintDependenceDeltaThreshold = -0.08,
            PersonalizedDifficultyAccelerationMaxCurrentConfusionScore = 0.25,
            PersonalizedDifficultyAccelerationMinCurrentGoalUnderstanding = 0.70,
            PersonalizedDifficultyAccelerationDelta                    = 0.08,

            EnablePersonalizedHintFadeRule                             = false,
            EnablePersonalizedConfusionBlockDifficultyRule             = false,
            HintFadeRequiresSupportFadeEligibleWindows                 = 2,
            HintRemoveRequiresStableMasteryWindows                     = 2,
            EnableNextWindowHintDependenceGuardrail                    = false,
        }));

    /// <summary>
    /// Marginal acceleration signals: ConfusionDelta=-0.06, HintDependenceDelta=-0.06.
    /// Tolerant acceleration threshold (-0.05) is satisfied → fires with tolerant delta (0.10).
    /// Global threshold (-0.08) is NOT satisfied → base delta (0.05) with global defaults.
    /// Sensitive threshold (-0.15) is NOT satisfied → base delta (0.05).
    /// </summary>
    [Fact]
    public void Acceleration_MarginalSignals_OnlyTolerantFires()
    {
        var (sensitive, tolerant) = SeedAndGet();
        var sut = AccelerationService();

        var marginalSignals = new PersonalizedPolicySignals
        {
            ConfusionDelta              = -0.06,
            HintDependenceDelta         = -0.06,
            IsBelowBaselineGoalUnderstanding = false,
            IsBelowBaselineAttention    = false,
            CurrentConfusionScore       = 0.20,
            CurrentGoalUnderstanding    = 0.75,
        };

        var resultSensitive = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: marginalSignals, userThresholds: sensitive);

        var resultTolerant = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: marginalSignals, userThresholds: tolerant);

        // Tolerant: acceleration fires with tolerant delta (0.10).
        Assert.NotNull(resultTolerant);
        Assert.Contains("personalized difficulty acceleration applied", resultTolerant.ReasoningSummary);
        Assert.Contains("[user-specific thresholds]", resultTolerant.ReasoningSummary);
        var tolerantDiff = resultTolerant.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(tolerantDiff);
        Assert.Equal(0.10, (double)tolerantDiff.Value!);

        // Sensitive: acceleration must NOT fire; difficulty stays at base 0.05.
        Assert.NotNull(resultSensitive);
        Assert.DoesNotContain("personalized difficulty acceleration", resultSensitive.ReasoningSummary);
        var sensitiveDiff = resultSensitive.ParameterChanges.FirstOrDefault(
            c => c.Parameter == "difficulty" && c.Operation == "increase");
        Assert.NotNull(sensitiveDiff);
        Assert.Equal(0.05, (double)sensitiveDiff.Value!);
    }

    /// <summary>
    /// Small confusion spike: ConfusionDelta=0.08.
    /// Sensitive threshold (0.02) is exceeded → blocks.
    /// Global (0.05) is exceeded → blocks.
    /// Tolerant threshold (0.15) is NOT exceeded → does not block.
    /// Expected: sensitive blocks difficulty increase; tolerant allows it.
    /// </summary>
    [Fact]
    public void ConfusionBlock_SmallSpike_SensitiveBlocksTolerantAllows()
    {
        var (sensitive, tolerant) = SeedAndGet();
        var sut = ConfusionBlockService();

        var signals = new PersonalizedPolicySignals
        {
            ConfusionDelta        = 0.08,
            CurrentConfusionScore = 0.48,
        };

        var resultSensitive = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: signals, userThresholds: sensitive);

        var resultTolerant = sut.ApplyPolicy(MasterySession(), Catalog(), MasteryHypothesisSet(),
            shadowPrediction: null, personalizedSignals: signals, userThresholds: tolerant);

        // Sensitive: difficulty-increase must be blocked.
        Assert.NotNull(resultSensitive);
        Assert.DoesNotContain("difficulty-increase", resultSensitive.InterventionFamilies);
        Assert.Contains("difficulty increase blocked", resultSensitive.ReasoningSummary);
        Assert.Contains("[user-specific]", resultSensitive.ReasoningSummary);

        // Tolerant: difficulty-increase must be present.
        Assert.NotNull(resultTolerant);
        Assert.Contains("difficulty-increase", resultTolerant.InterventionFamilies);
        Assert.DoesNotContain("difficulty increase blocked", resultTolerant.ReasoningSummary);
    }
}
