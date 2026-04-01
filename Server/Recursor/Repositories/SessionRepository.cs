using System.Collections.Concurrent;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

public interface ISessionRepository
{
    void Add(SessionDocument session);
    SessionDocument? Get(string sessionId);
    void Update(SessionDocument session);
    bool Exists(string sessionId);
}

public class SessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<string, SessionDocument> _store = new();

    public void Add(SessionDocument session)
        => _store[session.SessionId] = session;

    public SessionDocument? Get(string sessionId)
        => _store.TryGetValue(sessionId, out var session) ? session : null;

    public void Update(SessionDocument session)
        => _store[session.SessionId] = session;

    public bool Exists(string sessionId)
        => _store.ContainsKey(sessionId);
}
