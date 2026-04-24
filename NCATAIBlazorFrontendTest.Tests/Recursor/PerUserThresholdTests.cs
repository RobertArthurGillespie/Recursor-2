using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Unit tests for the Phase 8D per-user threshold layer.
///
/// Verifies that AdaptationPolicyService resolves effective thresholds as:
///   user-specific threshold (when present) → else global config default
///
/// Isolation: no shadowPrediction (ML guardrail and next-window guardrail both inactive).
/// Each test class enables only the relevant personalized rule to isolate behavior.
/// </summary>
public class PerUserThresholdTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

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

    // ── Confusion-block rule: per-user threshold tests ────────────────────────

    public class ConfusionBlockThresholdTests
    {
        private static AdaptationPolicyService CreateService(double globalThreshold) =>
            new(Options.Create(new RecursorPoliciesOptions
            {
                EnablePersonalizedHintFadeRule = false,
                EnablePersonalizedConfusionBlockDifficultyRule = true,
                PersonalizedConfusionBlockDifficultyIncreaseThreshold = globalThreshold,
                HintFadeRequiresSupportFadeEligibleWindows = 2,
                HintRemoveRequiresStableMasteryWindows = 2,
                EnableNextWindowHintDependenceGuardrail = false,
            }), NullLogger<AdaptationPolicyService>.Instance);

        private static PersonalizedPolicySignals SignalsWithConfusionDelta(double delta) =>
            new() { ConfusionDelta = delta, CurrentConfusionScore = 0.40 + delta };

        [Fact]
        public void UserThreshold_Present_OverridesGlobal_BlockFires()
        {
            // Global threshold = 0.10; user threshold = 0.05.
            // ConfusionDelta = 0.07 → above user threshold (0.07 > 0.05) but below global (0.07 < 0.10).
            // Expect: block fires (user threshold used).
            var sut = CreateService(globalThreshold: 0.10);
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = 0.05
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(0.07),
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
            Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
            Assert.Contains("Threshold=0.05", result.ReasoningSummary);
            Assert.Contains("[user-specific]", result.ReasoningSummary);
        }

        [Fact]
        public void UserThreshold_Present_OverridesGlobal_BlockDoesNotFire()
        {
            // Global threshold = 0.05; user threshold = 0.15 (more lenient).
            // ConfusionDelta = 0.10 → above global (0.10 > 0.05) but below user (0.10 < 0.15).
            // Expect: block does NOT fire (user threshold used).
            var sut = CreateService(globalThreshold: 0.05);
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = 0.15
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(0.10),
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
        }

        [Fact]
        public void NoUserThreshold_FallsBackToGlobal_BlockFires()
        {
            // No user threshold. Global threshold = 0.05. ConfusionDelta = 0.10.
            // Expect: block fires using global threshold.
            var sut = CreateService(globalThreshold: 0.05);

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(0.10),
                userThresholds: null);

            Assert.NotNull(result);
            Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
            Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
            Assert.Contains("Threshold=0.05", result.ReasoningSummary);
            Assert.DoesNotContain("[user-specific]", result.ReasoningSummary);
        }

        [Fact]
        public void NoUserThreshold_FallsBackToGlobal_BlockDoesNotFire()
        {
            // No user threshold. Global threshold = 0.15. ConfusionDelta = 0.10.
            // Expect: block does not fire.
            var sut = CreateService(globalThreshold: 0.15);

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(0.10),
                userThresholds: null);

            Assert.NotNull(result);
            Assert.Contains("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
        }

        [Fact]
        public void UserThresholdPresent_NullSpecificField_FallsBackToGlobal()
        {
            // UserPolicyThresholds exists but ConfusionBlockDifficultyIncreaseThreshold is null.
            // Global threshold = 0.05. ConfusionDelta = 0.10 → block fires via global.
            var sut = CreateService(globalThreshold: 0.05);
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = null  // not set for this user
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(0.10),
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
            Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
            Assert.Contains("Threshold=0.05", result.ReasoningSummary);
            Assert.DoesNotContain("[user-specific]", result.ReasoningSummary);
        }

        [Fact]
        public void UserThreshold_BoundaryAtExact_BlockDoesNotFire()
        {
            // ConfusionDelta exactly equals user threshold. Condition is strictly ">",
            // so the block must NOT fire.
            const double userThresh = 0.08;
            var sut = CreateService(globalThreshold: 0.05);
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = userThresh
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: SignalsWithConfusionDelta(userThresh),
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
        }
    }

    // ── Hint-fade rule: per-user threshold tests ──────────────────────────────

    public class HintFadeThresholdTests
    {
        private static AdaptationPolicyService CreateService(
            double globalConfusionDeltaThreshold = -0.05,
            double globalHintDependenceDeltaThreshold = -0.05,
            double globalMaxCurrentConfusion = 0.30,
            double globalMinGoalUnderstanding = 0.60) =>
            new(Options.Create(new RecursorPoliciesOptions
            {
                EnablePersonalizedHintFadeRule = true,
                EnablePersonalizedConfusionBlockDifficultyRule = false,
                PersonalizedHintFadeConfusionDeltaThreshold = globalConfusionDeltaThreshold,
                PersonalizedHintFadeHintDependenceDeltaThreshold = globalHintDependenceDeltaThreshold,
                PersonalizedHintFadeMaxCurrentConfusionScore = globalMaxCurrentConfusion,
                PersonalizedHintFadeMinCurrentGoalUnderstanding = globalMinGoalUnderstanding,
                HintFadeRequiresSupportFadeEligibleWindows = 2,
                HintRemoveRequiresStableMasteryWindows = 2,
                EnableNextWindowHintDependenceGuardrail = false,
            }), NullLogger<AdaptationPolicyService>.Instance);

        // Signals that satisfy all global hint-fade conditions.
        private static PersonalizedPolicySignals PassingSignals() => new()
        {
            ConfusionDelta = -0.10,
            HintDependenceDelta = -0.10,
            IsBelowBaselineGoalUnderstanding = false,
            IsBelowBaselineAttention = false,
            CurrentConfusionScore = 0.20,
            CurrentGoalUnderstanding = 0.70
        };

        [Fact]
        public void UserThreshold_ConfusionDelta_Tighter_RuleDoesNotFire()
        {
            // User threshold for confusion delta = -0.15 (stricter than global -0.05).
            // Signals have ConfusionDelta = -0.10 → fails user threshold (-0.10 is NOT <= -0.15).
            // Expect: rule does NOT fire.
            var sut = CreateService();
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                HintFadeConfusionDeltaThreshold = -0.15
            };
            var signals = PassingSignals(); // ConfusionDelta = -0.10

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: userThresholds);

            // hint-fade rule should not have fired (difficulty-increase + pace-increase remain,
            // but hint-fade should not have been added by the personalized rule).
            if (result is not null)
                Assert.DoesNotContain("personalized hint fade applied", result.ReasoningSummary);
        }

        [Fact]
        public void UserThreshold_ConfusionDelta_MoreLenient_RuleFires()
        {
            // Global threshold = -0.15 (strict). User threshold = -0.05 (lenient).
            // Signals have ConfusionDelta = -0.10 → fails global but passes user.
            // Expect: rule fires (user threshold used).
            var sut = CreateService(globalConfusionDeltaThreshold: -0.15);
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                HintFadeConfusionDeltaThreshold = -0.05
            };
            var signals = PassingSignals(); // ConfusionDelta = -0.10

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("personalized hint fade applied", result.ReasoningSummary);
            Assert.Contains("[user-specific thresholds]", result.ReasoningSummary);
        }

        [Fact]
        public void NoUserThreshold_FallsBackToGlobal_RuleFires()
        {
            // No user thresholds. Global thresholds are satisfied by PassingSignals.
            // Expect: rule fires using global defaults; no user-specific tag.
            var sut = CreateService();

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: PassingSignals(),
                userThresholds: null);

            Assert.NotNull(result);
            Assert.Contains("personalized hint fade applied", result.ReasoningSummary);
            Assert.DoesNotContain("[user-specific thresholds]", result.ReasoningSummary);
        }

        [Fact]
        public void NoUserThreshold_FallsBackToGlobal_RuleDoesNotFire()
        {
            // Global ConfusionDelta threshold = -0.15 (strict). PassingSignals has -0.10.
            // No user threshold to override. Expect: rule does NOT fire.
            var sut = CreateService(globalConfusionDeltaThreshold: -0.15);

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: PassingSignals(),
                userThresholds: null);

            if (result is not null)
                Assert.DoesNotContain("personalized hint fade applied", result.ReasoningSummary);
        }

        [Fact]
        public void UserThreshold_OnlyOneFieldSet_OtherFallsBackToGlobal_RuleFires()
        {
            // Only HintFadeConfusionDeltaThreshold is set to a lenient value.
            // HintFadeHintDependenceDeltaThreshold falls back to global (-0.05) which PassingSignals satisfies.
            // Expect: rule fires; [user-specific thresholds] tag present.
            var sut = CreateService(globalConfusionDeltaThreshold: -0.15); // strict global, user overrides to -0.05
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                HintFadeConfusionDeltaThreshold = -0.05
                // HintFadeHintDependenceDeltaThreshold not set → falls back to global -0.05
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: PassingSignals(),
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("personalized hint fade applied", result.ReasoningSummary);
            Assert.Contains("[user-specific thresholds]", result.ReasoningSummary);
        }

        [Fact]
        public void UserThreshold_BoundaryAtExact_ConfusionDelta_RuleFires()
        {
            // ConfusionDelta exactly equals user threshold. Condition is "<=", so rule fires.
            const double userThresh = -0.10;
            var sut = CreateService(globalConfusionDeltaThreshold: -0.05); // global is more lenient
            var userThresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                HintFadeConfusionDeltaThreshold = userThresh
            };
            var signals = PassingSignals(); // ConfusionDelta = -0.10, exactly at threshold

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("personalized hint fade applied", result.ReasoningSummary);
        }
    }

    // ── Repository: basic store and retrieve ──────────────────────────────────

    public class UserThresholdRepositoryTests
    {
        [Fact]
        public void GetReturnsNull_WhenUserNotStored()
        {
            var repo = new NCATAIBlazorFrontendTest.Server.Recursor.Repositories.InMemoryUserThresholdRepository();
            Assert.Null(repo.Get("unknown-user"));
        }

        [Fact]
        public void UpsertAndGet_ReturnsStoredThresholds()
        {
            var repo = new NCATAIBlazorFrontendTest.Server.Recursor.Repositories.InMemoryUserThresholdRepository();
            var thresholds = new UserPolicyThresholds
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = 0.08,
                HintFadeConfusionDeltaThreshold = -0.10
            };
            repo.Upsert(thresholds);

            var retrieved = repo.Get("u1");
            Assert.NotNull(retrieved);
            Assert.Equal(0.08, retrieved.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.10, retrieved.HintFadeConfusionDeltaThreshold);
            Assert.Null(retrieved.HintFadeHintDependenceDeltaThreshold);
        }

        [Fact]
        public void Upsert_Overwrites_PreviousEntry()
        {
            var repo = new NCATAIBlazorFrontendTest.Server.Recursor.Repositories.InMemoryUserThresholdRepository();
            repo.Upsert(new UserPolicyThresholds { UserId = "u1", ConfusionBlockDifficultyIncreaseThreshold = 0.05 });
            repo.Upsert(new UserPolicyThresholds { UserId = "u1", ConfusionBlockDifficultyIncreaseThreshold = 0.12 });

            var result = repo.Get("u1");
            Assert.NotNull(result);
            Assert.Equal(0.12, result.ConfusionBlockDifficultyIncreaseThreshold);
        }
    }
}
