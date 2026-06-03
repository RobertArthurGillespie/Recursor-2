using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
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

    // Phase 10A: sequence-aware trajectory data — null when fewer than 2 windows available.
    public SequenceFeatureSummary? SequenceSummary { get; init; }
    public TrajectorySummary? TrajectorySummary { get; init; }
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
    private readonly IUserRelativeSignalService _userRelativeSignalService;
    private readonly IUserThresholdRepository _userThresholdRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IUserProfileUpdateService _userProfileUpdateService;
    private readonly IMultiSignalGuardrailService _multiSignalGuardrail;
    private readonly IPhase8AGuardrailModifierService _phase8aModifier;
    private readonly IAdaptationEffectivenessService _adaptationEffectiveness;
    private readonly IPolicyReliabilityWeightingService _phase8eReliability;
    private readonly ISequenceFeatureExtractor _sequenceFeatureExtractor;
    private readonly ITemporalEmbeddingService _temporalEmbedding;
    private readonly ITemporalRiskPredictionService _temporalRiskPrediction;
    private readonly ITemporalElevatedRiskPredictionService _temporalElevatedRiskPrediction;
    private readonly RecursorPoliciesOptions _policies;
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
    IUserRelativeSignalService userRelativeSignalService,
    IUserThresholdRepository userThresholdRepository,
    IUserProfileRepository userProfileRepository,
    IUserProfileUpdateService userProfileUpdateService,
    IMultiSignalGuardrailService multiSignalGuardrail,
    IPhase8AGuardrailModifierService phase8aModifier,
    IAdaptationEffectivenessService adaptationEffectiveness,
    IPolicyReliabilityWeightingService phase8eReliability,
    ISequenceFeatureExtractor sequenceFeatureExtractor,
    ITemporalEmbeddingService temporalEmbedding,
    ITemporalRiskPredictionService temporalRiskPrediction,
    ITemporalElevatedRiskPredictionService temporalElevatedRiskPrediction,
    IOptions<RecursorPoliciesOptions> policiesOptions,
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
        _userRelativeSignalService = userRelativeSignalService;
        _userThresholdRepository = userThresholdRepository;
        _userProfileRepository = userProfileRepository;
        _userProfileUpdateService = userProfileUpdateService;
        _multiSignalGuardrail = multiSignalGuardrail;
        _phase8aModifier = phase8aModifier;
        _adaptationEffectiveness = adaptationEffectiveness;
        _phase8eReliability = phase8eReliability;
        _sequenceFeatureExtractor = sequenceFeatureExtractor;
        _temporalEmbedding = temporalEmbedding;
        _temporalRiskPrediction = temporalRiskPrediction;
        _temporalElevatedRiskPrediction = temporalElevatedRiskPrediction;
        _policies = policiesOptions.Value;
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
        bool hypothesisHasImproving = hypothesisLabels.Contains("improving_pattern");
        bool hypothesisHasRelapse = hypothesisLabels.Contains("relapse_pattern");

        if (hypothesisHasStableMastery)
        {
            session.ConsecutiveStableMasteryWindows += 1;
            session.ConsecutiveSupportFadeEligibleWindows += 1;
            session.ConsecutiveRelapseWindows = 0;
        }
        else if (hypothesisHasImproving)
        {
            session.ConsecutiveStableMasteryWindows = 0;
            session.ConsecutiveSupportFadeEligibleWindows += 1;
            session.ConsecutiveRelapseWindows = 0;
        }
        else if (hypothesisHasRelapse)
        {
            session.ConsecutiveRelapseWindows += 1;
            session.ConsecutiveStableMasteryWindows = 0;
            session.ConsecutiveSupportFadeEligibleWindows = 0;
        }
        else
        {
            session.ConsecutiveStableMasteryWindows = 0;
            session.ConsecutiveRelapseWindows = 0;
            session.ConsecutiveSupportFadeEligibleWindows = 0;
        }
        _sessionRepository.Update(session);

        // Phase 6B: update longitudinal user profile with this window's behavioral data.
        // Non-blocking — a failed update never stops the adaptation pipeline.
        try
        {
            await _userProfileUpdateService.UpdateProfileAsync(
                session.UserId, session.SessionId, behaviorProfile.WindowIndex,
                behaviorProfile, hypothesisLabels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Profile update failed for userId '{UserId}' (session {SessionId}). Continuing pipeline.",
                session.UserId, session.SessionId);
        }

        // Shadow ML prediction — shadow output feeds the ML guardrail in AdaptationPolicyService.
        // It may veto hint-reduction families when hint dependence probability is high (>= 0.85).
        // It does not create new adaptations or affect difficulty / pace.
        BehaviorStateFeatureVector? featureVector = null;
        BehaviorStatePrediction? shadowPrediction = null;
        try
        {
            featureVector = _featureVectorBuilder.Build(session, batch, behaviorProfile, trajectoryResult);
            shadowPrediction = await _behaviorStatePredictionService.PredictAsync(featureVector);
            if (shadowPrediction is not null)
            {
                _logger.LogInformation(
                    "Shadow ML prediction generated. SessionId={SessionId} WindowIndex={WindowIndex} " +
                    "ModelVersion={ModelVersion} InferenceMode={InferenceMode} " +
                    "ConfusionProbability={ConfusionProbability:0.000} PredictedConfusionRisk={PredictedConfusionRisk} " +
                    "ConfusionModelVersion={ConfusionModelVersion} " +
                    "HintDependenceProbability={HintDependenceProbability:0.000} " +
                    "HintDependenceNextProbability={HintDependenceNextProbability}",
                    session.SessionId, featureVector.WindowIndex,
                    shadowPrediction.ModelVersion, shadowPrediction.InferenceMode,
                    shadowPrediction.ConfusionProbability,
                    shadowPrediction.PredictedConfusionRisk ?? "n/a",
                    shadowPrediction.ConfusionModelVersion ?? "n/a",
                    shadowPrediction.HintDependenceProbability,
                    shadowPrediction.HintDependenceNextProbability);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shadow ML prediction failed for session {SessionId}. Continuing pipeline.", session.SessionId);
        }

        // Multi-signal guardrail summary — observability only, never blocks or changes adaptation.
        // Computed here so the result can be persisted in-line with the training row below.
        // personalizedSignals are not yet available at this point; user-relative warnings are
        // deferred until a future phase restructures that fetch earlier in the pipeline.
        MultiSignalGuardrailSummary? guardrailSummary = null;
        try
        {
            guardrailSummary = _multiSignalGuardrail.Evaluate(hypothesisSet, shadowPrediction, null);
            _logger.LogInformation(
                "Multi-signal guardrail summary. SessionId={SessionId} WindowIndex={WindowIndex} " +
                "OverallBehaviorState={OverallBehaviorState} " +
                "ConfusionAgreement={ConfusionAgreement} MasteryAgreement={MasteryAgreement} " +
                "HintDependenceRisk={HintDependenceRisk} ConflictCount={ConflictCount} " +
                "Warnings=[{Warnings}]",
                session.SessionId, featureVector?.WindowIndex ?? -1,
                guardrailSummary.OverallBehaviorState,
                guardrailSummary.ConfusionSignalAgreement,
                guardrailSummary.StableMasterySignalAgreement,
                guardrailSummary.HintDependenceRiskLevel,
                guardrailSummary.GuardrailConflictCount,
                string.Join(" | ", guardrailSummary.GuardrailWarnings));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Multi-signal guardrail evaluation failed for session {SessionId}. Continuing pipeline.", session.SessionId);
        }

        // Phase 10A: stamp guardrail behavior state on the most recent snapshot, then compute
        // sequence features from the updated window. Both are observability-only and non-blocking.
        if (guardrailSummary is not null && session.RecentSnapshots.Count > 0)
        {
            session.RecentSnapshots[^1].OverallBehaviorState = guardrailSummary.OverallBehaviorState;
            _sessionRepository.Update(session);
        }

        var sequenceSummary = _sequenceFeatureExtractor.Extract(session.RecentSnapshots);
        var trajectoryClassification = sequenceSummary is not null
            ? _sequenceFeatureExtractor.Classify(sequenceSummary)
            : null;

        // Training-row ingestion — additive only, never blocks or changes adaptation.
        if (featureVector is not null)
        {
            try
            {
                var trainingRow = AdxRowMapper.MapBehaviorStateTrainingRow(featureVector, hypothesisSet, shadowPrediction, guardrailSummary, sequenceSummary, trajectoryClassification);
                await _adxIngestion.IngestBehaviorStateTrainingRowAsync(trainingRow);
                _logger.LogInformation(
                    "Training row persisted to ADX. SessionId={SessionId} WindowIndex={WindowIndex} " +
                    "ConfusionProbability={ConfusionProbability:0.000} PredictedConfusionRisk={PredictedConfusionRisk} " +
                    "GuardrailState={GuardrailState}",
                    session.SessionId, featureVector.WindowIndex,
                    shadowPrediction?.ConfusionProbability ?? 0.0,
                    shadowPrediction?.PredictedConfusionRisk ?? "n/a",
                    guardrailSummary?.OverallBehaviorState ?? "n/a");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Training row ingestion failed for session {SessionId}. Continuing pipeline.", session.SessionId);
            }
        }

        // Phase 8B: adaptation effectiveness evaluation — observability only, never blocks pipeline.
        // Checks whether the previous window's adaptation improved learner behavior in this window.
        if (featureVector is not null)
        {
            try
            {
                var effectivenessDoc = _adaptationEffectiveness.EvaluateNextWindow(session.SessionId, featureVector);
                if (effectivenessDoc is not null)
                {
                    await _adxIngestion.IngestAdaptationEffectivenessAsync(
                        AdxRowMapper.MapAdaptationEffectiveness(effectivenessDoc));
                    _logger.LogInformation(
                        "Adaptation effectiveness evaluated. SessionId={SessionId} " +
                        "DecisionIndex={DecisionIndex} AppliedAtWindow={AppliedAt} " +
                        "EvaluatedAtWindow={EvaluatedAt} ConfusionDelta={ConfusionDelta:0.000} " +
                        "Outcome={Outcome} Families=[{Families}]",
                        session.SessionId,
                        effectivenessDoc.AdaptationDecisionIndex,
                        effectivenessDoc.AppliedAtWindowIndex,
                        effectivenessDoc.EvaluatedAtWindowIndex,
                        effectivenessDoc.ConfusionDelta,
                        effectivenessDoc.Outcome,
                        string.Join(", ", effectivenessDoc.InterventionFamilies));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Adaptation effectiveness evaluation failed for session {SessionId}. Continuing pipeline.",
                    session.SessionId);
            }
        }

        // Phase 10B: temporal embedding + prediction targets — observability only, never blocks pipeline.
        TemporalEmbeddingVector? embedding = null;
        try
        {
            embedding = _temporalEmbedding.BuildEmbedding(
                session.SessionId, session.UserId, session.SimId, session.ScenarioId,
                snapshot, sequenceSummary, trajectoryClassification, guardrailSummary, featureVector);
            await _adxIngestion.IngestTemporalEmbeddingAsync(AdxRowMapper.MapTemporalEmbedding(embedding));

            var targets = _temporalEmbedding.BuildPredictionTargets(
                session.SessionId, session.UserId, session.RecentSnapshots, trajectoryClassification);
            foreach (var target in targets)
                await _adxIngestion.IngestTemporalPredictionTargetAsync(AdxRowMapper.MapTemporalPredictionTarget(target));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Temporal embedding/target generation failed for session {SessionId}. Continuing pipeline.",
                session.SessionId);
        }

        // Phase 10C-2: shadow temporal risk prediction — never modifies adaptation decisions.
        if (embedding is not null)
        {
            try
            {
                var riskResult = _temporalRiskPrediction.Predict(embedding);
                var predNow = DateTime.UtcNow;

                foreach (var (pred, h) in new[]
                {
                    (riskResult.Horizon1, 1),
                    (riskResult.Horizon2, 2),
                    (riskResult.Horizon3, 3),
                })
                {
                    if (pred is null) continue;
                    _logger.LogInformation(
                        "Temporal risk shadow prediction. SessionId={SessionId} WindowIndex={WindowIndex} " +
                        "Horizon={Horizon} PredictedRisk={Risk} Confidence={Confidence:0.000} ModelVersion={ModelVersion}",
                        session.SessionId, snapshot.WindowIndex, h,
                        pred.PredictedNearTermRisk, pred.Confidence, pred.ModelVersion);
                    await _adxIngestion.IngestTemporalRiskPredictionAsync(
                        AdxRowMapper.MapTemporalRiskPrediction(embedding, pred, h, predNow));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Temporal risk shadow prediction failed for session {SessionId}. Continuing pipeline.",
                    session.SessionId);
            }
        }

        // Phase 10D-1: shadow elevated-risk prediction — never modifies adaptation decisions.
        if (embedding is not null)
        {
            try
            {
                var elevatedResult = _temporalElevatedRiskPrediction.Predict(embedding);
                var elevatedNow = DateTime.UtcNow;

                foreach (var (pred, h) in new[]
                {
                    (elevatedResult.Horizon1, 1),
                    (elevatedResult.Horizon2, 2),
                    (elevatedResult.Horizon3, 3),
                })
                {
                    if (pred is null) continue;
                    _logger.LogInformation(
                        "Elevated-risk shadow prediction. SessionId={SessionId} WindowIndex={WindowIndex} " +
                        "Horizon={Horizon} PredictedElevatedRisk={Risk} Confidence={Confidence:0.000} ModelVersion={ModelVersion}",
                        session.SessionId, snapshot.WindowIndex, h,
                        pred.PredictedElevatedRisk, pred.Confidence, pred.ModelVersion);
                    await _adxIngestion.IngestTemporalElevatedRiskPredictionAsync(
                        AdxRowMapper.MapTemporalElevatedRiskPrediction(embedding, pred, h, elevatedNow));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Elevated-risk shadow prediction failed for session {SessionId}. Continuing pipeline.",
                    session.SessionId);
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
                HypothesisLabels = hypothesisLabels,
                SequenceSummary = sequenceSummary,
                TrajectorySummary = trajectoryClassification
            };
        }

        // Phase 8 personalization — fetch personalized signals and per-user thresholds for any
        // active personalized rule. Only fetched when at least one rule flag is on; always non-blocking.
        PersonalizedPolicySignals? personalizedSignals = null;
        UserPolicyThresholds? userThresholds = null;
        if (_policies.EnablePersonalizedHintFadeRule
            || _policies.EnablePersonalizedConfusionBlockDifficultyRule
            || _policies.EnablePersonalizedDifficultyAccelerationRule)
        {
            try
            {
                var relativeSignals = await _userRelativeSignalService.GetUserRelativeSignalsAsync(session.UserId);
                if (relativeSignals is not null)
                {
                    personalizedSignals = new PersonalizedPolicySignals
                    {
                        ConfusionDelta = relativeSignals.ConfusionDelta,
                        HintDependenceDelta = relativeSignals.HintDependenceDelta,
                        IsBelowBaselineGoalUnderstanding = relativeSignals.IsBelowBaselineGoalUnderstanding,
                        IsBelowBaselineAttention = relativeSignals.IsBelowBaselineAttention,
                        CurrentConfusionScore = relativeSignals.CurrentConfusionScore,
                        CurrentGoalUnderstanding = relativeSignals.CurrentGoalUnderstanding
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch personalized signals for session {SessionId}. Skipping personalized rules.", session.SessionId);
            }

            // Phase 6A: prefer thresholds from persistent user profile; fall back to seeded
            // in-memory thresholds if no profile exists. Both produce the same UserPolicyThresholds
            // type consumed by ApplyPolicy — the fallback chain is transparent to the policy layer.
            var userProfile = await _userProfileRepository.GetAsync(session.UserId);
            if (userProfile is not null)
            {
                userThresholds = UserProfileThresholdMapper.ToUserPolicyThresholds(userProfile);
                _logger.LogInformation(
                    "Per-user profile thresholds active for userId '{UserId}' (session {SessionId}).",
                    session.UserId, session.SessionId);
            }
            else
            {
                userThresholds = _userThresholdRepository.Get(session.UserId);
                if (userThresholds is not null)
                    _logger.LogInformation(
                        "Per-user seeded thresholds active for userId '{UserId}' (session {SessionId}).",
                        session.UserId, session.SessionId);
            }
        }

        var adaptation = _adaptationPolicy.ApplyPolicy(session, catalog, hypothesisSet, shadowPrediction, personalizedSignals, userThresholds);
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
                Explanation = explanationNoAdaptation,
                SequenceSummary = sequenceSummary,
                TrajectorySummary = trajectoryClassification
            };
        }

        // Phase 10A: stamp trajectory notes on the adaptation document before any ingest path.
        adaptation.Phase10TrajectoryNotes = BuildPhase10TrajectoryNotes(sequenceSummary, trajectoryClassification);

        // Phase 8A: post-policy safety modifier.
        // Reviews the proposed adaptation and removes parameter changes that violate
        // Phase 8A guardrail rules (conflicted/struggling veto, high-confusion block,
        // strong-mastery permission, hint-dependence protection).
        // Only fires when enabled AND a guardrail summary is available.
        // Never creates new changes. Does not affect adaptation if flag is off.
        if (_policies.EnablePhase8AGuardrailModifier && guardrailSummary is not null)
        {
            adaptation = _phase8aModifier.Apply(adaptation, guardrailSummary, shadowPrediction);
        }

        // If Phase 8A vetoed every parameter change, ingest the audit record but
        // treat the result as "no adaptation" so the sim is not told to do anything.
        if (adaptation.Phase8AGuardrailNotes is not null && adaptation.ParameterChanges.Count == 0)
        {
            _logger.LogInformation(
                "Phase 8A vetoed all parameter changes for session {SessionId}. " +
                "Ingesting audit record. Notes={Notes}",
                session.SessionId, adaptation.Phase8AGuardrailNotes);
            try
            {
                await _adxIngestion.IngestAdaptationDecisionAsync(AdxRowMapper.MapAdaptationDecision(adaptation));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to ingest Phase 8A fully-vetoed adaptation for session {SessionId}.",
                    session.SessionId);
            }
            var explanationVetoed = await _explanationService.GenerateExplanationAsync(
                session, behaviorProfile, hypothesisSet, null);
            return new IngestionResult
            {
                AdaptationProduced = false,
                HypothesisLabels = hypothesisLabels,
                Explanation = explanationVetoed,
                SequenceSummary = sequenceSummary,
                TrajectorySummary = trajectoryClassification
            };
        }

        // Phase 8E: policy reliability weighting.
        // Suppresses "risky" intervention families (and their owned parameter changes) based on
        // pre-configured Phase 8D reliability tier data. Never creates new changes.
        // Mode "disabled" (default) — no effect.
        // Mode "shadow"   — logs what would be suppressed; adaptation is not modified.
        // Mode "active"   — removes risky families and their parameter changes.
        adaptation = _phase8eReliability.Apply(adaptation, guardrailSummary, shadowPrediction);

        // If Phase 8E active-mode suppressed every parameter change, ingest the audit record
        // but return "no adaptation" so the sim receives no instructions.
        if (adaptation.Phase8EReliabilityMode == "active" && adaptation.ParameterChanges.Count == 0)
        {
            _logger.LogInformation(
                "Phase 8E suppressed all parameter changes for session {SessionId}. " +
                "Ingesting audit record. Notes={Notes}",
                session.SessionId, adaptation.Phase8EReliabilityNotes);
            try
            {
                await _adxIngestion.IngestAdaptationDecisionAsync(AdxRowMapper.MapAdaptationDecision(adaptation));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to ingest Phase 8E fully-suppressed adaptation for session {SessionId}.",
                    session.SessionId);
            }
            var explanationSuppressed = await _explanationService.GenerateExplanationAsync(
                session, behaviorProfile, hypothesisSet, null);
            return new IngestionResult
            {
                AdaptationProduced = false,
                HypothesisLabels = hypothesisLabels,
                Explanation = explanationSuppressed,
                SequenceSummary = sequenceSummary,
                TrajectorySummary = trajectoryClassification
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

        // Phase 8B: record this adaptation so the next window can evaluate its effectiveness.
        if (featureVector is not null)
        {
            _adaptationEffectiveness.RecordAppliedAdaptation(session.SessionId, adaptation, featureVector);
        }

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
            Explanation = explanation,
            SequenceSummary = sequenceSummary,
            TrajectorySummary = trajectoryClassification
        };
    }

    private static string? BuildPhase10TrajectoryNotes(SequenceFeatureSummary? seq, TrajectorySummary? traj)
    {
        if (seq is null || traj is null) return null;
        var label = traj.IsImproving ? "improving"
            : traj.IsDeteriorating ? "deteriorating"
            : traj.IsVolatile ? "volatile"
            : "stable";
        var volatility = traj.IsVolatile ? "high" : "low";
        return $"trajectory: {label}; volatility={volatility}; " +
               $"predicted-risk={traj.PredictedNearTermRisk}; windows={seq.WindowCount}; " +
               $"confusion-slope={seq.ConfusionTrendSlope:0.000}; stability={traj.StabilityScore:0.00}";
    }
}
