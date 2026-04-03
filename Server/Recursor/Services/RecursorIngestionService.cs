using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using System.Linq;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IRecursorIngestionService
{
    // Runs pipeline steps 4–15 for an active session.
    // Returns the adaptation result (parameter changes) or null if no adaptation fired.
    Task<IngestionResult> ProcessBatchAsync(SessionDocument session, RawEventBatch batch);
}

public class IngestionResult
{
    public bool AdaptationProduced { get; init; }
    public List<ParameterChange> ParameterChanges { get; init; } = [];
    public string? ReasoningSummary { get; init; }
    public string? AdaptationId { get; init; }

    public List<string> HypothesisLabels { get; init; } = new ();
}

public class RecursorIngestionService : IRecursorIngestionService
{
    private readonly IAdxIngestionService _adxIngestion;
    private readonly IFeatureExtractionService _featureExtraction;
    private readonly IBehaviorInterpreter _behaviorInterpreter;
    private readonly IAdaptationPolicyService _adaptationPolicy;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISimCatalogRepository _simCatalog;
    private readonly ILogger<RecursorIngestionService> _logger;

    public RecursorIngestionService(
        IAdxIngestionService adxIngestion,
        IFeatureExtractionService featureExtraction,
        IBehaviorInterpreter behaviorInterpreter,
        IAdaptationPolicyService adaptationPolicy,
        ISessionRepository sessionRepository,
        ISimCatalogRepository simCatalog,
        ILogger<RecursorIngestionService> logger)
    {
        _adxIngestion = adxIngestion;
        _featureExtraction = featureExtraction;
        _behaviorInterpreter = behaviorInterpreter;
        _adaptationPolicy = adaptationPolicy;
        _sessionRepository = sessionRepository;
        _simCatalog = simCatalog;
        _logger = logger;
    }

    public async Task<IngestionResult> ProcessBatchAsync(SessionDocument session, RawEventBatch batch)
    {
        // Step 5: Ingest raw events into ADX.
        var rawEventRows = AdxRowMapper.MapRawEvents(batch).ToList();
        await _adxIngestion.IngestRawEventsAsync(rawEventRows);
        _logger.LogInformation("Ingested {Count} raw events for session {SessionId}.", rawEventRows.Count, session.SessionId);

        // Step 6: Update in-memory session counters.
        session.EventCount += batch.Events.Count;
        session.EventsSinceLastWindow += batch.Events.Count;
        session.BatchCount += 1;
        session.LastSeenAtUtc = DateTime.UtcNow;
        _sessionRepository.Update(session);

        // Step 7: Attempt to build a feature window.
        var featureWindow = _featureExtraction.TryExtractWindow(session, batch);
        if (featureWindow is null)
        {
            _logger.LogInformation("No feature window produced for session {SessionId} batch {Batch}.", session.SessionId, session.BatchCount);
            return new IngestionResult { AdaptationProduced = false };
        }

        // Step 8: Ingest feature window into ADX.
        await _adxIngestion.IngestFeatureWindowAsync(AdxRowMapper.MapFeatureWindow(featureWindow));
        session.LatestFeatureWindowId = featureWindow.Id;
        session.EventsSinceLastWindow = 0; // reset accumulation after window is produced
        _sessionRepository.Update(session);
        _logger.LogInformation("Feature window {WindowIndex} produced for session {SessionId}.", featureWindow.WindowIndex, session.SessionId);

        // Step 9: Build behavior profile.
        var behaviorProfile = _behaviorInterpreter.BuildBehaviorProfile(featureWindow);

        // Step 10: Ingest behavior profile into ADX.
        await _adxIngestion.IngestBehaviorProfileAsync(AdxRowMapper.MapBehaviorProfile(behaviorProfile));
        session.LatestBehaviorProfileId = behaviorProfile.Id;
        _sessionRepository.Update(session);

        // Step 11: Build hypothesis set.
        var hypothesisSet = _behaviorInterpreter.BuildHypothesisSet(behaviorProfile);
        var hypothesisLabels = hypothesisSet.Hypotheses
    .Select(h => h.Label)
    .ToList();

        // Step 12: Ingest hypothesis set into ADX.
        await _adxIngestion.IngestHypothesisSetAsync(AdxRowMapper.MapHypothesisSet(hypothesisSet));
        session.LatestHypothesisSetId = hypothesisSet.Id;
        _sessionRepository.Update(session);

        // Step 13: Apply adaptation policy.
        var catalog = _simCatalog.Get(session.SimId);
        if (catalog is null)
        {
            _logger.LogWarning("SimId {SimId} not found in catalog. Skipping adaptation.", session.SimId);
            return new IngestionResult
            {
                AdaptationProduced = false,
                HypothesisLabels = hypothesisLabels
            };
        }

        var adaptation = _adaptationPolicy.ApplyPolicy(session, catalog, hypothesisSet);
        if (adaptation is null)
        {
            _logger.LogInformation("No adaptation produced for session {SessionId}.", session.SessionId);
            return new IngestionResult
            {
                AdaptationProduced = false,
                HypothesisLabels = hypothesisLabels
            };
        }

        // Step 14: Ingest adaptation decision into ADX.
        await _adxIngestion.IngestAdaptationDecisionAsync(AdxRowMapper.MapAdaptationDecision(adaptation));

        // Persist the applied adaptive state onto the session so future policy
        // decisions can see the current effective hint mode / difficulty profile.
        foreach (var change in adaptation.ParameterChanges)
        {
            if (change.Value is not null)
            {
                session.CurrentDifficultyProfile[change.Parameter] = change.Value.ToString() ?? "";
            }
        }

        session.LatestAdaptationId = adaptation.Id;
        session.LastSeenAtUtc = DateTime.UtcNow;

        _sessionRepository.Update(session);

        _logger.LogInformation(
            "Adaptation produced for session {SessionId}: {Summary}. CurrentDifficultyProfile hintMode={HintMode}",
            session.SessionId,
            adaptation.ReasoningSummary,
            session.CurrentDifficultyProfile.TryGetValue("hintMode", out var hintMode) ? hintMode : "(none)");

        // Step 15: Return bounded parameter changes.
        return new IngestionResult
        {
            AdaptationProduced = true,
            ParameterChanges = adaptation.ParameterChanges,
            HypothesisLabels = hypothesisLabels,
            ReasoningSummary = adaptation.ReasoningSummary,
            AdaptationId = adaptation.Id
        };
    }
}
