using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 6B tests for UserProfileUpdateService.
///
/// Coverage:
///   - New profile creation when no profile exists
///   - TotalWindows increments on each call
///   - TotalSessions increments only when session changes
///   - LastSessionId is updated on session change
///   - Rolling averages (Welford's incremental mean) are numerically correct
///   - StableMasteryRate / ConfusionRate / HintDependenceRate update correctly
///   - Threshold override fields are never cleared during behavioral updates
///   - Null BehaviorScores and missing DimensionScore keys are handled safely
///   - IncrementalMean helper formula correctness
///   - ADX mapper produces correct row shapes
/// </summary>
public class UserProfilePhase6BTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private static UserProfileUpdateService CreateService(
        IUserProfileRepository repo,
        IAdxIngestionService? adx = null)
        => new(repo, adx ?? new NoOpAdxIngestion(), NullLogger<UserProfileUpdateService>.Instance);

    private static BehaviorProfileDocument MakeProfile(
        string sessionId = "s1",
        double confusion = 0.0,
        double hintDependence = 0.0,
        double hesitation = 0.0,
        double goalUnderstanding = 0.0,
        double attentionDetection = 0.0,
        double procedureSequencing = 0.0,
        double selfCorrection = 0.0,
        double taskContinuity = 0.0)
        => new()
        {
            SessionId = sessionId,
            UserId = "u1",
            WindowIndex = 1,
            BehaviorScores = new BehaviorScores
            {
                ConfusionScore      = confusion,
                HintDependenceScore = hintDependence,
                HesitationScore     = hesitation,
            },
            DimensionScores = new Dictionary<string, DimensionScore>
            {
                ["goalUnderstanding"]   = new() { Score = goalUnderstanding },
                ["attentionDetection"]  = new() { Score = attentionDetection },
                ["procedureSequencing"] = new() { Score = procedureSequencing },
                ["selfCorrection"]      = new() { Score = selfCorrection },
                ["taskContinuity"]      = new() { Score = taskContinuity },
            }
        };

    // No-op ADX stub — prevents real ADX calls in unit tests.
    private sealed class NoOpAdxIngestion : IAdxIngestionService
    {
        public Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows) => Task.CompletedTask;
        public Task IngestFeatureWindowAsync(FeatureWindowRow row) => Task.CompletedTask;
        public Task IngestBehaviorProfileAsync(BehaviorProfileRow row) => Task.CompletedTask;
        public Task IngestHypothesisSetAsync(HypothesisSetRow row) => Task.CompletedTask;
        public Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row) => Task.CompletedTask;
        public Task IngestBehaviorStateTrainingRowAsync(BehaviorStateTrainingRow row) => Task.CompletedTask;
        public Task IngestUserBehaviorProfileAsync(UserBehaviorProfileRow row) => Task.CompletedTask;
        public Task IngestUserBehaviorProfileUpdateAsync(UserBehaviorProfileUpdateRow row) => Task.CompletedTask;
    }

    // ── New profile creation ──────────────────────────────────────────────────

    public class NewProfileTests
    {
        [Fact]
        public async Task NewProfile_IsCreated_WhenNoProfileExists()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.NotNull(profile);
            Assert.Equal("u1", profile.UserId);
        }

        [Fact]
        public async Task NewProfile_TotalWindows_IsOne()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(1, profile!.TotalWindows);
        }

        [Fact]
        public async Task NewProfile_TotalSessions_IsOne()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(1, profile!.TotalSessions);
        }

        [Fact]
        public async Task NewProfile_LastSessionId_IsSet()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal("s1", profile!.LastSessionId);
        }

        [Fact]
        public async Task NewProfile_FirstWindow_AveragesEqualWindowValues()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0,
                MakeProfile(confusion: 0.40, hintDependence: 0.30, hesitation: 0.20,
                            goalUnderstanding: 0.70, attentionDetection: 0.65,
                            procedureSequencing: 0.55, selfCorrection: 0.50, taskContinuity: 0.60),
                []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.40, profile!.AvgConfusionScore,      precision: 10);
            Assert.Equal(0.30, profile.AvgHintDependenceScore,   precision: 10);
            Assert.Equal(0.20, profile.AvgHesitationScore,       precision: 10);
            Assert.Equal(0.70, profile.AvgGoalUnderstanding,     precision: 10);
            Assert.Equal(0.65, profile.AvgAttentionDetection,    precision: 10);
            Assert.Equal(0.55, profile.AvgProcedureSequencing,   precision: 10);
            Assert.Equal(0.50, profile.AvgSelfCorrection,        precision: 10);
            Assert.Equal(0.60, profile.AvgTaskContinuity,        precision: 10);
        }
    }

    // ── Incremental count updates ─────────────────────────────────────────────

    public class CountTests
    {
        [Fact]
        public async Task TotalWindows_IncrementsEachCall()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 2, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(3, profile!.TotalWindows);
        }

        [Fact]
        public async Task TotalSessions_UnchangedWhenSameSession()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 2, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(1, profile!.TotalSessions);
        }

        [Fact]
        public async Task TotalSessions_IncrementsWhenSessionChanges()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s2", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s3", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(3, profile!.TotalSessions);
        }

        [Fact]
        public async Task LastSessionId_UpdatesToMostRecentSession()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s2", 0, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal("s2", profile!.LastSessionId);
        }

        [Fact]
        public async Task TotalSessions_NotIncrementedWhenSessionRepeatsAfterGap()
        {
            // s1 → s2 → s1 again: s1 is "new" relative to the last seen session (s2)
            // so TotalSessions should be 3.
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s2", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(3, profile!.TotalSessions);
        }
    }

    // ── Rolling averages ──────────────────────────────────────────────────────

    public class RollingAverageTests
    {
        [Fact]
        public async Task AvgConfusionScore_TwoWindows_EqualsArithmeticMean()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(confusion: 0.20), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(confusion: 0.60), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.40, profile!.AvgConfusionScore, precision: 10);
        }

        [Fact]
        public async Task AvgConfusionScore_ThreeWindows_CorrectMean()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(confusion: 0.30), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(confusion: 0.60), []);
            await sut.UpdateProfileAsync("u1", "s1", 2, MakeProfile(confusion: 0.90), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.60, profile!.AvgConfusionScore, precision: 10);
        }

        [Fact]
        public async Task AllDimensionScores_TwoWindows_EqualsArithmeticMean()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0,
                MakeProfile(goalUnderstanding: 0.40, attentionDetection: 0.50,
                            procedureSequencing: 0.60, selfCorrection: 0.70, taskContinuity: 0.80), []);

            await sut.UpdateProfileAsync("u1", "s1", 1,
                MakeProfile(goalUnderstanding: 0.80, attentionDetection: 0.90,
                            procedureSequencing: 1.00, selfCorrection: 0.50, taskContinuity: 0.40), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.60, profile!.AvgGoalUnderstanding,   precision: 10);
            Assert.Equal(0.70, profile.AvgAttentionDetection,   precision: 10);
            Assert.Equal(0.80, profile.AvgProcedureSequencing,  precision: 10);
            Assert.Equal(0.60, profile.AvgSelfCorrection,       precision: 10);
            Assert.Equal(0.60, profile.AvgTaskContinuity,       precision: 10);
        }
    }

    // ── Rate fields ───────────────────────────────────────────────────────────

    public class RateTests
    {
        [Fact]
        public async Task StableMasteryRate_AllMastery_IsOne()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["stable_mastery_pattern"]);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), ["stable_mastery_pattern"]);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(1.0, profile!.StableMasteryRate, precision: 10);
        }

        [Fact]
        public async Task StableMasteryRate_NoMastery_IsZero()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), []);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.0, profile!.StableMasteryRate, precision: 10);
        }

        [Fact]
        public async Task StableMasteryRate_HalfMastery_IsHalf()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["stable_mastery_pattern"]);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.5, profile!.StableMasteryRate, precision: 10);
        }

        [Fact]
        public async Task ConfusionRate_ConfusionPattern_Updates()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["confusion_pattern"]);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.5, profile!.ConfusionRate, precision: 10);
        }

        [Fact]
        public async Task ConfusionRate_GoalConfusionAlias_Counts()
        {
            // "goal-confusion" is the alternative label name for the confusion pattern.
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["goal-confusion"]);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(1.0, profile!.ConfusionRate, precision: 10);
        }

        [Fact]
        public async Task HintDependenceRate_HintDependencyAlias_Counts()
        {
            // "hint-dependency" is the alternative label name.
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["hint-dependency"]);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.5, profile!.HintDependenceRate, precision: 10);
        }

        [Fact]
        public async Task HintDependenceRate_BothAliases_CountOncePerWindow()
        {
            // Both labels present in the same window — rate should count as 1 that window.
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(),
                ["hint_dependence_pattern", "hint-dependency"]);
            await sut.UpdateProfileAsync("u1", "s1", 1, MakeProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(0.5, profile!.HintDependenceRate, precision: 10);
        }
    }

    // ── Threshold preservation ────────────────────────────────────────────────

    public class ThresholdPreservationTests
    {
        [Fact]
        public async Task ThresholdOverrides_ArePreserved_WhenProfileExists()
        {
            var repo = new InMemoryUserProfileRepository();

            // Pre-seed a profile with tuned thresholds.
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId = "u1",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                TotalWindows = 5,
                TotalSessions = 1,
                LastSessionId = "s0",
                ConfusionBlockDifficultyIncreaseThreshold = 0.03,
                HintFadeConfusionDeltaThreshold = -0.12,
                DifficultyAccelerationDelta = 0.06,
            });

            var sut = CreateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(confusion: 0.55), []);

            var profile = await repo.GetAsync("u1");
            Assert.NotNull(profile);
            Assert.Equal(0.03,  profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.12, profile.HintFadeConfusionDeltaThreshold);
            Assert.Equal(0.06,  profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task ThresholdOverrides_AllFieldsPreserved()
        {
            var repo = new InMemoryUserProfileRepository();

            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId = "u1",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                LastSessionId = "s0",
                ConfusionBlockDifficultyIncreaseThreshold          = 0.02,
                HintFadeConfusionDeltaThreshold                    = -0.12,
                HintFadeHintDependenceDeltaThreshold               = -0.12,
                HintFadeMaxCurrentConfusionScore                   = 0.22,
                HintFadeMinCurrentGoalUnderstanding                = 0.70,
                DifficultyAccelerationConfusionDeltaThreshold      = -0.15,
                DifficultyAccelerationHintDependenceDeltaThreshold = -0.15,
                DifficultyAccelerationMaxCurrentConfusionScore     = 0.18,
                DifficultyAccelerationMinCurrentGoalUnderstanding  = 0.78,
                DifficultyAccelerationDelta                        = 0.06,
            });

            var sut = CreateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(), ["stable_mastery_pattern"]);

            var updated = await repo.GetAsync("u1");
            Assert.Equal(0.02,  updated!.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.12, updated.HintFadeConfusionDeltaThreshold);
            Assert.Equal(-0.12, updated.HintFadeHintDependenceDeltaThreshold);
            Assert.Equal(0.22,  updated.HintFadeMaxCurrentConfusionScore);
            Assert.Equal(0.70,  updated.HintFadeMinCurrentGoalUnderstanding);
            Assert.Equal(-0.15, updated.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.Equal(-0.15, updated.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.Equal(0.18,  updated.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.Equal(0.78,  updated.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.Equal(0.06,  updated.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task NullThresholds_RemainNull_AfterUpdate()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            // No thresholds are set — all should stay null.
            await sut.UpdateProfileAsync("u1", "s1", 0, MakeProfile(confusion: 0.4), []);

            var profile = await repo.GetAsync("u1");
            Assert.Null(profile!.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(profile.HintFadeConfusionDeltaThreshold);
            Assert.Null(profile.DifficultyAccelerationDelta);
        }
    }

    // ── Null / missing data safety ────────────────────────────────────────────

    public class SafetyTests
    {
        [Fact]
        public async Task NullBehaviorScores_DoesNotThrow_UsesZeros()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            var profile = new BehaviorProfileDocument
            {
                UserId = "u1",
                SessionId = "s1",
                WindowIndex = 0,
                BehaviorScores = null,  // deliberately null
                DimensionScores = new Dictionary<string, DimensionScore>()
            };

            await sut.UpdateProfileAsync("u1", "s1", 0, profile, []);

            var result = await repo.GetAsync("u1");
            Assert.NotNull(result);
            Assert.Equal(0.0, result.AvgConfusionScore);
        }

        [Fact]
        public async Task MissingDimensionScores_DoesNotThrow_UsesZeros()
        {
            var repo = new InMemoryUserProfileRepository();
            var sut = CreateService(repo);

            // DimensionScores dictionary is empty — no keys present.
            var profile = new BehaviorProfileDocument
            {
                UserId = "u1",
                SessionId = "s1",
                WindowIndex = 0,
                BehaviorScores = new BehaviorScores { ConfusionScore = 0.5 },
                DimensionScores = new Dictionary<string, DimensionScore>() // all missing
            };

            await sut.UpdateProfileAsync("u1", "s1", 0, profile, []);

            var result = await repo.GetAsync("u1");
            Assert.NotNull(result);
            Assert.Equal(0.5, result.AvgConfusionScore);
            Assert.Equal(0.0, result.AvgGoalUnderstanding);
            Assert.Equal(0.0, result.AvgAttentionDetection);
        }
    }

    // ── IncrementalMean formula ───────────────────────────────────────────────

    public class IncrementalMeanTests
    {
        [Theory]
        [InlineData(0.0, 0.5, 1, 0.5)]   // first window — returns value directly
        [InlineData(0.4, 0.6, 2, 0.5)]   // two windows of 0.4 and 0.6
        [InlineData(0.5, 0.8, 3, 0.6)]   // three-window mean: (0.4+0.6+0.8)/3=0.6
        [InlineData(1.0, 0.0, 2, 0.5)]   // boundary: full swing
        public void Formula_IsCorrect(double oldMean, double newValue, int newCount, double expected)
        {
            var result = UserProfileUpdateService.IncrementalMean(oldMean, newValue, newCount);
            Assert.Equal(expected, result, precision: 10);
        }

        [Fact]
        public void Formula_NewCountOne_ReturnsValueDirectly()
        {
            // oldMean is irrelevant when newCount == 1 (first window).
            var result = UserProfileUpdateService.IncrementalMean(99.0, 0.42, 1);
            Assert.Equal(0.42, result, precision: 10);
        }
    }

    // ── ADX mapper correctness ────────────────────────────────────────────────

    public class MapperTests
    {
        [Fact]
        public void MapUserBehaviorProfile_AllFieldsPresent()
        {
            var profile = new UserBehaviorProfile
            {
                UserId        = "u1",
                CreatedAtUtc  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc  = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSessionId = "s99",
                TotalSessions = 7,
                TotalWindows  = 42,
                AvgConfusionScore      = 0.35,
                AvgHintDependenceScore = 0.28,
                StableMasteryRate      = 0.60,
                ConfusionBlockDifficultyIncreaseThreshold = 0.04,
                HintFadeConfusionDeltaThreshold = -0.10,
                DifficultyAccelerationDelta = null, // null threshold preserved
            };

            var row = AdxRowMapper.MapUserBehaviorProfile(profile);

            Assert.Equal("u1",   row.UserId);
            Assert.Equal("s99",  row.LastSessionId);
            Assert.Equal(7,      row.TotalSessions);
            Assert.Equal(42,     row.TotalWindows);
            Assert.Equal(0.35,   row.AvgConfusionScore);
            Assert.Equal(0.60,   row.StableMasteryRate);
            Assert.Equal(0.04,   row.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.10,  row.HintFadeConfusionDeltaThreshold);
            Assert.Null(row.DifficultyAccelerationDelta);
        }

        [Fact]
        public void MapUserBehaviorProfileUpdate_AllFieldsPresent()
        {
            var now = DateTime.UtcNow;

            var row = AdxRowMapper.MapUserBehaviorProfileUpdate(
                "u1", "s1", 3, now,
                confusionScore: 0.45, hintDependenceScore: 0.30, hesitationScore: 0.10,
                goalUnderstanding: 0.70, attentionDetection: 0.65,
                procedureSequencing: 0.55, selfCorrection: 0.50, taskContinuity: 0.60,
                hasStableMastery: true, hasConfusionPattern: false, hasHintDependencePattern: false);

            Assert.Equal("u1", row.UserId);
            Assert.Equal("s1", row.SessionId);
            Assert.Equal(3,    row.WindowIndex);
            Assert.Equal(now,  row.CreatedAtUtc);
            Assert.Equal(0.45, row.ConfusionScore);
            Assert.Equal(0.70, row.GoalUnderstanding);
            Assert.True(row.HasStableMastery);
            Assert.False(row.HasConfusionPattern);
        }
    }
}
