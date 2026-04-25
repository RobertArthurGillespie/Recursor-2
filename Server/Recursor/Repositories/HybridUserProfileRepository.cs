using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

// ── Hybrid User Profile Repository (Phase 6D) ────────────────────────────────
// Implements IUserProfileRepository by composing an ADX query service with an
// in-memory fallback store. This allows personalization to survive app restarts
// and deployments because ADX holds durable profile snapshots.
//
// Read path (GetAsync):
//   1. Query ADX for the latest profile snapshot (arg_max on UpdatedAtUtc).
//      If found, return the ADX profile (includes Phase 6C derived thresholds).
//   2. If ADX returns nothing or is unavailable, fall back to the in-memory store.
//   3. If neither has a profile, return null.
//      RecursorIngestionService then falls back to the seeded IUserThresholdRepository,
//      and finally to global RecursorPoliciesOptions defaults.
//
// Write path (UpsertAsync):
//   Writes to the in-memory store only. ADX is append-only; full profile snapshots
//   are already written by UserProfileUpdateService via IAdxIngestionService
//   (IngestUserBehaviorProfileAsync). Do not attempt ADX mutation here.
//
// Fallback chain summary:
//   ADX profile thresholds
//   → in-memory profile thresholds
//   → seeded UserPolicyThresholds (IUserThresholdRepository)
//   → global defaults (RecursorPoliciesOptions)
// ─────────────────────────────────────────────────────────────────────────────

public class HybridUserProfileRepository : IUserProfileRepository
{
    private readonly IAdxUserProfileQueryService _adxQuery;
    private readonly InMemoryUserProfileRepository _memoryStore;
    private readonly ILogger<HybridUserProfileRepository> _logger;

    public HybridUserProfileRepository(
        IAdxUserProfileQueryService adxQuery,
        InMemoryUserProfileRepository memoryStore,
        ILogger<HybridUserProfileRepository> logger)
    {
        _adxQuery    = adxQuery;
        _memoryStore = memoryStore;
        _logger      = logger;
    }

    public async Task<UserBehaviorProfile?> GetAsync(string userId)
    {
        // ── Step 1: try ADX ───────────────────────────────────────────────────
        UserBehaviorProfile? adxProfile = null;
        try
        {
            adxProfile = await _adxQuery.GetLatestProfileAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ADX profile lookup failed for userId '{UserId}' — using in-memory fallback.",
                userId);
        }

        if (adxProfile is not null)
        {
            _logger.LogInformation(
                "Latest ADX user profile loaded for userId '{UserId}' " +
                "(TotalWindows={TotalWindows}, DerivedThresholds={HasThresholds}).",
                userId,
                adxProfile.TotalWindows,
                adxProfile.ConfusionBlockDifficultyIncreaseThreshold.HasValue);
            return adxProfile;
        }

        _logger.LogInformation(
            "No ADX profile found for userId '{UserId}' — trying in-memory fallback.",
            userId);

        // ── Step 2: fall back to in-memory ────────────────────────────────────
        var memoryProfile = await _memoryStore.GetAsync(userId);
        if (memoryProfile is not null)
        {
            _logger.LogInformation(
                "In-memory profile fallback found for userId '{UserId}' (TotalWindows={TotalWindows}).",
                userId,
                memoryProfile.TotalWindows);
            return memoryProfile;
        }

        // ── Step 3: no profile in either store ───────────────────────────────
        _logger.LogDebug(
            "No profile found for userId '{UserId}' in ADX or in-memory store.",
            userId);
        return null;
    }

    public Task UpsertAsync(UserBehaviorProfile profile)
    {
        // ADX is append-only. Snapshots are appended by UserProfileUpdateService via
        // IAdxIngestionService — do not attempt ADX mutation here.
        // The in-memory store holds the live state for the current process lifetime.
        // On restart, GetAsync re-seeds from ADX.
        return _memoryStore.UpsertAsync(profile);
    }
}
