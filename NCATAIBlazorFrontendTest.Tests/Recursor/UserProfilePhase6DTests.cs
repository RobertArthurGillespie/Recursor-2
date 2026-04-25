using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 6D tests for the hybrid ADX + in-memory user profile repository.
///
/// Coverage:
///   1. ADX profile found → hybrid returns ADX profile
///   2. ADX returns null → hybrid falls back to in-memory profile
///   3. ADX throws → hybrid falls back to in-memory profile
///   4. ADX null AND in-memory empty → hybrid returns null
///   5. ADX throws AND in-memory empty → hybrid returns null
///   6. ADX profile with derived threshold fields → all fields preserved in result
///   7. UpsertAsync writes to in-memory store; ADX read path is unaffected
///   8. Sanitize strips single quotes (KQL injection prevention)
/// </summary>
public class UserProfilePhase6DTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private static HybridUserProfileRepository CreateHybrid(
        IAdxUserProfileQueryService adxQuery,
        InMemoryUserProfileRepository? memoryStore = null)
        => new(
            adxQuery,
            memoryStore ?? new InMemoryUserProfileRepository(),
            NullLogger<HybridUserProfileRepository>.Instance);

    private static UserBehaviorProfile MakeProfile(
        string userId = "u1",
        int totalWindows = 15,
        double confusionBlock = 0.03,
        double? accelDelta = 0.10)
        => new()
        {
            UserId        = userId,
            TotalWindows  = totalWindows,
            TotalSessions = 3,
            LastSessionId = "s99",
            CreatedAtUtc  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc  = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),

            AvgConfusionScore      = 0.20,
            AvgHintDependenceScore = 0.15,
            AvgGoalUnderstanding   = 0.75,
            StableMasteryRate      = 0.70,
            ConfusionRate          = 0.05,
            HintDependenceRate     = 0.10,

            ConfusionBlockDifficultyIncreaseThreshold          = confusionBlock,
            HintFadeConfusionDeltaThreshold                    = -0.03,
            HintFadeHintDependenceDeltaThreshold               = -0.04,
            HintFadeMaxCurrentConfusionScore                   = 0.30,
            HintFadeMinCurrentGoalUnderstanding                = 0.60,
            DifficultyAccelerationConfusionDeltaThreshold      = -0.04,
            DifficultyAccelerationHintDependenceDeltaThreshold = -0.03,
            DifficultyAccelerationMaxCurrentConfusionScore     = 0.30,
            DifficultyAccelerationMinCurrentGoalUnderstanding  = 0.60,
            DifficultyAccelerationDelta                        = accelDelta,
        };

    // ── Fake ADX query implementations ────────────────────────────────────────

    private sealed class FakeAdxQuery : IAdxUserProfileQueryService
    {
        private readonly UserBehaviorProfile? _profile;

        public FakeAdxQuery(UserBehaviorProfile? profile) => _profile = profile;

        public Task<UserBehaviorProfile?> GetLatestProfileAsync(string userId)
            => Task.FromResult(_profile);
    }

    private sealed class ThrowingAdxQuery : IAdxUserProfileQueryService
    {
        public Task<UserBehaviorProfile?> GetLatestProfileAsync(string userId)
            => throw new InvalidOperationException("ADX unavailable in test");
    }

    private sealed class CapturingAdxQuery : IAdxUserProfileQueryService
    {
        public string? LastUserId { get; private set; }

        public Task<UserBehaviorProfile?> GetLatestProfileAsync(string userId)
        {
            LastUserId = userId;
            return Task.FromResult<UserBehaviorProfile?>(null);
        }
    }

    // ── 1. ADX profile found ──────────────────────────────────────────────────

    public class AdxFoundTests
    {
        [Fact]
        public async Task GetAsync_ReturnsAdxProfile_WhenAdxHasProfile()
        {
            var adxProfile = MakeProfile(userId: "u1", totalWindows: 20);
            var repo = CreateHybrid(new FakeAdxQuery(adxProfile));

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal("u1", result.UserId);
            Assert.Equal(20, result.TotalWindows);
        }

        [Fact]
        public async Task GetAsync_PrefersAdxOver_InMemory_WhenBothExist()
        {
            var adxProfile    = MakeProfile(totalWindows: 50);
            var memoryProfile = MakeProfile(totalWindows: 10);

            var memory = new InMemoryUserProfileRepository();
            await memory.UpsertAsync(memoryProfile);

            var repo = CreateHybrid(new FakeAdxQuery(adxProfile), memory);

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal(50, result.TotalWindows);  // ADX wins
        }
    }

    // ── 2. ADX null — fall back to in-memory ─────────────────────────────────

    public class AdxMissTests
    {
        [Fact]
        public async Task GetAsync_ReturnsMemoryProfile_WhenAdxReturnsNull()
        {
            var memoryProfile = MakeProfile(userId: "u1", totalWindows: 8);
            var memory = new InMemoryUserProfileRepository();
            await memory.UpsertAsync(memoryProfile);

            var repo = CreateHybrid(new FakeAdxQuery(null), memory);

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal(8, result.TotalWindows);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenAdxNullAndMemoryEmpty()
        {
            var repo = CreateHybrid(new FakeAdxQuery(null));

            var result = await repo.GetAsync("u1");

            Assert.Null(result);
        }
    }

    // ── 3. ADX throws — fall back to in-memory ───────────────────────────────

    public class AdxErrorTests
    {
        [Fact]
        public async Task GetAsync_ReturnsMemoryProfile_WhenAdxThrows()
        {
            var memoryProfile = MakeProfile(userId: "u1", totalWindows: 12);
            var memory = new InMemoryUserProfileRepository();
            await memory.UpsertAsync(memoryProfile);

            var repo = CreateHybrid(new ThrowingAdxQuery(), memory);

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal(12, result.TotalWindows);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenAdxThrowsAndMemoryEmpty()
        {
            var repo = CreateHybrid(new ThrowingAdxQuery());

            var result = await repo.GetAsync("u1");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_DoesNotThrow_WhenAdxThrows()
        {
            var repo = CreateHybrid(new ThrowingAdxQuery());

            // Must not throw — the hybrid absorbs the ADX failure gracefully.
            var ex = await Record.ExceptionAsync(() => repo.GetAsync("u1"));
            Assert.Null(ex);
        }
    }

    // ── 4. Threshold field preservation ──────────────────────────────────────

    public class ThresholdPreservationTests
    {
        [Fact]
        public async Task GetAsync_AdxProfile_AllThresholdFieldsPreserved()
        {
            var adxProfile = MakeProfile(confusionBlock: 0.03, accelDelta: 0.10);
            var repo = CreateHybrid(new FakeAdxQuery(adxProfile));

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal(0.03,  result.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Equal(-0.03, result.HintFadeConfusionDeltaThreshold);
            Assert.Equal(-0.04, result.HintFadeHintDependenceDeltaThreshold);
            Assert.Equal(0.30,  result.HintFadeMaxCurrentConfusionScore);
            Assert.Equal(0.60,  result.HintFadeMinCurrentGoalUnderstanding);
            Assert.Equal(-0.04, result.DifficultyAccelerationConfusionDeltaThreshold);
            Assert.Equal(-0.03, result.DifficultyAccelerationHintDependenceDeltaThreshold);
            Assert.Equal(0.30,  result.DifficultyAccelerationMaxCurrentConfusionScore);
            Assert.Equal(0.60,  result.DifficultyAccelerationMinCurrentGoalUnderstanding);
            Assert.Equal(0.10,  result.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task GetAsync_AdxProfile_NullThresholdFields_RemainNull()
        {
            // Profiles from before Phase 6C (TotalWindows < 10) have null threshold fields.
            var adxProfile = new UserBehaviorProfile
            {
                UserId       = "u1",
                TotalWindows = 5,
                // All threshold fields are null (pre-6C profile)
            };

            var repo = CreateHybrid(new FakeAdxQuery(adxProfile));

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Null(result.ConfusionBlockDifficultyIncreaseThreshold);
            Assert.Null(result.DifficultyAccelerationDelta);
        }

        [Fact]
        public async Task GetAsync_AdxProfile_BehavioralAveragesPreserved()
        {
            var adxProfile = new UserBehaviorProfile
            {
                UserId             = "u1",
                TotalWindows       = 30,
                AvgConfusionScore  = 0.18,
                StableMasteryRate  = 0.72,
                HintDependenceRate = 0.08,
            };

            var repo = CreateHybrid(new FakeAdxQuery(adxProfile));

            var result = await repo.GetAsync("u1");

            Assert.NotNull(result);
            Assert.Equal(0.18, result.AvgConfusionScore,  precision: 10);
            Assert.Equal(0.72, result.StableMasteryRate,  precision: 10);
            Assert.Equal(0.08, result.HintDependenceRate, precision: 10);
        }
    }

    // ── 5. UpsertAsync writes to in-memory only ───────────────────────────────

    public class UpsertTests
    {
        [Fact]
        public async Task UpsertAsync_WritesToMemoryStore_NotToAdx()
        {
            var memory = new InMemoryUserProfileRepository();
            var capturing = new CapturingAdxQuery();
            var repo = CreateHybrid(capturing, memory);

            var profile = MakeProfile(totalWindows: 5);
            await repo.UpsertAsync(profile);

            // Memory store should have the profile.
            var memResult = await memory.GetAsync("u1");
            Assert.NotNull(memResult);
            Assert.Equal(5, memResult.TotalWindows);
        }

        [Fact]
        public async Task UpsertAsync_ProfileReadableViaGetAsync_AfterUpsert()
        {
            // With ADX returning null, the upserted in-memory profile should be returned.
            var memory = new InMemoryUserProfileRepository();
            var repo   = CreateHybrid(new FakeAdxQuery(null), memory);

            var profile = MakeProfile(totalWindows: 7);
            await repo.UpsertAsync(profile);

            var result = await repo.GetAsync("u1");
            Assert.NotNull(result);
            Assert.Equal(7, result.TotalWindows);
        }
    }

    // ── 6. UserId forwarding ──────────────────────────────────────────────────

    public class UserIdForwardingTests
    {
        [Fact]
        public async Task GetAsync_ForwardsUserIdToAdxQueryService()
        {
            var capturing = new CapturingAdxQuery();
            var repo = CreateHybrid(capturing);

            await repo.GetAsync("target-user");

            Assert.Equal("target-user", capturing.LastUserId);
        }

        [Fact]
        public async Task GetAsync_DifferentUsers_ReturnsCorrectProfiles()
        {
            var memory = new InMemoryUserProfileRepository();
            await memory.UpsertAsync(new UserBehaviorProfile { UserId = "u1", TotalWindows = 10 });
            await memory.UpsertAsync(new UserBehaviorProfile { UserId = "u2", TotalWindows = 20 });

            var repo = CreateHybrid(new FakeAdxQuery(null), memory);

            var u1 = await repo.GetAsync("u1");
            var u2 = await repo.GetAsync("u2");

            Assert.Equal(10, u1!.TotalWindows);
            Assert.Equal(20, u2!.TotalWindows);
        }
    }

    // ── 7. Sanitize (KQL injection prevention) ────────────────────────────────

    public class SanitizeTests
    {
        [Fact]
        public void Sanitize_RemovesSingleQuotes()
        {
            var result = AdxUserProfileQueryService.Sanitize("user'with'quotes");
            Assert.Equal("userwithquotes", result);
        }

        [Fact]
        public void Sanitize_NoQuotes_ReturnsUnchanged()
        {
            var result = AdxUserProfileQueryService.Sanitize("normal-user-id");
            Assert.Equal("normal-user-id", result);
        }

        [Fact]
        public void Sanitize_AllQuotes_ReturnsEmpty()
        {
            var result = AdxUserProfileQueryService.Sanitize("''''");
            Assert.Equal("", result);
        }

        [Fact]
        public void Sanitize_InjectionAttempt_Neutralized()
        {
            // Attempt to break out of KQL string literal.
            var malicious = "x' | union (print secret='leaked')";
            var result = AdxUserProfileQueryService.Sanitize(malicious);
            Assert.DoesNotContain("'", result);
        }
    }
}
