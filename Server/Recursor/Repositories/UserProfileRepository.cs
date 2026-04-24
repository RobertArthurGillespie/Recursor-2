using System.Collections.Concurrent;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

// ── User Profile Repository (Phase 6A) ───────────────────────────────────────
// Provides async get/upsert for UserBehaviorProfile records.
//
// InMemoryUserProfileRepository is the Phase 6A implementation.
// ADX migration: implement IUserProfileRepository against a UserProfiles ADX
// table and swap the registration in Program.cs. No callers change.
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserProfileRepository
{
    Task<UserBehaviorProfile?> GetAsync(string userId);
    Task UpsertAsync(UserBehaviorProfile profile);
}

public class InMemoryUserProfileRepository : IUserProfileRepository
{
    private readonly ConcurrentDictionary<string, UserBehaviorProfile> _store = new();

    public Task<UserBehaviorProfile?> GetAsync(string userId)
    {
        _store.TryGetValue(userId, out var profile);
        return Task.FromResult<UserBehaviorProfile?>(profile);
    }

    public Task UpsertAsync(UserBehaviorProfile profile)
    {
        _store[profile.UserId] = profile;
        return Task.CompletedTask;
    }
}
