using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 6A tests for the persistent user profile foundation.
///
/// Coverage:
///   - UserBehaviorProfile model round-trips through InMemoryUserProfileRepository
///   - UserProfileThresholdMapper maps all fields correctly
///   - Null threshold fields in the profile map to null in UserPolicyThresholds
///     (policy falls back to global defaults — same behaviour as no userThresholds at all)
///   - AdaptationPolicyService receives correct thresholds derived from a profile
///   - Missing profile falls back safely (no crash, global defaults apply)
///   - Seeded IUserThresholdRepository path is preserved and still functional
/// </summary>
public class UserProfilePhase6ATests
{
    // ── InMemoryUserProfileRepository ─────────────────────────────────────────

    public class RepositoryTests
    {
        [Fact]
        public async Task GetAsync_ReturnsNull_WhenUserNotStored()
        {
            var repo = new InMemoryUserProfileRepository();
            var result = await repo.GetAsync("unknown-user");
            Assert.Null(result);
        }

        [Fact]
        public async Task UpsertAndGet_RoundTrip()
        {
            var repo = new InMemoryUserProfileRepository();
            var profile = new UserBehaviorProfile
            {
                UserId = "u1",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                TotalSessions = 5,
                TotalWindows = 30,
                LastSessionId = "session-abc",
                AvgConfusionScore = 0.35,
                ConfusionBlockDifficultyIncreaseThreshold = 0.08,
                HintFadeConfusionDeltaThreshold = -0.12,
            };

            await repo.UpsertAsync(profile);
            var retrieved = await repo.GetAsync("u1");

            Assert.NotNull(retrieved);
            Assert.Equal("u1", retrieved.UserId);
            Assert.Equal(5, retrieved.TotalSessions);
            Assert.Equal(30, retrieved.TotalWindows);
            Assert.Equal("session-abc", retrieved.LastSessionId);
            Assert.Equal(0.35, retrieved.AvgConfusionScore);
            Assert.Equal(0.08, retrieved.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.12, retrieved.HintFadeConfusionDeltaThreshold);
        }

