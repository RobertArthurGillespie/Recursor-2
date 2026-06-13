using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;

// Generic adapter for sims that emit the standard Recursor event vocabulary:
//   error, safety_violation, hint_request, step_complete, action
// This is the existing extraction logic, extracted verbatim into the adapter contract.
public class DefaultSimEventInterpretationAdapter : ISimEventInterpretationAdapter
{
    private static readonly HashSet<string> SafetyTriggerTypes = ["safety_violation"];

    public bool AppliesTo(string simId) => true; // default for any unrecognised sim

    public bool ShouldForceWindow(RawEventBatch batch) =>
        batch.Events.Any(e => SafetyTriggerTypes.Contains(e.EventType));

    public BehavioralFeatureSet ExtractFeatures(SessionDocument session, List<RawEventRecord> events)
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

        double paceScore = Math.Max(0.0, 1.0 - (avgDurationMs / 5000.0));

        double sequencingScore =
            stepCompleteEvents > 0
                ? Clamp(completionRate + correctActionRate * 0.4)
                : Clamp(0.5 + correctActionRate * 0.4);

        return new BehavioralFeatureSet
        {
            AttentionDetection     = Clamp(1.0 - errorRate * 2),
            GoalUnderstanding      = Clamp(avgScore),
            ProcedureSequencing    = sequencingScore,
            PaceRegulation         = Clamp(paceScore),
            SelfCorrection         = Clamp(errorRate > 0 ? (1.0 - errorRate) * 0.8 : 0.7),
            FeedbackResponsiveness = Clamp(1.0 - hintRate * 3),
            SafetyCompliance       = Clamp(1.0 - safetyRate * 5),
            TaskContinuity         = Clamp(session.EventCount > 0 ? 0.6 + completionRate * 0.4 : 0.5)
        };
    }

    private static double Clamp(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
