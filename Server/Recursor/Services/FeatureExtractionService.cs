using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IFeatureExtractionService
{
    // Returns a FeatureWindowDocument when a trigger condition is met, or null otherwise.
    // Trigger conditions (any one sufficient):
    //   1. Events accumulated since last window >= AccumulationThreshold (50)
    //   2. Batch contains a stage/task completion event
    //   3. Batch contains a safety violation event
    FeatureWindowDocument? TryExtractWindow(SessionDocument session, RawEventBatch batch);
}

public class FeatureExtractionService : IFeatureExtractionService
{
    // Minimum events accumulated since the last window before extraction fires.
    private const int AccumulationThreshold = 50;

    // Event types that force a window regardless of accumulation count.
    private static readonly HashSet<string> StageTriggerTypes =
        ["task_complete", "stage_complete"];

    private static readonly HashSet<string> SafetyTriggerTypes =
        ["safety_violation"];

    public FeatureWindowDocument? TryExtractWindow(SessionDocument session, RawEventBatch batch)
    {
        if (batch.Events.Count == 0)
            return null;

        bool accumulationTrigger = session.EventsSinceLastWindow >= AccumulationThreshold;
        bool stageTrigger        = batch.Events.Any(e => StageTriggerTypes.Contains(e.EventType));
        bool safetyTrigger       = batch.Events.Any(e => SafetyTriggerTypes.Contains(e.EventType));

        if (!accumulationTrigger && !stageTrigger && !safetyTrigger)
            return null;

        var events = batch.Events;
        long minSeq   = events.Min(e => e.SequenceNumber);
        long maxSeq   = events.Max(e => e.SequenceNumber);
        DateTime minTime = events.Min(e => e.TimestampUtc);
        DateTime maxTime = events.Max(e => e.TimestampUtc);

        return new FeatureWindowDocument
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.SessionId,
            WindowIndex = session.BatchCount,
            WindowType = stageTrigger  ? "stage-completion" :
                         safetyTrigger ? "safety-trigger"   :
                                         "accumulation",
            WindowStartSequence = minSeq,
            WindowEndSequence   = maxSeq,
            WindowStartUtc      = minTime,
            WindowEndUtc        = maxTime,
            SimId      = session.SimId,
            ScenarioId = session.ScenarioId,
            Features   = ExtractFeatures(session, events)
        };
    }

    private static BehavioralFeatureSet ExtractFeatures(SessionDocument session, List<RawEventRecord> events)
    {
        int totalEvents = events.Count;
        int errorEvents = events.Count(e => e.EventType == "error");
        int hintEvents = events.Count(e => e.EventType == "hint_request");
        int safetyEvents = events.Count(e => e.EventType == "safety_violation");
        int stepCompleteEvents = events.Count(e => e.EventType == "step_complete");
        int actionEvents = events.Count(e => e.EventType == "action");
        int correctActionEvents = events.Count(e =>
            e.EventType == "action" &&
            string.Equals(e.Target, "correct-object", StringComparison.OrdinalIgnoreCase));

        double avgScore = events
            .Where(e => e.Metrics.Score.HasValue)
            .Select(e => e.Metrics.Score!.Value)
            .DefaultIfEmpty(0.5)
            .Average();

        double avgDurationMs = events
            .Where(e => e.Metrics.DurationMs.HasValue)
            .Select(e => e.Metrics.DurationMs!.Value)
            .DefaultIfEmpty(1000)
            .Average();

        double errorRate = totalEvents > 0 ? (double)errorEvents / totalEvents : 0.0;
        double hintRate = totalEvents > 0 ? (double)hintEvents / totalEvents : 0.0;
        double safetyRate = totalEvents > 0 ? (double)safetyEvents / totalEvents : 0.0;
        double completionRate = totalEvents > 0 ? (double)stepCompleteEvents / totalEvents : 0.0;
        double correctActionRate = actionEvents > 0 ? (double)correctActionEvents / actionEvents : 0.5;

        // Normalize duration: 0 ms → 1.0 (fast), ≥ 5000 ms → 0.0 (slow).
        double paceScore = Math.Max(0.0, 1.0 - (avgDurationMs / 5000.0));

        double sequencingScore =
            stepCompleteEvents > 0
                ? Clamp(completionRate + correctActionRate * 0.4)
                : Clamp(0.5 + correctActionRate * 0.4);

        return new BehavioralFeatureSet
        {
            AttentionDetection = Clamp(1.0 - errorRate * 2),
            GoalUnderstanding = Clamp(avgScore),
            ProcedureSequencing = sequencingScore,
            PaceRegulation = Clamp(paceScore),
            SelfCorrection = Clamp(errorRate > 0 ? (1.0 - errorRate) * 0.8 : 0.7),
            FeedbackResponsiveness = Clamp(1.0 - hintRate * 3),
            SafetyCompliance = Clamp(1.0 - safetyRate * 5),
            TaskContinuity = Clamp(session.EventCount > 0 ? 0.6 + completionRate * 0.4 : 0.5)
        };
    }

    private static double Clamp(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
