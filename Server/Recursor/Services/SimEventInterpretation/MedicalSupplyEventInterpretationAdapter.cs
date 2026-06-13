using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;

// Medical-supply-stocking sim adapter.
//
// Translates the sim's domain event vocabulary into Recursor behavioral dimensions.
// Applied only when SimId == "medical-supply-stocking".
//
// Dimension mapping summary:
//   SafetyCompliance       = correct supply-quality decisions / total quality decisions
//   AttentionDetection     = correct item identifications / total identification attempts
//   GoalUnderstanding      = correct dispositions / (correct + incorrect dispositions)
//   ProcedureSequencing    = procedure-correct events / (procedure-correct + procedure-error)
//   PaceRegulation         = derived from timeSincePreviousActionSeconds or DurationMs
//   SelfCorrection         = corrected_after_feedback / feedback events; proxy from error rate otherwise
//   FeedbackResponsiveness = hint_followed / (hint_followed + hint_ignored)
//   TaskContinuity         = progress rate; boosted on scenario_completed
public class MedicalSupplyEventInterpretationAdapter : ISimEventInterpretationAdapter
{
    private static readonly HashSet<string> SafetyTriggerTypes =
    [
        MedicalSupplyEventTypes.DamagedSupplyStocked,
        MedicalSupplyEventTypes.ExpiredSupplyStocked,
        MedicalSupplyEventTypes.InspectionSkipped,
    ];

    private static readonly HashSet<string> CompletionTriggerTypes =
    [
        MedicalSupplyEventTypes.ScenarioCompleted,
    ];

    public bool AppliesTo(string simId) =>
        string.Equals(simId, "medical-supply-stocking", StringComparison.OrdinalIgnoreCase);

    public bool ShouldForceWindow(RawEventBatch batch) =>
        batch.Events.Any(e =>
            SafetyTriggerTypes.Contains(e.EventType) ||
            CompletionTriggerTypes.Contains(e.EventType));

    public BehavioralFeatureSet ExtractFeatures(SessionDocument session, List<RawEventRecord> events)
    {
        // ── Event counts ──────────────────────────────────────────────────────
        int damagedStocked    = events.Count(e => e.EventType == MedicalSupplyEventTypes.DamagedSupplyStocked);
        int expiredStocked    = events.Count(e => e.EventType == MedicalSupplyEventTypes.ExpiredSupplyStocked);
        int inspectionSkipped = events.Count(e => e.EventType == MedicalSupplyEventTypes.InspectionSkipped);
        int safetyErrors      = damagedStocked + expiredStocked + inspectionSkipped;

        int damagedRejectedOk = events.Count(e => e.EventType == MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly);
        int expiredRejectedOk = events.Count(e => e.EventType == MedicalSupplyEventTypes.ExpiredSupplyRejectedCorrectly);
        int correctRejections = damagedRejectedOk + expiredRejectedOk;

        int stockedCorrectly    = events.Count(e => e.EventType == MedicalSupplyEventTypes.SupplyStockedCorrectly);
        int usableRejected      = events.Count(e => e.EventType == MedicalSupplyEventTypes.UsableSupplyRejected);
        int wrongBinError       = events.Count(e => e.EventType == MedicalSupplyEventTypes.WrongBinError);

        int hintRequested             = events.Count(e => e.EventType == MedicalSupplyEventTypes.HintRequested);
        int hintFollowed              = events.Count(e => e.EventType == MedicalSupplyEventTypes.HintFollowed);
        int hintIgnored               = events.Count(e => e.EventType == MedicalSupplyEventTypes.HintIgnored);
        int correctedAfterFeedback    = events.Count(e => e.EventType == MedicalSupplyEventTypes.CorrectedAfterFeedback);
        int repeatedErrorAfterFeedback = events.Count(e => e.EventType == MedicalSupplyEventTypes.RepeatedErrorAfterFeedback);

        bool scenarioCompleted = events.Any(e => e.EventType == MedicalSupplyEventTypes.ScenarioCompleted);

        int totalErrors    = safetyErrors + usableRejected + wrongBinError;
        int totalSuccesses = stockedCorrectly + correctRejections;
        int totalDecisions = totalErrors + totalSuccesses;

        return new BehavioralFeatureSet
        {
            SafetyCompliance       = SafetyCompliance(safetyErrors, correctRejections),
            AttentionDetection     = AttentionDetection(safetyErrors, usableRejected, correctRejections, stockedCorrectly),
            GoalUnderstanding      = GoalUnderstanding(stockedCorrectly, correctRejections, usableRejected, wrongBinError),
            ProcedureSequencing    = ProcedureSequencing(wrongBinError, inspectionSkipped, usableRejected, stockedCorrectly, correctRejections),
            PaceRegulation         = PaceRegulation(events),
            SelfCorrection         = SelfCorrection(correctedAfterFeedback, repeatedErrorAfterFeedback, totalErrors, totalDecisions),
            FeedbackResponsiveness = FeedbackResponsiveness(hintFollowed, hintIgnored, hintRequested),
            TaskContinuity         = TaskContinuity(totalSuccesses, totalErrors, scenarioCompleted),
        };
    }

