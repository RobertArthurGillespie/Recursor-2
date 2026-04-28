using System.Text.Json;
//using NCATAIBlazorFrontendTest.Client.Pages.Apps.Users;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// Maps domain models ↔ ADX row DTOs.
// Forward (domain → row): used by RecursorIngestionService before ADX ingest.
// Reverse (row → domain): used by debug query endpoints after ADX read.
// Keeps mapping logic out of controllers and services.
public static class AdxRowMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null // preserve PascalCase to match ADX column names
    };

    public static IEnumerable<RawEventRow> MapRawEvents(RawEventBatch batch)
    {
        foreach (var evt in batch.Events)
        {
            yield return new RawEventRow
            {
                SessionId = batch.SessionId,
                UserId = batch.UserId,
                SimId = batch.SimId,
                SimVersion = batch.SimVersion,
                ScenarioId = batch.ScenarioId,
                BatchId = batch.BatchId,
                BatchSequence = batch.BatchSequence,
                EventId = evt.EventId,
                SequenceNumber = evt.SequenceNumber,
                TimestampUtc = evt.TimestampUtc,
                EventType = evt.EventType,
                Category = evt.Category,
                Actor = evt.Actor,
                Target = evt.Target,
                Metrics = JsonSerializer.SerializeToElement(evt.Metrics, JsonOpts),
                Context = JsonSerializer.SerializeToElement(evt.Context, JsonOpts),
                Payload = JsonSerializer.SerializeToElement(evt.Payload, JsonOpts)
            };
        }
    }

    public static FeatureWindowRow MapFeatureWindow(FeatureWindowDocument doc)
    {
        return new FeatureWindowRow
        {
            SessionId = doc.SessionId,
            UserId = doc.UserId,
            WindowIndex = doc.WindowIndex,
            WindowType = doc.WindowType,
            WindowStartSequence = doc.WindowStartSequence,
            WindowEndSequence = doc.WindowEndSequence,
            WindowStartUtc = doc.WindowStartUtc,
            WindowEndUtc = doc.WindowEndUtc,
            SimId = doc.SimId,
            ScenarioId = doc.ScenarioId,
            FeatureExtractorVersion = "1.0",
            Features = JsonSerializer.SerializeToElement(doc.Features, JsonOpts)
        };
    }

    public static BehaviorProfileRow MapBehaviorProfile(BehaviorProfileDocument doc)
    {
        return new BehaviorProfileRow
        {
            SessionId = doc.SessionId,
            UserId = doc.UserId,
            WindowIndex = doc.WindowIndex,
            SourceFeatureWindowId = doc.SourceFeatureWindowId,
            InterpreterVersion = "1.0",
            DimensionScores = JsonSerializer.SerializeToElement(doc.DimensionScores, JsonOpts),
            BehaviorScores = JsonSerializer.SerializeToElement(doc.BehaviorScores),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static HypothesisSetRow MapHypothesisSet(HypothesisSetDocument doc)
    {
        return new HypothesisSetRow
        {
            SessionId = doc.SessionId,
            UserId = doc.UserId,
            WindowIndex = doc.WindowIndex,
            SourceBehaviorProfileId = doc.SourceBehaviorProfileId,
            InterpreterMode = doc.InterpreterMode,
            InterpreterVersion = doc.InterpreterVersion,
            Hypotheses = JsonSerializer.SerializeToElement(doc.Hypotheses, JsonOpts),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static AdaptationDecisionRow MapAdaptationDecision(AdaptationDecisionDocument doc)
    {
        return new AdaptationDecisionRow
        {
            SessionId = doc.SessionId,
            UserId = doc.UserId,
            AdaptationDecisionId = doc.Id,
            DecisionIndex = doc.DecisionIndex,
            SourceHypothesisSetId = doc.SourceHypothesisSetId,
            PolicyVersion = "1.0",
            InterventionFamilies = JsonSerializer.SerializeToElement(doc.InterventionFamilies, JsonOpts),
            ParameterChanges = JsonSerializer.SerializeToElement(doc.ParameterChanges, JsonOpts),
            ReasoningSummary = doc.ReasoningSummary,
            ExpiresAfterWindow = doc.ExpiresAfterWindow,
            CreatedAtUtc = DateTime.UtcNow,
            HintDependenceNextProbability = doc.HintDependenceNextProbability,
            NextHintDependenceGuardrailTriggered = doc.NextHintDependenceGuardrailTriggered,
            NextHintDependenceGuardrailLevel = doc.NextHintDependenceGuardrailLevel,
            Phase8AGuardrailNotes = doc.Phase8AGuardrailNotes,
            Phase8EReliabilityNotes = doc.Phase8EReliabilityNotes,
            Phase8EReliabilityMode = doc.Phase8EReliabilityMode,
            Phase10TrajectoryNotes = doc.Phase10TrajectoryNotes,
        };
    }

    public static BehaviorStateTrainingRow MapBehaviorStateTrainingRow(
        BehaviorStateFeatureVector featureVector,
        HypothesisSetDocument hypothesisSet,
        BehaviorStatePrediction? prediction,
        MultiSignalGuardrailSummary? guardrail = null,
        SequenceFeatureSummary? sequenceSummary = null,
        TrajectorySummary? trajectorySummary = null)
    {
        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToHashSet();

        return new BehaviorStateTrainingRow
        {
            // Identity / metadata
            SessionId = featureVector.SessionId,
            UserId = featureVector.UserId,
            SimId = featureVector.SimId,
            ScenarioId = featureVector.ScenarioId,
            WindowIndex = featureVector.WindowIndex,
            TaskType = featureVector.TaskType,
            CreatedAtUtc = DateTime.UtcNow,

            // Dimension scores
            AttentionDetection = featureVector.AttentionDetection,
            GoalUnderstanding = featureVector.GoalUnderstanding,
            ProcedureSequencing = featureVector.ProcedureSequencing,
            PaceRegulation = featureVector.PaceRegulation,
            SelfCorrection = featureVector.SelfCorrection,
            FeedbackResponsiveness = featureVector.FeedbackResponsiveness,
            SafetyCompliance = featureVector.SafetyCompliance,
            TaskContinuity = featureVector.TaskContinuity,

            // Higher-order behavior scores
            ConfusionScore = featureVector.ConfusionScore,
            HesitationScore = featureVector.HesitationScore,
            ImpulsivityScore = featureVector.ImpulsivityScore,
            HintDependenceScore = featureVector.HintDependenceScore,

            // Trajectory
            GoalTrend = featureVector.GoalTrend,
            AttentionTrend = featureVector.AttentionTrend,
            ConfusionTrend = featureVector.ConfusionTrend,
            HintDependenceTrend = featureVector.HintDependenceTrend,

            // Adaptive state
            CurrentHintMode = featureVector.CurrentHintMode,
            CurrentDifficulty = featureVector.CurrentDifficulty,
            CurrentTimePressure = featureVector.CurrentTimePressure,
            CurrentErrorTolerance = featureVector.CurrentErrorTolerance,

            // Counters
            ConsecutiveStableMasteryWindows = featureVector.ConsecutiveStableMasteryWindows,
            ConsecutiveRelapseWindows = featureVector.ConsecutiveRelapseWindows,

            // Window summary
            EventCountInWindow = featureVector.EventCountInWindow,
            ErrorCountInWindow = featureVector.ErrorCountInWindow,
            HintCountInWindow = featureVector.HintCountInWindow,
            StepCompleteCountInWindow = featureVector.StepCompleteCountInWindow,

            // Weak labels
            LabelConfusion = (labels.Contains("confusion_pattern") || labels.Contains("goal-confusion")) ? 1 : 0,
            LabelHintDependence = (labels.Contains("hint_dependence_pattern") || labels.Contains("hint-dependency")) ? 1 : 0,
            LabelStableMastery = labels.Contains("stable_mastery_pattern") ? 1 : 0,

            // Shadow prediction
            PredConfusionProbability = prediction?.ConfusionProbability ?? 0.0,
            PredHintDependenceProbability = prediction?.HintDependenceProbability ?? 0.0,
            PredStableMasteryProbability = prediction?.StableMasteryProbability ?? 0.0,
            ModelVersion = prediction?.ModelVersion ?? "",
            InferenceMode = prediction?.InferenceMode ?? "shadow",

            // Confusion shadow detail (Phase 7A)
            PredictedConfusionRisk  = prediction?.PredictedConfusionRisk ?? "",
            ConfusionModelVersion   = prediction?.ConfusionModelVersion ?? "",
            ConfusionPredictionUtc  = prediction?.ConfusionPredictionUtc,

            // Stable mastery shadow detail (Phase 7B)
            PredictedStableMasteryState = prediction?.PredictedStableMasteryState ?? "",
            StableMasteryModelVersion   = prediction?.StableMasteryModelVersion ?? "",
            StableMasteryPredictionUtc  = prediction?.StableMasteryPredictionUtc,

            // Multi-signal guardrail summary (Phase 7C)
            GuardrailOverallBehaviorState        = guardrail?.OverallBehaviorState ?? "",
            GuardrailConfusionSignalAgreement     = guardrail?.ConfusionSignalAgreement ?? false,
            GuardrailStableMasterySignalAgreement = guardrail?.StableMasterySignalAgreement ?? false,
            GuardrailHintDependenceRiskLevel      = guardrail?.HintDependenceRiskLevel ?? "",
            GuardrailConflictCount                = guardrail?.GuardrailConflictCount ?? 0,
            GuardrailWarnings                     = guardrail is not null
                ? string.Join(" | ", guardrail.GuardrailWarnings)
                : "",

            // Phase 10A: sequence-aware features
            SequenceWindowCount    = sequenceSummary?.WindowCount ?? 0,
            MeanConfusionScore     = sequenceSummary?.MeanConfusionScore ?? 0.0,
            ConfusionTrendSlope    = sequenceSummary?.ConfusionTrendSlope ?? 0.0,
            GoalTrendSlope         = sequenceSummary?.GoalTrendSlope ?? 0.0,
            HintTrendSlope         = sequenceSummary?.HintTrendSlope ?? 0.0,
            VolatilityScore        = sequenceSummary?.VolatilityScore ?? 0.0,
            MomentumScore          = sequenceSummary?.MomentumScore ?? 0.0,
            StabilityScore         = trajectorySummary?.StabilityScore ?? 0.0,
            PredictedNearTermRisk  = trajectorySummary?.PredictedNearTermRisk ?? "",
        };
    }

    // ── Phase 8B: adaptation effectiveness row mapping ───────────────────────

    public static AdaptationEffectivenessRow MapAdaptationEffectiveness(AdaptationEffectivenessDocument doc) =>
        new()
        {
            SessionId               = doc.SessionId,
            UserId                  = doc.UserId,
            AdaptationDecisionId    = doc.AdaptationDecisionId,
            AdaptationDecisionIndex = doc.AdaptationDecisionIndex,
            InterventionFamilies    = JsonSerializer.SerializeToElement(doc.InterventionFamilies, JsonOpts),
            ParameterChanges        = JsonSerializer.SerializeToElement(doc.ParameterChanges, JsonOpts),
            AppliedAtWindowIndex    = doc.AppliedAtWindowIndex,
            EvaluatedAtWindowIndex  = doc.EvaluatedAtWindowIndex,
            PreConfusionScore       = doc.PreConfusionScore,
            PreHintDependenceScore  = doc.PreHintDependenceScore,
            PreGoalUnderstanding    = doc.PreGoalUnderstanding,
            NextConfusionScore      = doc.NextConfusionScore,
            NextHintDependenceScore = doc.NextHintDependenceScore,
            NextGoalUnderstanding   = doc.NextGoalUnderstanding,
            ConfusionDelta          = doc.ConfusionDelta,
            HintDependenceDelta     = doc.HintDependenceDelta,
            GoalUnderstandingDelta  = doc.GoalUnderstandingDelta,
            Outcome                 = doc.Outcome,
            CreatedAtUtc            = DateTime.UtcNow,
        };

    // ── Phase 6B: user profile row mappings ──────────────────────────────────

    public static UserBehaviorProfileRow MapUserBehaviorProfile(
        NCATAIBlazorFrontendTest.Server.Recursor.Models.UserBehaviorProfile profile) =>
        new()
        {
            UserId        = profile.UserId,
            CreatedAtUtc  = profile.CreatedAtUtc,
            UpdatedAtUtc  = profile.UpdatedAtUtc,
            LastSessionId = profile.LastSessionId,
            TotalSessions = profile.TotalSessions,
            TotalWindows  = profile.TotalWindows,

            AvgConfusionScore      = profile.AvgConfusionScore,
            AvgHintDependenceScore = profile.AvgHintDependenceScore,
            AvgHesitationScore     = profile.AvgHesitationScore,
            AvgGoalUnderstanding   = profile.AvgGoalUnderstanding,
            AvgAttentionDetection  = profile.AvgAttentionDetection,
            AvgProcedureSequencing = profile.AvgProcedureSequencing,
            AvgSelfCorrection      = profile.AvgSelfCorrection,
            AvgTaskContinuity      = profile.AvgTaskContinuity,
            StableMasteryRate      = profile.StableMasteryRate,
            ConfusionRate          = profile.ConfusionRate,
            HintDependenceRate     = profile.HintDependenceRate,

            ConfusionBlockDifficultyIncreaseThreshold          = profile.ConfusionBlockDifficultyIncreaseThreshold,
            HintFadeConfusionDeltaThreshold                    = profile.HintFadeConfusionDeltaThreshold,
            HintFadeHintDependenceDeltaThreshold               = profile.HintFadeHintDependenceDeltaThreshold,
            HintFadeMaxCurrentConfusionScore                   = profile.HintFadeMaxCurrentConfusionScore,
            HintFadeMinCurrentGoalUnderstanding                = profile.HintFadeMinCurrentGoalUnderstanding,
            DifficultyAccelerationConfusionDeltaThreshold      = profile.DifficultyAccelerationConfusionDeltaThreshold,
            DifficultyAccelerationHintDependenceDeltaThreshold = profile.DifficultyAccelerationHintDependenceDeltaThreshold,
            DifficultyAccelerationMaxCurrentConfusionScore     = profile.DifficultyAccelerationMaxCurrentConfusionScore,
            DifficultyAccelerationMinCurrentGoalUnderstanding  = profile.DifficultyAccelerationMinCurrentGoalUnderstanding,
            DifficultyAccelerationDelta                        = profile.DifficultyAccelerationDelta,
        };

    public static UserBehaviorProfileUpdateRow MapUserBehaviorProfileUpdate(
        string userId, string sessionId, int windowIndex, DateTime createdAtUtc,
        double confusionScore, double hintDependenceScore, double hesitationScore,
        double goalUnderstanding, double attentionDetection, double procedureSequencing,
        double selfCorrection, double taskContinuity,
        bool hasStableMastery, bool hasConfusionPattern, bool hasHintDependencePattern) =>
        new()
        {
            UserId       = userId,
            SessionId    = sessionId,
            WindowIndex  = windowIndex,
            CreatedAtUtc = createdAtUtc,

            ConfusionScore      = confusionScore,
            HintDependenceScore = hintDependenceScore,
            HesitationScore     = hesitationScore,
            GoalUnderstanding   = goalUnderstanding,
            AttentionDetection  = attentionDetection,
            ProcedureSequencing = procedureSequencing,
            SelfCorrection      = selfCorrection,
            TaskContinuity      = taskContinuity,

            HasStableMastery       = hasStableMastery,
            HasConfusionPattern    = hasConfusionPattern,
            HasHintDependencePattern = hasHintDependencePattern,
        };

    // ── Reverse mappings (ADX row → domain model) ─────────────────────────────
    // Used by debug query endpoints. Note: FeatureWindowDocument.Id is not stored
    // in ADX, so the returned document will have Id = "".

    public static FeatureWindowDocument MapToFeatureWindowDocument(FeatureWindowRow row)
    {
        return new FeatureWindowDocument
        {
            Id = "",    // not persisted in ADX — debug use only
            DocumentType = "FeatureWindow",
            SessionId = row.SessionId,
            UserId = row.UserId,
            WindowIndex = row.WindowIndex,
            WindowType = row.WindowType,
            WindowStartSequence = row.WindowStartSequence,
            WindowEndSequence = row.WindowEndSequence,
            WindowStartUtc = row.WindowStartUtc,
            WindowEndUtc = row.WindowEndUtc,
            SimId = row.SimId,
            ScenarioId = row.ScenarioId,
            Features = JsonSerializer.Deserialize<BehavioralFeatureSet>(
                row.Features.GetRawText(), JsonOpts) ?? new BehavioralFeatureSet()
        };
    }
}
