using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IRecursorSessionService
{
    Task<StartSessionResult> StartSessionAsync(StartSessionRequest request);
    Task<ProcessBatchResult> ProcessBatchAsync(RawEventBatch batch);
    Task EndSessionAsync(string sessionId);
}

public class StartSessionRequest
{
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
}

public class StartSessionResult
{
    public bool Success { get; init; }
    public string? SessionId { get; init; }
    public string? Error { get; init; }
}

public class ProcessBatchResult
{
    public bool Success { get; init; }
    public bool AdaptationProduced { get; init; }
    public List<ParameterChange> ParameterChanges { get; init; } = [];
    public string? ReasoningSummary { get; init; }
    public List<string> HypothesisLabels { get; set; } = new();
    public GptExplanationResult? Explanation { get; init; }
    public string? Error { get; init; }
}

public class RecursorSessionService : IRecursorSessionService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ISimCatalogRepository _simCatalog;
    private readonly IRecursorIngestionService _ingestionService;
    private readonly ILogger<RecursorSessionService> _logger;

    public RecursorSessionService(
        ISessionRepository sessionRepository,
        ISimCatalogRepository simCatalog,
        IRecursorIngestionService ingestionService,
        ILogger<RecursorSessionService> logger)
    {
        _sessionRepository = sessionRepository;
        _simCatalog = simCatalog;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    // Step 1–2: Create session.
    public Task<StartSessionResult> StartSessionAsync(StartSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SimId))
            return Task.FromResult(new StartSessionResult { Success = false, Error = "SimId is required." });

        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var session = new SessionDocument
        {
            Id = sessionId,
            SessionId = sessionId,
            UserId = request.UserId,
            SimId = request.SimId,
            SimVersion = request.SimVersion,
            ScenarioId = request.ScenarioId,
            Status = "active",
            StartedAtUtc = now,
            LastSeenAtUtc = now
        };

        _sessionRepository.Add(session);
        _logger.LogInformation("Session {SessionId} started for sim {SimId}.", sessionId, request.SimId);

        return Task.FromResult(new StartSessionResult { Success = true, SessionId = sessionId });
    }

    // Steps 4–15: Validate then run ingestion pipeline.
    public async Task<ProcessBatchResult> ProcessBatchAsync(RawEventBatch batch)
    {
        // Step 4: Validate active session.
        var session = _sessionRepository.Get(batch.SessionId);
        if (session is null)
            return new ProcessBatchResult { Success = false, Error = $"Session {batch.SessionId} not found." };

        if (session.Status != "active")
            return new ProcessBatchResult { Success = false, Error = $"Session {batch.SessionId} is not active (status: {session.Status})." };

        var result = await _ingestionService.ProcessBatchAsync(session, batch);

        return new ProcessBatchResult
        {
            Success = true,
            AdaptationProduced = result.AdaptationProduced,
            ParameterChanges = result.ParameterChanges,
            HypothesisLabels = result.HypothesisLabels,
            ReasoningSummary = result.ReasoningSummary,
            Explanation = result.Explanation
        };
    }

    public Task EndSessionAsync(string sessionId)
    {
        var session = _sessionRepository.Get(sessionId);
        if (session is null)
        {
            _logger.LogWarning("EndSession called for unknown session {SessionId}.", sessionId);
            return Task.CompletedTask;
        }

        session.Status = "ended";
        session.LastSeenAtUtc = DateTime.UtcNow;
        _sessionRepository.Update(session);
        _logger.LogInformation("Session {SessionId} ended.", sessionId);

        return Task.CompletedTask;
    }
}
