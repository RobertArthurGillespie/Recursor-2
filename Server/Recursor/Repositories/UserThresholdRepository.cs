using System.Collections.Concurrent;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

public interface IUserThresholdRepository
{
    UserPolicyThresholds? Get(string userId);
    void Upsert(UserPolicyThresholds thresholds);
}

public class InMemoryUserThresholdRepository : IUserThresholdRepository
{
    private readonly ConcurrentDictionary<string, UserPolicyThresholds> _store = new();

    public UserPolicyThresholds? Get(string userId)
        => _store.TryGetValue(userId, out var t) ? t : null;

    public void Upsert(UserPolicyThresholds thresholds)
        => _store[thresholds.UserId] = thresholds;
}
