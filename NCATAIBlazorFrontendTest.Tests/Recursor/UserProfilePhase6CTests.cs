using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 6C tests for UserThresholdDerivationService.
///
/// Coverage:
///   1. TotalWindows &lt; 10 — thresholds remain unchanged
///   2. TotalWindows >= 10 — all threshold fields are populated
///   3. High-confusion user gets lower confusion-block threshold (0.03)
///   4. Stable strong user gets higher confusion-block threshold (0.08)
///      and stronger difficulty acceleration delta (0.10)
///   5. High hint-dependence user gets stricter hint-fade hint-dependence delta (-0.08)
///   6. All derived values are within declared bounds
///   7. Derivation preserves behavioral averages and window counts (no side effects)
///   8. Integration: derivation fires through UserProfileUpdateService at TotalWindows == 10
/// </summary>
public class UserProfilePhase6CTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private static UserThresholdDerivationService CreateDerivationService()
        => new(NullLogger<UserThresholdDerivationService>.Instance);

    private static UserBehaviorProfile MatureProfile(
        double confusionRate      = 0.10,
        double stableMasteryRate  = 0.50,
        double hintDependenceRate = 0.10,
        double avgConfusion       = 0.25,
        double avgGoal            = 0.65,
        int    totalWindows       = 10)
        => new()
        {
            UserId            = "u1",
            TotalWindows      = totalWindows,
            ConfusionRate     = confusionRate,
            StableMasteryRate = stableMasteryRate,
            HintDependenceRate = hintDependenceRate,
            AvgConfusionScore = avgConfusion,
            AvgGoalUnderstanding = avgGoal,
        };

    // No-op ADX stub so unit tests do not touch ADX.
    private sealed class NoOpAdxIngestion : IAdxIngestionService
    {
        public Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows)                   => Task.CompletedTask;
        public Task IngestFeatureWindowAsync(FeatureWindowRow row)                         => Task.CompletedTask;
        public Task IngestBehaviorProfileAsync(BehaviorProfileRow row)                     => Task.CompletedTask;
        public Task IngestHypothesisSetAsync(HypothesisSetRow row)                         => Task.CompletedTask;
        public Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row)               => Task.CompletedTask;
        public Task IngestBehaviorStateTrainingRowAsync(BehaviorStateTrainingRow row)      => Task.CompletedTask;
        public Task IngestUserBehaviorProfileAsync(UserBehaviorProfileRow row)             => Task.CompletedTask;
        public Task IngestUserBehaviorProfileUpdateAsync(UserBehaviorProfileUpdateRow row) => Task.CompletedTask;
        public Task IngestAdaptationEffectivenessAsync(AdaptationEffectivenessRow row)    => Task.CompletedTask;
        public Task IngestTemporalEmbeddingAsync(TemporalEmbeddingRow row)               => Task.CompletedTask;
        public Task IngestTemporalPredictionTargetAsync(TemporalPredictionTargetRow row) => Task.CompletedTask;
    }

    private static UserProfileUpdateService CreateUpdateService(IUserProfileRepository repo)
        => new(repo,
               new NoOpAdxIngestion(),
               new UserThresholdDerivationService(NullLogger<UserThresholdDerivationService>.Instance),
               NullLogger<UserProfileUpdateService>.Instance);

    private static BehaviorProfileDocument MakeBehaviorProfile(
        double confusion = 0.0, double hintDependence = 0.0, double goalUnderstanding = 0.0)
        => new()
        {
            UserId = "u1",
            SessionId = "s1",
            WindowIndex = 0,
            BehaviorScores = new BehaviorScores
            {
                ConfusionScore      = confusion,
                HintDependenceScore = hintDependence,
            },
            DimensionScores = new Dictionary<string, DimensionScore>
            {
                ["goalUnderstanding"] = new() { Score = goalUnderstanding },
            }
        };

    // ── 1. Minimum data guard ─────────────────────────────────────────────────

    public class MinimumDataGuardTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(9)]
        public void BelowThreshold_ThresholdFieldsRemainNull(int totalWindows)
        {
            var sut     = CreateDerivationService();
            var profile = MatureProfile(totalWindows: totalWindows);

            sut.DeriveThresholds(profile);

            Assert.Null(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(profile.HintFadeConfusionDeltaThreshold);
            Assert.Null(profile.HintFadeHintDependenceDeltaThreshold);
            Assert.Null(profile.HintFadeMaxCurrentConfusionScore);
            Assert.Null(profile.HintFadeMinCurrentGoalUnderstanding);
            Assert.Null(profile.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.Null(profile.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.Null(profile.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.Null(profile.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.Null(profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public void BelowThreshold_ExistingThresholdFieldsArePreserved()
        {
            // Pre-set threshold fields on a profile with 9 windows.
            // Derivation must not clear or change them.
            var sut = CreateDerivationService();
            var profile = MatureProfile(totalWindows: 9);
            profile.ConfusionBlockDifficultyIncreaseThreshold = 0.03;
            profile.DifficultyAccelerationDelta = 0.06;

            sut.DeriveThresholds(profile);

            Assert.Equal(0.03, profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(0.06, profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public void AtThreshold_AllFieldsArePopulated()
        {
            var sut     = CreateDerivationService();
            var profile = MatureProfile(totalWindows: 10);

            sut.DeriveThresholds(profile);

            Assert.NotNull(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.NotNull(profile.HintFadeConfusionDeltaThreshold);
            Assert.NotNull(profile.HintFadeHintDependenceDeltaThreshold);
            Assert.NotNull(profile.HintFadeMaxCurrentConfusionScore);
            Assert.NotNull(profile.HintFadeMinCurrentGoalUnderstanding);
            Assert.NotNull(profile.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.NotNull(profile.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.NotNull(profile.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.NotNull(profile.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.NotNull(profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public void AboveThreshold_AllFieldsArePopulated()
        {
            var sut     = CreateDerivationService();
            var profile = MatureProfile(totalWindows: 50);

            sut.DeriveThresholds(profile);

            Assert.NotNull(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.NotNull(profile.DifficultyAccelerationDelta);
        }
    }

    // ── 2. Confusion-block threshold formulas ─────────────────────────────────

    public class ConfusionBlockTests
    {
        [Fact]
        public void HighConfusionRate_GetsLowerThreshold()
        {
            // ConfusionRate >= 0.35 → threshold drops to 0.03 (more protective).
            var sut     = CreateDerivationService();
            var profile = MatureProfile(confusionRate: 0.40, stableMasteryRate: 0.30);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.03, profile.ConfusionBlockDifficultyIncreaseThreshold);
        }

        [Fact]
        public void StableStrongUser_GetsHigherThreshold()
        {
            // StableMasteryRate >= 0.60 AND AvgConfusionScore <= 0.20 → threshold rises to 0.08.
            var sut = CreateDerivationService();
            var profile = MatureProfile(
                confusionRate: 0.05, stableMasteryRate: 0.65, avgConfusion: 0.15);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.08, profile.ConfusionBlockDifficultyIncreaseThreshold);
        }

        [Fact]
        public void AverageUser_GetsBaseThreshold()
        {
            // Neither high-confusion nor stable strong → base = 0.05.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(confusionRate: 0.15, stableMasteryRate: 0.40);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.05, profile.ConfusionBlockDifficultyIncreaseThreshold);
        }

        [Fact]
        public void HighConfusionRate_TakesPriorityOverStableMastery()
        {
            // ConfusionRate >= 0.35 wins even when stableMasteryRate >= 0.60 (defensive).
            var sut = CreateDerivationService();
            var profile = MatureProfile(
                confusionRate: 0.40, stableMasteryRate: 0.70, avgConfusion: 0.10);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.03, profile.ConfusionBlockDifficultyIncreaseThreshold);
        }
    }

    // ── 3. Hint-fade threshold formulas ───────────────────────────────────────

    public class HintFadeThresholdTests
    {
        [Fact]
        public void StableUser_GetsRelaxedConfusionDelta()
        {
            // StableMasteryRate >= 0.60 → confusion delta threshold relaxed to -0.03.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.70);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.03, profile.HintFadeConfusionDeltaThreshold);
        }

        [Fact]
        public void WeakerUser_GetsStricterConfusionDelta()
        {
            // StableMasteryRate < 0.60 → confusion delta threshold stays at -0.06.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.40);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.06, profile.HintFadeConfusionDeltaThreshold);
        }

        [Fact]
        public void HighHintDependenceUser_GetsStricterHintDependenceDelta()
        {
            // HintDependenceRate >= 0.35 → hint-fade hint-dependence delta = -0.08.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(hintDependenceRate: 0.40);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.08, profile.HintFadeHintDependenceDeltaThreshold);
        }

        [Fact]
        public void LowHintDependenceUser_GetsRelaxedHintDependenceDelta()
        {
            // HintDependenceRate < 0.35 → hint-fade hint-dependence delta = -0.04.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(hintDependenceRate: 0.10);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.04, profile.HintFadeHintDependenceDeltaThreshold);
        }

        [Fact]
        public void LowConfusionUser_GetsHigherMaxConfusionGate()
        {
            // AvgConfusionScore <= 0.20 → max confusion gate = 0.30.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(avgConfusion: 0.15);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.30, profile.HintFadeMaxCurrentConfusionScore);
        }

        [Fact]
        public void HighConfusionUser_GetsLowerMaxConfusionGate()
        {
            // AvgConfusionScore > 0.20 → max confusion gate = 0.22.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(avgConfusion: 0.35);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.22, profile.HintFadeMaxCurrentConfusionScore);
        }

        [Fact]
        public void HighGoalUnderstandingUser_GetsLowerMinGoalFloor()
        {
            // AvgGoalUnderstanding >= 0.70 → min goal understanding = 0.60.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(avgGoal: 0.75);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.60, profile.HintFadeMinCurrentGoalUnderstanding);
        }

        [Fact]
        public void LowGoalUnderstandingUser_GetsHigherMinGoalFloor()
        {
            // AvgGoalUnderstanding < 0.70 → min goal understanding = 0.70.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(avgGoal: 0.55);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.70, profile.HintFadeMinCurrentGoalUnderstanding);
        }
    }

    // ── 4. Difficulty acceleration threshold formulas ─────────────────────────

    public class DifficultyAccelerationTests
    {
        [Fact]
        public void StableUser_GetsRelaxedAccelConfusionDelta()
        {
            // StableMasteryRate >= 0.60 → accel confusion delta = -0.04.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.65);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.04, profile.DifficultyAccelerationConfusionDeltaThreshold);
        }

        [Fact]
        public void WeakerUser_GetsStricterAccelConfusionDelta()
        {
            // StableMasteryRate < 0.60 → accel confusion delta = -0.08.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.30);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.08, profile.DifficultyAccelerationConfusionDeltaThreshold);
        }

        [Fact]
        public void LowHintDependenceUser_GetsRelaxedAccelHintDependenceDelta()
        {
            // HintDependenceRate <= 0.20 → accel hint-dependence delta = -0.03.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(hintDependenceRate: 0.10);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.03, profile.DifficultyAccelerationHintDependenceDeltaThreshold);
        }

        [Fact]
        public void HighHintDependenceUser_GetsStricterAccelHintDependenceDelta()
        {
            // HintDependenceRate > 0.20 → accel hint-dependence delta = -0.06.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(hintDependenceRate: 0.35);

            sut.DeriveThresholds(profile);

            Assert.Equal(-0.06, profile.DifficultyAccelerationHintDependenceDeltaThreshold);
        }

        [Fact]
        public void StrongStableUser_GetsHigherAccelDelta()
        {
            // StableMasteryRate >= 0.70 AND AvgConfusionScore <= 0.20 → accel delta = 0.10.
            var sut = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.75, avgConfusion: 0.15);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.10, profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public void WeakerUser_GetsBaseAccelDelta()
        {
            // Neither condition met → accel delta = 0.08.
            var sut     = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.40, avgConfusion: 0.30);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.08, profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public void StableButHighConfusion_GetsBaseAccelDelta()
        {
            // StableMasteryRate >= 0.70 but AvgConfusionScore > 0.20 → accel delta = 0.08.
            var sut = CreateDerivationService();
            var profile = MatureProfile(stableMasteryRate: 0.75, avgConfusion: 0.25);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.08, profile.DifficultyAccelerationDelta);
        }
    }

    // ── 5. Bounds enforcement ─────────────────────────────────────────────────

    public class BoundsTests
    {
        private static readonly double[] ConfusionRates     = [0.0, 0.35, 1.0];
        private static readonly double[] StableMasteryRates = [0.0, 0.60, 0.70, 1.0];
        private static readonly double[] HintDependenceRates = [0.0, 0.20, 0.35, 1.0];
        private static readonly double[] AvgConfusions      = [0.0, 0.20, 0.50, 1.0];
        private static readonly double[] AvgGoals           = [0.0, 0.70, 1.0];

        [Fact]
        public void AllDerivedValues_AreWithinDeclaredBounds()
        {
            var sut = CreateDerivationService();

            // Exhaust the discrete branch combinations to verify bounds hold for all paths.
            foreach (var cr  in ConfusionRates)
            foreach (var smr in StableMasteryRates)
            foreach (var hdr in HintDependenceRates)
            foreach (var ac  in AvgConfusions)
            foreach (var ag  in AvgGoals)
            {
                var profile = MatureProfile(
                    confusionRate:      cr,
                    stableMasteryRate:  smr,
                    hintDependenceRate: hdr,
                    avgConfusion:       ac,
                    avgGoal:            ag,
                    totalWindows:       10);

                sut.DeriveThresholds(profile);

                double cb = profile.ConfusionBlockDifficultyIncreaseThreshold!.Value;
                Assert.InRange(cb, 0.02, 0.15);

                double hfcd = profile.HintFadeConfusionDeltaThreshold!.Value;
                Assert.InRange(hfcd, -0.15, -0.02);

                double hfhd = profile.HintFadeHintDependenceDeltaThreshold!.Value;
                Assert.InRange(hfhd, -0.15, -0.02);

                double hfmc = profile.HintFadeMaxCurrentConfusionScore!.Value;
                Assert.InRange(hfmc, 0.15, 0.45);

                double hfmg = profile.HintFadeMinCurrentGoalUnderstanding!.Value;
                Assert.InRange(hfmg, 0.50, 0.85);

                double aacd = profile.DifficultyAccelerationConfusionDeltaThreshold!.Value;
                Assert.InRange(aacd, -0.15, -0.02);

                double aahd = profile.DifficultyAccelerationHintDependenceDeltaThreshold!.Value;
                Assert.InRange(aahd, -0.15, -0.02);

                double aamc = profile.DifficultyAccelerationMaxCurrentConfusionScore!.Value;
                Assert.InRange(aamc, 0.15, 0.45);

                double aamg = profile.DifficultyAccelerationMinCurrentGoalUnderstanding!.Value;
                Assert.InRange(aamg, 0.50, 0.85);

                double ad = profile.DifficultyAccelerationDelta!.Value;
                Assert.InRange(ad, 0.05, 0.10);
            }
        }
    }

    // ── 6. Preservation: derivation must not touch behavioral fields ───────────

    public class PreservationTests
    {
        [Fact]
        public void DeriveThresholds_DoesNotAffectBehavioralAverages()
        {
            var sut = CreateDerivationService();
            var profile = MatureProfile(avgConfusion: 0.35, avgGoal: 0.72);
            profile.AvgHintDependenceScore  = 0.28;
            profile.AvgHesitationScore      = 0.12;
            profile.AvgAttentionDetection   = 0.80;
            profile.AvgProcedureSequencing  = 0.75;
            profile.AvgSelfCorrection       = 0.65;
            profile.AvgTaskContinuity       = 0.70;

            sut.DeriveThresholds(profile);

            Assert.Equal(0.35, profile.AvgConfusionScore,        precision: 10);
            Assert.Equal(0.28, profile.AvgHintDependenceScore,   precision: 10);
            Assert.Equal(0.12, profile.AvgHesitationScore,       precision: 10);
            Assert.Equal(0.72, profile.AvgGoalUnderstanding,     precision: 10);
            Assert.Equal(0.80, profile.AvgAttentionDetection,    precision: 10);
            Assert.Equal(0.75, profile.AvgProcedureSequencing,   precision: 10);
            Assert.Equal(0.65, profile.AvgSelfCorrection,        precision: 10);
            Assert.Equal(0.70, profile.AvgTaskContinuity,        precision: 10);
        }

        [Fact]
        public void DeriveThresholds_DoesNotAffectWindowAndSessionCounts()
        {
            var sut = CreateDerivationService();
            var profile = MatureProfile(totalWindows: 25);
            profile.TotalSessions  = 4;
            profile.LastSessionId  = "s-last";

            sut.DeriveThresholds(profile);

            Assert.Equal(25,       profile.TotalWindows);
            Assert.Equal(4,        profile.TotalSessions);
            Assert.Equal("s-last", profile.LastSessionId);
        }

        [Fact]
        public void DeriveThresholds_DoesNotAffectRates()
        {
            var sut = CreateDerivationService();
            var profile = MatureProfile(
                confusionRate:     0.20,
                stableMasteryRate: 0.55,
                hintDependenceRate: 0.15);

            sut.DeriveThresholds(profile);

            Assert.Equal(0.20, profile.ConfusionRate,      precision: 10);
            Assert.Equal(0.55, profile.StableMasteryRate,  precision: 10);
            Assert.Equal(0.15, profile.HintDependenceRate, precision: 10);
        }
    }

    // ── 7. Integration: derivation fires through UserProfileUpdateService ──────

    public class IntegrationTests
    {
        [Fact]
        public async Task UpdateProfileAsync_BelowMinimumWindows_ThresholdsNotSet()
        {
            // Start with 8 windows already in the profile; call once → 9 total.
            // Still below 10 → thresholds should remain null.
            var repo = new InMemoryUserProfileRepository();
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId       = "u1",
                TotalWindows = 8,
                LastSessionId = "s0",
            });

            var sut = CreateUpdateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0, MakeBehaviorProfile(), []);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(9, profile!.TotalWindows);
            Assert.Null(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task UpdateProfileAsync_AtMinimumWindows_ThresholdsDerived()
        {
            // Start with 9 windows; call once → 10 total → derivation fires.
            var repo = new InMemoryUserProfileRepository();
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId        = "u1",
                TotalWindows  = 9,
                LastSessionId = "s0",
                // Stable average so stable mastery path applies
                StableMasteryRate  = 0.65,
                ConfusionRate      = 0.05,
                AvgConfusionScore  = 0.18,
                AvgGoalUnderstanding = 0.72,
                HintDependenceRate = 0.10,
            });

            var sut = CreateUpdateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0, MakeBehaviorProfile(), ["stable_mastery_pattern"]);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(10, profile!.TotalWindows);
            Assert.NotNull(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.NotNull(profile.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task UpdateProfileAsync_AtMinimumWindows_PreservesAverages()
        {
            // Verify that behavioral averages are intact after derivation fires.
            var repo = new InMemoryUserProfileRepository();
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId        = "u1",
                TotalWindows  = 9,
                LastSessionId = "s0",
            });

            var sut = CreateUpdateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0,
                MakeBehaviorProfile(confusion: 0.30, hintDependence: 0.20, goalUnderstanding: 0.65),
                []);

            var profile = await repo.GetAsync("u1");
            // TotalWindows = 10 so derivation fired
            Assert.Equal(10, profile!.TotalWindows);
            // Behavioral averages are still set (not cleared by derivation)
            Assert.True(profile.AvgConfusionScore > 0.0, "AvgConfusionScore should be non-zero after window with confusion=0.30");
            Assert.True(profile.AvgGoalUnderstanding > 0.0, "AvgGoalUnderstanding should be non-zero after window with goalUnderstanding=0.65");
        }

        [Fact]
        public async Task UpdateProfileAsync_HighConfusionProfile_GetsLowerConfusionBlock()
        {
            // Seed a high-confusion profile, trigger derivation at window 10.
            var repo = new InMemoryUserProfileRepository();
            await repo.UpsertAsync(new UserBehaviorProfile
            {
                UserId        = "u1",
                TotalWindows  = 9,
                LastSessionId = "s0",
                ConfusionRate = 0.40,
            });

            var sut = CreateUpdateService(repo);
            await sut.UpdateProfileAsync("u1", "s1", 0,
                MakeBehaviorProfile(confusion: 0.60),
                ["confusion_pattern"]);

            var profile = await repo.GetAsync("u1");
            Assert.Equal(10, profile!.TotalWindows);
            // After 9 confusion-pattern windows + 1 more confusion_pattern, ConfusionRate
            // will be high → confusion-block threshold should be 0.03.
            Assert.NotNull(profile.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.True(profile.ConfusionBlockDifficultyIncreaseThreshold <= 0.05,
                $"Expected confusion-block threshold <= 0.05 for a high-confusion user, got {profile.ConfusionBlockDifficultyIncreaseThreshold}");
        }
    }
}
