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
            DecisionIndex = doc.DecisionIndex,
            SourceHypothesisSetId = doc.SourceHypothesisSetId,
            PolicyVersion = "1.0",
            InterventionFamilies = JsonSerializer.SerializeToElement(doc.InterventionFamilies, JsonOpts),
            ParameterChanges = JsonSerializer.SerializeToElement(doc.ParameterChanges, JsonOpts),
            ReasoningSummary = doc.ReasoningSummary,
            ExpiresAfterWindow = doc.ExpiresAfterWindow,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static BehaviorStateTrainingRow MapBehaviorStateTrainingRow(
        BehaviorStateFeatureVector featureVector,
        HypothesisSetDocument hypothesisSet,
        BehaviorStatePrediction? prediction)
    {
        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToHashSet();

        return new BehaviorStateTrainingRow
        {
            // Identity / metadata
            SessionId = featureVector.SessionId,
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
        };
    }

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