    // ── Dimension formulas ────────────────────────────────────────────────────

    // Of all supply-quality decisions in this window, what fraction were correct?
    // safetyErrors  = stocking a damaged/expired supply (wrong decision)
    // correctRejections = correctly refusing a damaged/expired supply (right decision)
    private static double SafetyCompliance(int safetyErrors, int correctRejections)
    {
        int total = safetyErrors + correctRejections;
        if (total == 0)
            return 0.75; // no supply-quality decisions available — neutral
        return Clamp((double)correctRejections / total);
    }

    // Of all item-identification attempts, what fraction were correct?
    // Errors: stocking damaged/expired (type-II miss) OR rejecting usable (type-I false positive)
    // Successes: correctly rejecting bad supply OR correctly stocking a good one
    private static double AttentionDetection(
        int safetyErrors, int usableRejected, int correctRejections, int stockedCorrectly)
    {
        int errors    = safetyErrors + usableRejected;
        int successes = correctRejections + stockedCorrectly;
        int total     = errors + successes;
        if (total == 0) return 0.75;
        return Clamp((double)successes / total);
    }

    // Did the learner understand what to do with each supply?
    // Errors: rejected a usable supply (wrong call) OR chose the wrong bin (right item, wrong place)
    private static double GoalUnderstanding(
        int stockedCorrectly, int correctRejections, int usableRejected, int wrongBinError)
    {
        int successes = stockedCorrectly + correctRejections;
        int errors    = usableRejected + wrongBinError;
        int total     = successes + errors;
        if (total == 0) return 0.5;
        return Clamp((double)successes / total);
    }

    // Did the learner follow the correct procedure?
    // Errors: wrong bin (right decision, wrong execution) + inspection skipped + usable rejected
    private static double ProcedureSequencing(
        int wrongBinError, int inspectionSkipped, int usableRejected,
        int stockedCorrectly, int correctRejections)
    {
        int errors    = wrongBinError + inspectionSkipped + usableRejected;
        int successes = stockedCorrectly + correctRejections;
        int total     = errors + successes;
        if (total < 2) return 0.5; // too few procedure events for reliable assessment
        return Clamp((double)successes / total);
    }

    // Normalize action latency: 0 s → 1.0 (fast); ≥ 30 s → 0.0 (very slow).
    // Typical medical-supply inspection: 5–15 seconds.
    internal static double PaceRegulation(List<RawEventRecord> events)
    {
        var latencies = events
            .Where(e => e.Metrics.AdditionalMetrics?
                         .ContainsKey(RecursorCommonMetricKeys.TimeSincePreviousActionSec) == true)
            .Select(e => e.Metrics.AdditionalMetrics![RecursorCommonMetricKeys.TimeSincePreviousActionSec])
            .ToList();

        if (latencies.Count > 0)
            return Clamp(1.0 - latencies.Average() / 30.0);

        var durations = events
            .Where(e => e.Metrics.DurationMs.HasValue)
            .Select(e => e.Metrics.DurationMs!.Value)
            .ToList();

        if (durations.Count > 0)
            return Clamp(1.0 - durations.Average() / 30_000.0);

        return 0.65; // neutral default — no timing data available
    }

    // Response to feedback: explicit hint follow/ignore ratio when available;
    // falls back to error-rate proxy.
    private static double SelfCorrection(
        int correctedAfterFeedback, int repeatedErrorAfterFeedback,
        int totalErrors, int totalDecisions)
    {
        int feedbackEvents = correctedAfterFeedback + repeatedErrorAfterFeedback;
        if (feedbackEvents > 0)
            return Clamp((double)correctedAfterFeedback / feedbackEvents);

        // No explicit feedback events — proxy from error rate
        if (totalDecisions == 0) return 0.7;
        double errorRate = (double)totalErrors / totalDecisions;
        return Clamp(errorRate > 0 ? (1.0 - errorRate) * 0.8 : 0.7);
    }

    // Response to hints: ratio of hints followed vs ignored when engagement present.
    private static double FeedbackResponsiveness(int hintFollowed, int hintIgnored, int hintRequested)
    {
        int engagement = hintFollowed + hintIgnored;
        if (engagement > 0)
            return Clamp((double)hintFollowed / engagement);

        // No hint follow-through data — if hints were only requested, moderately neutral
        return hintRequested > 0 ? 0.60 : 0.70;
    }

    // Session-level progress: boosted when the scenario is completed.
    private static double TaskContinuity(int totalSuccesses, int totalErrors, bool scenarioCompleted)
    {
        if (scenarioCompleted)
        {
            int total = totalSuccesses + totalErrors;
            double correctRate = total > 0 ? (double)totalSuccesses / total : 0.7;
            return Clamp(0.65 + correctRate * 0.30);
        }

        int decisions = totalSuccesses + totalErrors;
        if (decisions == 0) return 0.5;
        double progressRate = (double)totalSuccesses / decisions;
        return Clamp(0.40 + progressRate * 0.40);
    }

    private static double Clamp(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
