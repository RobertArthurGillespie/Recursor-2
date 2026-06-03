using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IRecursorSimContractValidator
{
    // Inspects a RawEventBatch for contract compliance.
    // Returns warnings (not hard failures) — never rejects the batch from the production pipeline.
    SimBatchValidationResult Validate(RawEventBatch batch);
}

// Stateless diagnostic validator. Safe to register as singleton.
// Does not ingest, persist, or modify any data.
public class RecursorSimContractValidator : IRecursorSimContractValidator
{
    public SimBatchValidationResult Validate(RawEventBatch batch)
    {
        var warnings = new List<string>();

        // ── Batch-level identity checks ───────────────────────────────────────
        if (string.IsNullOrWhiteSpace(batch.SimId))
            warnings.Add("SimId is missing or empty.");
        if (string.IsNullOrWhiteSpace(batch.SimVersion))
            warnings.Add("SimVersion is missing or empty.");
        if (string.IsNullOrWhiteSpace(batch.ScenarioId))
            warnings.Add("ScenarioId is missing or empty.");
        if (string.IsNullOrWhiteSpace(batch.UserId))
            warnings.Add("UserId is missing or empty.");
        if (string.IsNullOrWhiteSpace(batch.SessionId))
            warnings.Add("SessionId is missing or empty.");

        // ── Per-event checks ──────────────────────────────────────────────────
        int missingEventType = 0;
        int missingCategory  = 0;
        int missingTarget    = 0;

        foreach (var evt in batch.Events)
        {
            if (string.IsNullOrWhiteSpace(evt.EventType)) missingEventType++;
            if (string.IsNullOrWhiteSpace(evt.Category))  missingCategory++;
            if (string.IsNullOrWhiteSpace(evt.Target))    missingTarget++;

            // isCorrect expected on success and error events
            if (evt.Category is RecursorEventCategories.Success or RecursorEventCategories.Error)
            {
                if (!HasAdditionalMetric(evt, RecursorCommonMetricKeys.IsCorrect))
                    warnings.Add(
                        $"Event '{evt.EventId}' (category={evt.Category}) is missing " +
                        $"recommended metric '{RecursorCommonMetricKeys.IsCorrect}'.");
            }

            // errorSeverity expected on error and safety_error events
            if (evt.Category is RecursorEventCategories.Error or RecursorEventCategories.SafetyError)
            {
                if (!HasAdditionalMetric(evt, RecursorCommonMetricKeys.ErrorSeverity))
                    warnings.Add(
                        $"Event '{evt.EventId}' (category={evt.Category}) is missing " +
                        $"recommended metric '{RecursorCommonMetricKeys.ErrorSeverity}'.");
            }

            // safetyCritical expected specifically on safety_error events
            if (evt.Category == RecursorEventCategories.SafetyError)
            {
                if (!HasAdditionalMetric(evt, RecursorCommonMetricKeys.SafetyCritical))
                    warnings.Add(
                        $"Event '{evt.EventId}' (category=safety_error) is missing " +
                        $"recommended metric '{RecursorCommonMetricKeys.SafetyCritical}'.");
            }
        }

        if (missingEventType > 0)
            warnings.Add($"{missingEventType} event(s) are missing EventType.");
        if (missingCategory > 0)
            warnings.Add($"{missingCategory} event(s) are missing Category.");
        if (missingTarget > 0)
            warnings.Add($"{missingTarget} event(s) are missing Target.");

        // ── Batch-level context check ─────────────────────────────────────────
        // If any event carries context, expect recommended keys to appear somewhere in the batch.
        if (batch.Events.Any(e => e.Context.Count > 0))
        {
            if (!batch.Events.Any(e => e.Context.ContainsKey(RecursorCommonContextKeys.CurrentDifficulty)))
                warnings.Add(
                    $"No event in the batch includes the recommended context key " +
                    $"'{RecursorCommonContextKeys.CurrentDifficulty}'.");

            if (!batch.Events.Any(e => e.Context.ContainsKey(RecursorCommonContextKeys.CurrentHintMode)))
                warnings.Add(
                    $"No event in the batch includes the recommended context key " +
                    $"'{RecursorCommonContextKeys.CurrentHintMode}'.");
        }

        // ── Aggregated counts ─────────────────────────────────────────────────
        var categoryCounts = batch.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Category))
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        var eventTypeCounts = batch.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.EventType))
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        // ── Shadow-mode gate ──────────────────────────────────────────────────
        // A batch is valid enough for shadow mode if the four key identity fields are
        // present and at least one event has an EventType.
        var isValidEnough =
            !string.IsNullOrWhiteSpace(batch.SimId)     &&
            !string.IsNullOrWhiteSpace(batch.SessionId) &&
            !string.IsNullOrWhiteSpace(batch.UserId)    &&
            batch.Events.Any(e => !string.IsNullOrWhiteSpace(e.EventType));

        return new SimBatchValidationResult
        {
            IsValidEnoughForShadowMode = isValidEnough,
            Warnings                   = warnings,
            DetectedSimId              = batch.SimId,
            DetectedScenarioId         = batch.ScenarioId,
            EventCount                 = batch.Events.Count,
            CategoryCounts             = categoryCounts,
            EventTypeCounts            = eventTypeCounts,
        };
    }

    private static bool HasAdditionalMetric(RawEventRecord evt, string key) =>
        evt.Metrics.AdditionalMetrics?.ContainsKey(key) == true;
}