        [Fact]
        public async Task UpsertAsync_Overwrites_ExistingEntry()
        {
            var repo = new InMemoryUserProfileRepository();

            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId = "u1",
                TotalSessions = 2,
                ConfusionBlockDifficultyIncreaseThreshold = 0.05
            });
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId = "u1",
                TotalSessions = 7,
                ConfusionBlockDifficultyIncreaseThreshold = 0.12
            });

            var result = await repo.GetAsync("u1");
            Assert.NotNull(result);
            Assert.Equal(7, result.TotalSessions);
            Assert.Equal(0.12, result.ConfusionBlockDifficultyIncreaseThreshold);
        }

        [Fact]
        public async Task GetAsync_DifferentUsers_AreIndependent()
        {
            var repo = new InMemoryUserProfileRepository();
            await repo.UpsertAsync(new UserBehaviorProfile { UserId = "u1", TotalSessions = 10 });
            await repo.UpsertAsync(new UserBehaviorProfile { UserId = "u2", TotalSessions = 20 });

            var u1 = await repo.GetAsync("u1");
            var u2 = await repo.GetAsync("u2");

            Assert.Equal(10, u1!.TotalSessions);
            Assert.Equal(20, u2!.TotalSessions);
        }
    }

    // ── UserProfileThresholdMapper ────────────────────────────────────────────

    public class MapperTests
    {
        private static UserBehaviorProfile FullProfile() => new()
        {
            UserId = "u1",
            ConfusionBlockDifficultyIncreaseThreshold          = 0.07,
            HintFadeConfusionDeltaThreshold                    = -0.10,
            HintFadeHintDependenceDeltaThreshold               = -0.08,
            HintFadeMaxCurrentConfusionScore                   = 0.22,
            HintFadeMinCurrentGoalUnderstanding                = 0.72,
            DifficultyAccelerationConfusionDeltaThreshold      = -0.15,
            DifficultyAccelerationHintDependenceDeltaThreshold = -0.14,
            DifficultyAccelerationMaxCurrentConfusionScore     = 0.18,
            DifficultyAccelerationMinCurrentGoalUnderstanding  = 0.78,
            DifficultyAccelerationDelta                        = 0.06,
        };

        [Fact]
        public void AllThresholdFields_MappedCorrectly()
        {
            var thresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(FullProfile());

            Assert.Equal("u1", thresholds.UserId);
            Assert.Equal(0.07,  thresholds.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.10, thresholds.HintFadeConfusionDeltaThreshold);
            Assert.Equal(-0.08, thresholds.HintFadeHintDependenceDeltaThreshold);
            Assert.Equal(0.22,  thresholds.HintFadeMaxCurrentConfusionScore);
            Assert.Equal(0.72,  thresholds.HintFadeMinCurrentGoalUnderstanding);
            Assert.Equal(-0.15, thresholds.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.Equal(-0.14, thresholds.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.Equal(0.18,  thresholds.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.Equal(0.78,  thresholds.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.Equal(0.06,  thresholds.DifficultyAccelerationDelta);
        }

        [Fact]
        public void NullThresholdFields_MappedToNull_PolicyFallsBackToGlobals()
        {
            // A profile that has baseline data but no threshold overrides.
            var profile = new UserBehaviorProfile
            {
                UserId = "u1",
                AvgConfusionScore = 0.40,
                TotalSessions = 3,
            };

            var thresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(profile);

            Assert.Equal("u1", thresholds.UserId);
            Assert.Null(thresholds.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(thresholds.HintFadeConfusionDeltaThreshold);
            Assert.Null(thresholds.HintFadeHintDependenceDeltaThreshold);
            Assert.Null(thresholds.HintFadeMaxCurrentConfusionScore);
            Assert.Null(thresholds.HintFadeMinCurrentGoalUnderstanding);
            Assert.Null(thresholds.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.Null(thresholds.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.Null(thresholds.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.Null(thresholds.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.Null(thresholds.DifficultyAccelerationDelta);
        }

        [Fact]
        public void PartialThresholds_MixNullAndSet()
        {
            // Only confusion-block threshold is set; all others are null.
            var profile = new UserBehaviorProfile
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = 0.03,
            };

            var thresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(profile);

            Assert.Equal(0.03, thresholds.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(thresholds.HintFadeConfusionDeltaThreshold);
            Assert.Null(thresholds.DifficultyAccelerationDelta);
        }
    }

    // ── Policy integration: profile thresholds reach AdaptationPolicyService ──

    public class PolicyIntegrationTests
    {
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

        private static AdaptationPolicyService CreatePolicyService(
            bool enableConfusionBlock = true,
            double globalConfusionBlockThreshold = 0.10) =>
            new(Options.Create(new RecursorPoliciesOptions
            {
                EnablePersonalizedHintFadeRule = false,
                EnablePersonalizedConfusionBlockDifficultyRule = enableConfusionBlock,
                PersonalizedConfusionBlockDifficultyIncreaseThreshold = globalConfusionBlockThreshold,
                HintFadeRequiresSupportFadeEligibleWindows = 2,
                HintRemoveRequiresStableMasteryWindows = 2,
                EnableNextWindowHintDependenceGuardrail = false,
            }), new Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptationPolicyService>());

        [Fact]
        public void ProfileThresholds_ReachPolicy_ConfusionBlockFires()
        {
            // Global threshold = 0.10; profile threshold = 0.04.
            // ConfusionDelta = 0.06 → above profile threshold (0.06 > 0.04) but below global.
            // Profile thresholds used → block fires.
            var profile = new UserBehaviorProfile
            {
                UserId = "u1",
                ConfusionBlockDifficultyIncreaseThreshold = 0.04
            };
            var userThresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(profile);
            var sut = CreatePolicyService(globalConfusionBlockThreshold: 0.10);

            var signals = new PersonalizedPolicySignals
            {
                ConfusionDelta = 0.06,
                CurrentConfusionScore = 0.46
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.DoesNotContain("difficulty-increase", result.InterventionFamilies);
            Assert.Contains("difficulty increase blocked", result.ReasoningSummary);
            Assert.Contains("[user-specific]", result.ReasoningSummary);
        }

        [Fact]
        public void MissingProfile_NullUserThresholds_PolicyUsesGlobalDefaults()
        {
            // No profile, no userThresholds. Global threshold = 0.10. ConfusionDelta = 0.06.
            // 0.06 <= 0.10, so confusion block does NOT fire — difficulty-increase passes through.
            var sut = CreatePolicyService(globalConfusionBlockThreshold: 0.10);

            var signals = new PersonalizedPolicySignals
            {
                ConfusionDelta = 0.06,
                CurrentConfusionScore = 0.46
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: null);

            Assert.NotNull(result);
            Assert.Contains("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("difficulty increase blocked", result.ReasoningSummary);
        }

        [Fact]
        public void ProfileWithAllNullThresholds_BehavesLikeGlobalDefaults()
        {
            // Profile exists but has no threshold overrides (all null).
            // Global threshold = 0.10. ConfusionDelta = 0.06 → block should NOT fire.
            var profile = new UserBehaviorProfile
            {
                UserId = "u1",
                AvgConfusionScore = 0.40,
                TotalSessions = 5,
                // All threshold overrides remain null
            };
            var userThresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(profile);
            var sut = CreatePolicyService(globalConfusionBlockThreshold: 0.10);

            var signals = new PersonalizedPolicySignals
            {
                ConfusionDelta = 0.06,
                CurrentConfusionScore = 0.46
            };

            var result = sut.ApplyPolicy(
                CreateMasterySession(), CreateCatalog(), MasteryHypothesisSet(),
                shadowPrediction: null,
                personalizedSignals: signals,
                userThresholds: userThresholds);

            Assert.NotNull(result);
            Assert.Contains("difficulty-increase", result.InterventionFamilies);
            Assert.DoesNotContain("[user-specific]", result.ReasoningSummary);
        }
    }

    // ── Seeded IUserThresholdRepository path still works ──────────────────────

    public class SeededFallbackTests
    {
        [Fact]
        public void SeededThresholdRepository_StillFunctional()
        {
            // Verify that the existing IUserThresholdRepository seeding path is unbroken.
            var repo = new InMemoryUserThresholdRepository();
            repo.Upsert(new UserPolicyThresholds
            {
                UserId = "seeded-user",
                ConfusionBlockDifficultyIncreaseThreshold = 0.05,
                HintFadeConfusionDeltaThreshold = -0.10
            });

            var result = repo.Get("seeded-user");

            Assert.NotNull(result);
            Assert.Equal(0.05, result.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.10, result.HintFadeConfusionDeltaThreshold);
        }

        [Fact]
        public void SeededThresholdRepository_ReturnsNull_WhenNotSeeded()
        {
            var repo = new InMemoryUserThresholdRepository();
            Assert.Null(repo.Get("unknown-user"));
        }
    }
}
