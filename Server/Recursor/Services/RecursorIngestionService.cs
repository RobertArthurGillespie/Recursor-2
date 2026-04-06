using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Shared;
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
    public List<string> HypothesisLabels { get; init; } = new();
    public GptExplanationResult? Explanation { get; init; }
}

public class RecursorIngestionService : IRecursorIngestionService
{
    private readonly IAdxIngestionService _adxIngestion;
    private readonly IFeatureExtractionService _featureExtraction;
    private readonly IBehaviorInterpreter _behaviorInterpreter;
    private readonly IAdaptationPolicyService _adaptationPolicy;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISimCatalogRepository _simCatalog;
    private readonly IExplanationGenerationService _explanationService;
    private readonly ITrajectoryAnalysisService _trajectoryAnalysis;
    private readonly IBehaviorStateFeatureVectorBuilder _featureVectorBuilder;
    private readonly IBehaviorStatePredictionService _behaviorStatePredictionService;
    private readonly ILogger<RecursorIngestionService> _logger;

    public RecursorIngestionService(
    IAdxIngestionService adxIngestion,
    IFeatureExtractionService featureExtraction,
    IBehaviorInterpreter behaviorInterpreter,
    IAdaptationPolicyService adaptationPolicy,
    IExplanationGenerationService explanationService,
    ISessionRepository sessionRepository,
    ISimCatalogRepository simCatalog,
    ITrajectoryAnalysisService trajectoryAnalysis,
    IBehaviorStateFeatureVectorBuilder featureVectorBuilder,
    IBehaviorStatePredictionService behaviorStatePredictionService,
    ILogger<RecursorIngestionService> logger)
    {
        _adxIngestion = adxIngestion;
        _featureExtraction = featureExtraction;
        _behaviorInterpreter = behaviorInterpreter;
        _adaptationPolicy = adaptationPolicy;
        _explanationService = explanationService;
        _sessionRepository = sessionRepository;
        _simCatalog = simCatalog;
        _trajectoryAnalysis = trajectoryAnalysis;
        _featureVectorBuilder = featureVectorBuilder;
        _behaviorStatePredictionService = behaviorStatePredictionService;
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

        // Trajectory analysis reads history before the current snapshot is added.
        var trajectoryResult = _trajectoryAnalysis.Analyze(session, behaviorProfile);

        // Append snapshot for this window and trim to the most recent 5.
        var snapshot = new TrajectorySnapshot
        {
            WindowIndex = behaviorProfile.WindowIndex,
            AttentionDetection = behaviorProfile.DimensionScores.TryGetValue("attentionDetection", out var snAttn) ? snAttn.Score : 0.0,
            GoalUnderstanding = behaviorProfile.DimensionScores.TryGetValue("goalUnderstanding", out var snGoal) ? snGoal.Score : 0.0,
            ProcedureSequencing = behaviorProfile.DimensionScores.TryGetValue("procedureSequencing", out var snProc) ? snProc.Score : 0.0,
            SelfCorrection = behaviorProfile.DimensionScores.TryGetValue("selfCorrection", out var snSelf) ? snSelf.Score : 0.0,
            FeedbackResponsiveness = behaviorProfile.DimensionScores.TryGetValue("feedbackResponsiveness", out var snFb) ? snFb.Score : 0.0,
            ConfusionScore = behaviorProfile.BehaviorScores?.ConfusionScore ?? 0.0,
            HesitationScore = behaviorProfile.BehaviorScores?.HesitationScore ?? 0.0,
            ImpulsivityScore = behaviorProfile.BehaviorScores?.ImpulsivityScore ?? 0.0,
            HintDependenceScore = behaviorProfile.BehaviorScores?.HintDependenceScore ?? 0.0,
            CreatedAtUtc = DateTime.UtcNow
        };
        session.RecentSnapshots.Add(snapshot);
        if (session.RecentSnapshots.Count > 5)
        {
            session.RecentSnapshots = session.RecentSnapshots
                .Skip(session.RecentSnapshots.Count - 5)
                .ToList();
        }
        _sessionRepository.Update(session);

        // Step 10: Ingest behavior profile into ADX.
        await _adxIngestion.IngestBehaviorProfileAsync(AdxRowMapper.MapBehaviorProfile(behaviorProfile));
        session.LatestBehaviorProfileId = behaviorProfile.Id;
        _sessionRepository.Update(session);

        // Step 11: Build hypothesis set.
        var hypothesisSet = _behaviorInterpreter.BuildHypothesisSet(behaviorProfile);

        // Append trajectory hypotheses after the base hypothesis set is built.
        if (trajectoryResult.HasEnoughHistory && trajectoryResult.TrajectoryLabels.Count > 0)
        {
            foreach (var label in trajectoryResult.TrajectoryLabels)
            {
                BehavioralHypothesis? trajectoryHypothesis = label switch
                {
                    "stable_mastery_pattern" => new BehavioralHypothesis
                    {
                        Label = "stable_mastery_pattern",
                        Dimensions = new List<string> { "goalUnderstanding", "attentionDetection" },
                        Confidence = 0.80,
                        Evidence = new List<string>
                        {
                            $"GoalTrend={trajectoryResult.GoalTrend:0.00}",
                            $"AttentionTrend={trajectoryResult.AttentionTrend:0.00}",
                            $"ConfusionTrend={trajectoryResult.ConfusionTrend:0.00}",
                            $"HintDependenceTrend={trajectoryResult.HintDependenceTrend:0.00}"
                        }
                    },
                    "relapse_pattern" => new BehavioralHypothesis
                    {
                        Label = "relapse_pattern",
                        Dimensions = new List<string> { "goalUnderstanding", "attentionDetection", "feedbackResponsiveness" },
                        Confidence = 0.80,
                        Evidence = new List<string>
                        {
                            $"GoalTrend={trajectoryResult.GoalTrend:0.00}",
                            $"AttentionTrend={trajectoryResult.AttentionTrend:0.00}",
                            $"ConfusionTrend={trajectoryResult.ConfusionTrend:0.00}",
                            $"HintDependenceTrend={trajectoryResult.HintDependenceTrend:0.00}"
                        }
                    },
                    "improving_pattern" => new BehavioralHypothesis
                    {
                        Label = "improving_pattern",
                        Dimensions = new List<string> { "goalUnderstanding", "attentionDetection" },
                        Confidence = 0.70,
                        Evidence = new List<string>
                        {
                            $"GoalTrend={trajectoryResult.GoalTrend:0.00}",
                            $"AttentionTrend={trajectoryResult.AttentionTrend:0.00}",
                            $"ConfusionTrend={trajectoryResult.ConfusionTrend:0.00}"
                        }
                    },
                    "worsening_pattern" => new BehavioralHypothesis
                    {
                        Label = "worsening_pattern",
                        Dimensions = new List<string> { "goalUnderstanding", "attentionDetection" },
                        Confidence = 0.70,
                        Evidence = new List<string>
                        {
                            $"GoalTrend={trajectoryResult.GoalTrend:0.00}",
                            $"AttentionTrend={trajectoryResult.AttentionTrend:0.00}",
                            $"ConfusionTrend={trajectoryResult.ConfusionTrend:0.00}"
                        }
                    },
                    _ => null
                };

                if (trajectoryHypothesis is not null)
                    hypothesisSet.Hypotheses.Add(trajectoryHypothesis);
            }
        }

        var hypothesisLabels = hypothesisSet.Hypotheses
            .Select(h => h.Label)
            .ToList();

        // Update consecutive trajectory counters for hysteresis in the adaptation policy.
        bool hypothesisHasStableMastery = hypothesisLabels.Contains("stable_mastery_pattern");
        bool hypothesisHasRelapse = hypothesisLabels.Contains("relapse_pattern");

        if (hypothesisHasStableMastery)
        {
            session.ConsecutiveStableMasteryWindows += 1;
            session.ConsecutiveRelapseWindows = 0;
        }
        else if (hypothesisHasRelapse)
        {
            session.ConsecutiveRelapseWindows += 1;
            session.ConsecutiveStableMasteryWindows = 0;
        }
        else
        {
            session.ConsecutiveStableMasteryWindows = 0;
            session.ConsecutiveRelapseWindows = 0;
        }
        _sessionRepository.Update(session);

        // Shadow ML prediction — additive only, never blocks or changes adaptation.
        BehaviorStateFeatureVector? featureVector = null;
        BehaviorStatePrediction? shadowPrediction = null;
        try
        {
            featureVector = _featureVectorBuilder.Build(session, batch, behaviorProfile, trajectoryResult);
            shadowPrediction = await _behaviorStatePredictionService.PredictAsync(featureVector);
            if (shadowPrediction is not null)
            {
                _logger.LogInformation(
                    "Shadow ML prediction generated. SessionId={SessionId} WindowIndex={WindowIndex} ModelVersion={ModelVersion} InferenceMode={InferenceMode}",
                    session.SessionId, featureVector.WindowIndex, shadowPrediction.ModelVersion, shadowPrediction.InferenceMode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shadow ML prediction failed for session {SessionId}. Continuing pipeline.", session.SessionId);
        }

        // Training-row ingestion — additive only, never blocks or changes adaptation.
        if (featureVector is not null)
        {
            try
            {
                var trainingRow = AdxRowMapper.MapBehaviorStateTrainingRow(featureVector, hypothesisSet, shadowPrediction);
                await _adxIngestion.IngestBehaviorStateTrainingRowAsync(trainingRow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Training row ingestion failed for session {SessionId}. Continuing pipeline.", session.SessionId);
            }
        }

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
            var explanationNoAdaptation = await _explanationService.GenerateExplanationAsync(
    session,
    behaviorProfile,
    hypothesisSet,
    null);

            return new IngestionResult
            {
                AdaptationProduced = false,
                HypothesisLabels = hypothesisLabels,
                Explanation = explanationNoAdaptation
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

        var explanation = await _explanationService.GenerateExplanationAsync(
    session,
    behaviorProfile,
    hypothesisSet,
    adaptation);

        // Step 15: Return bounded parameter changes.
        return new IngestionResult
        {
            AdaptationProduced = true,
            ParameterChanges = adaptation.ParameterChanges,
            HypothesisLabels = hypothesisLabels,
            ReasoningSummary = adaptation.ReasoningSummary,
            AdaptationId = adaptation.Id,
            Explanation = explanation
        };
    }
}
