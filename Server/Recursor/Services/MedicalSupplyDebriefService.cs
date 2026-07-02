using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using OpenAI.Chat;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── Internal service models ───────────────────────────────────────────────────

public class MedicalSupplySessionSummary
{
    public string SessionId { get; set; } = "";
    public double PerformanceScore { get; set; }
    public string ErrorsText { get; set; } = "";
    public MedicalSupplyRawEventCounts EventCounts { get; set; } = new();
    public MedicalSupplyBehaviorSummary? BehaviorSummary { get; set; }
    public List<string> HypothesisLabels { get; set; } = [];
    public List<string> InterventionFamilies { get; set; } = [];
    // Populated from Unity-provided CurrentSummary DTO; null when built from ADX.
    public int? TotalErrors { get; set; }
    public int? TotalActions { get; set; }
}

public class MedicalSupplyTrendDeltas
{
    public bool HasPreviousData { get; set; }
    public int? WrongBinErrorDelta { get; set; }
    public int? DamagedSupplyStockedDelta { get; set; }
    public int? UsableSupplyRejectedDelta { get; set; }
    public int? SafetyErrorDelta { get; set; }
    public int? SuccessDelta { get; set; }
    public double? AvgConfusionDelta { get; set; }
    public double? AvgSafetyComplianceDelta { get; set; }
    public double? AvgGoalUnderstandingDelta { get; set; }
    public string? DominantStateChange { get; set; }
}

// ── Pure helper — testable without DI ────────────────────────────────────────

public static class MedicalSupplyDebriefHelper
{
    // Mirrors the KQL countif logic from AdxMedicalSupplyDebriefQueryService.
    // Used in tests to verify counting against in-memory event lists.
    public static MedicalSupplyRawEventCounts CountEventsFromRecords(
        IEnumerable<Models.RawEventRecord> events)
    {
        var list = events.ToList();
        return new MedicalSupplyRawEventCounts
        {
            WrongBinErrors       = list.Count(e => e.EventType == MedicalSupplyEventTypes.WrongBinError),
            DamagedSupplyStocked = list.Count(e => e.EventType == MedicalSupplyEventTypes.DamagedSupplyStocked),
            UsableSupplyRejected = list.Count(e => e.EventType == MedicalSupplyEventTypes.UsableSupplyRejected),
            SafetyErrors = list.Count(e =>
                e.EventType == MedicalSupplyEventTypes.DamagedSupplyStocked ||
                e.EventType == MedicalSupplyEventTypes.ExpiredSupplyStocked ||
                e.EventType == MedicalSupplyEventTypes.InspectionSkipped),
            SuccessCount = list.Count(e =>
                e.EventType == MedicalSupplyEventTypes.SupplyStockedCorrectly ||
                e.EventType == MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly ||
                e.EventType == MedicalSupplyEventTypes.ExpiredSupplyRejectedCorrectly),
        };
    }

    public static MedicalSupplyTrendDeltas ComputeDeltas(
        MedicalSupplySessionSummary current,
        MedicalSupplySessionSummary? previous)
    {
        if (previous is null)
            return new MedicalSupplyTrendDeltas { HasPreviousData = false };

        var d = new MedicalSupplyTrendDeltas { HasPreviousData = true };

        d.WrongBinErrorDelta        = current.EventCounts.WrongBinErrors - previous.EventCounts.WrongBinErrors;
        d.DamagedSupplyStockedDelta = current.EventCounts.DamagedSupplyStocked - previous.EventCounts.DamagedSupplyStocked;
        d.UsableSupplyRejectedDelta = current.EventCounts.UsableSupplyRejected - previous.EventCounts.UsableSupplyRejected;
        d.SafetyErrorDelta          = current.EventCounts.SafetyErrors - previous.EventCounts.SafetyErrors;
        d.SuccessDelta              = current.EventCounts.SuccessCount - previous.EventCounts.SuccessCount;

        if (current.BehaviorSummary is not null && previous.BehaviorSummary is not null)
        {
            d.AvgConfusionDelta         = current.BehaviorSummary.AvgConfusion - previous.BehaviorSummary.AvgConfusion;
            d.AvgSafetyComplianceDelta  = current.BehaviorSummary.AvgSafetyCompliance - previous.BehaviorSummary.AvgSafetyCompliance;
            d.AvgGoalUnderstandingDelta = current.BehaviorSummary.AvgGoalUnderstanding - previous.BehaviorSummary.AvgGoalUnderstanding;

            var prev = previous.BehaviorSummary.DominantState;
            var curr = current.BehaviorSummary.DominantState;
            if (!string.IsNullOrEmpty(prev) && !string.IsNullOrEmpty(curr) && prev != curr)
                d.DominantStateChange = $"{prev} → {curr}";
        }

        return d;
    }

    // Heuristic trend classification based on computed deltas.
    // Negative error deltas = improvement; positive behavior score deltas = improvement.
    public static string ComputeOverallTrend(MedicalSupplyTrendDeltas deltas)
    {
        if (!deltas.HasPreviousData)
            return "insufficient_data";

        int improved = 0;
        int worsened = 0;

        if (deltas.SafetyErrorDelta < 0) improved++;
        if (deltas.SafetyErrorDelta > 0) worsened++;

        if (deltas.SuccessDelta > 0) improved++;
        if (deltas.SuccessDelta < 0) worsened++;

        if (deltas.AvgConfusionDelta < -0.05) improved++;
        if (deltas.AvgConfusionDelta > 0.05) worsened++;

        if (deltas.AvgSafetyComplianceDelta > 0.05) improved++;
        if (deltas.AvgSafetyComplianceDelta < -0.05) worsened++;

        if (deltas.AvgGoalUnderstandingDelta > 0.05) improved++;
        if (deltas.AvgGoalUnderstandingDelta < -0.05) worsened++;

        if (improved == 0 && worsened == 0)
            return "mixed";
        if (improved > worsened * 2)
            return "improved";
        if (worsened > improved * 2)
            return "worsened";
        return "mixed";
    }

    // Builds a compact JSON-serializable payload for the GPT prompt.
    // Deltas and computed trend are pre-calculated by code; GPT explains them.
    // No raw event arrays are included — only counts and averages.
    public static object BuildPromptPayload(
        MedicalSupplySessionSummary current,
        MedicalSupplySessionSummary? previous,
        MedicalSupplyTrendDeltas deltas,
        string overallTrend)
    {
        return new
        {
            overallTrend,
            currentSession = new
            {
                current.SessionId,
                current.PerformanceScore,
                current.ErrorsText,
                wrongBinErrors        = current.EventCounts.WrongBinErrors,
                damagedSupplyStocked  = current.EventCounts.DamagedSupplyStocked,
                usableSupplyRejected  = current.EventCounts.UsableSupplyRejected,
                safetyErrors          = current.EventCounts.SafetyErrors,
                successCount          = current.EventCounts.SuccessCount,
                totalErrors           = current.TotalErrors,
                totalActions          = current.TotalActions,
                avgConfusion          = current.BehaviorSummary?.AvgConfusion,
                avgSafetyCompliance   = current.BehaviorSummary?.AvgSafetyCompliance,
                avgGoalUnderstanding  = current.BehaviorSummary?.AvgGoalUnderstanding,
                dominantState         = current.BehaviorSummary?.DominantState,
                hypothesisLabels      = current.HypothesisLabels,
                interventionFamilies  = current.InterventionFamilies,
            },
            previousSession = previous is null ? (object?)null : new
            {
                previous.SessionId,
                previous.PerformanceScore,
                wrongBinErrors        = previous.EventCounts.WrongBinErrors,
                damagedSupplyStocked  = previous.EventCounts.DamagedSupplyStocked,
                usableSupplyRejected  = previous.EventCounts.UsableSupplyRejected,
                safetyErrors          = previous.EventCounts.SafetyErrors,
                successCount          = previous.EventCounts.SuccessCount,
                avgConfusion          = previous.BehaviorSummary?.AvgConfusion,
                avgSafetyCompliance   = previous.BehaviorSummary?.AvgSafetyCompliance,
                avgGoalUnderstanding  = previous.BehaviorSummary?.AvgGoalUnderstanding,
                dominantState         = previous.BehaviorSummary?.DominantState,
                hypothesisLabels      = previous.HypothesisLabels,
            },
            trendDeltas = new
            {
                deltas.HasPreviousData,
                wrongBinErrorDelta        = deltas.WrongBinErrorDelta,
                damagedSupplyStockedDelta = deltas.DamagedSupplyStockedDelta,
                usableSupplyRejectedDelta = deltas.UsableSupplyRejectedDelta,
                safetyErrorDelta          = deltas.SafetyErrorDelta,
                successDelta              = deltas.SuccessDelta,
                avgConfusionDelta         = deltas.AvgConfusionDelta,
                avgSafetyComplianceDelta  = deltas.AvgSafetyComplianceDelta,
                avgGoalUnderstandingDelta = deltas.AvgGoalUnderstandingDelta,
                dominantStateChange       = deltas.DominantStateChange,
            },
        };
    }

    public static List<string> ExtractJsonStringArray(JsonElement element)
    {
        var result = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s))
                    result.Add(s);
            }
        }
        return result;
    }

    public static List<string> ExtractHypothesisLabels(JsonElement hypotheses)
    {
        var result = new List<string>();
        if (hypotheses.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var h in hypotheses.EnumerateArray())
        {
            // ADX dynamic columns may use either capitalisation.
            if (h.TryGetProperty("label", out var lp) || h.TryGetProperty("Label", out lp))
            {
                var s = lp.GetString();
                if (!string.IsNullOrEmpty(s))
                    result.Add(s);
            }
        }
        return result;
    }
}

// ── Service interface and implementation ─────────────────────────────────────

public interface IMedicalSupplyDebriefService
{
    Task<TrendAwareDebriefResponse> GenerateTrendAwareDebriefAsync(TrendAwareDebriefRequest request);
}

public class MedicalSupplyDebriefService : IMedicalSupplyDebriefService
{
    private readonly IAdxMedicalSupplyDebriefQueryService _debriefQuery;
    private readonly IAdxRecursorQueryService _recursorQuery;
    private readonly ILogger<MedicalSupplyDebriefService> _logger;

    private const string AzureOpenAiEndpoint   = "https://manuscriptgenerator.openai.azure.com/";
    private const string AzureOpenAiKey        = "F8fFcrOkGNjJFbL710c19YIU6Vq1H0sP0ifcZ0bM4eAJvZwT4FxHJQQJ99BLACYeBjFXJ3w3AAABACOGhJoF";
    private const string AzureOpenAiDeployment = "gpt-5.4-mini";

    private const string SystemPrompt = """
You are a supportive training coach for a medical-supply stocking simulation.

Use only the evidence provided in the payload. Do not invent actions not supported by the metrics.
Be encouraging but specific. Avoid shaming. Mention improvement clearly when present.
Compare current and previous performance when previousSession data is available.
This is simulation performance coaching — do not give medical advice.

The payload contains:
- overallTrend: pre-computed trend classification ("improved", "mixed", "worsened", or "insufficient_data"). Use it as-is in your response.
- currentSession: summary of this shift (counts, averages, hypothesis labels from the adaptive engine).
- previousSession: summary of the prior shift, or null if no comparison data exists.
- trendDeltas: arithmetic differences computed by code. Negative error deltas mean fewer errors (improvement). Positive behavior score deltas mean higher scores (improvement).

Return valid JSON only — no markdown, no extra commentary — with exactly these fields:
{
  "headline": "one sentence summarizing the session",
  "overallTrend": "copy from payload overallTrend",
  "whatWentWell": ["up to 3 items"],
  "needsImprovement": ["up to 3 items"],
  "trendComparison": "one or two sentences comparing to previous session, or acknowledge it is the first session",
  "nextShiftFocus": ["1-2 concrete focus items for next session"],
  "coachMessage": "brief encouraging message",
  "confidenceNote": "short note on data completeness and confidence",
  "evidenceSummary": ["2-4 specific metrics or facts from the payload that support the assessment"]
}
""";

    public MedicalSupplyDebriefService(
        IAdxMedicalSupplyDebriefQueryService debriefQuery,
        IAdxRecursorQueryService recursorQuery,
        ILogger<MedicalSupplyDebriefService> logger)
    {
        _debriefQuery  = debriefQuery;
        _recursorQuery = recursorQuery;
        _logger        = logger;
    }

    public async Task<TrendAwareDebriefResponse> GenerateTrendAwareDebriefAsync(TrendAwareDebriefRequest request)
    {
        var current = request.CurrentSummary is not null
            ? await BuildSessionSummaryFromDtoAsync(
                request.CurrentSummary, request.CurrentSessionId, request.PerformanceScore, request.ErrorsText)
            : await BuildSessionSummaryAsync(
                request.CurrentSessionId, request.PerformanceScore, request.ErrorsText);

        MedicalSupplySessionSummary? previous = null;
        if (request.IncludePreviousSession)
        {
            var prevId = !string.IsNullOrWhiteSpace(request.PreviousSessionId)
                ? request.PreviousSessionId
                : await _debriefQuery.GetPreviousSessionIdAsync(
                    request.UserId, request.SimId, request.ScenarioId, request.CurrentSessionId);

            if (!string.IsNullOrEmpty(prevId))
                previous = await BuildSessionSummaryAsync(prevId, 0, "");
        }

        var deltas      = MedicalSupplyDebriefHelper.ComputeDeltas(current, previous);
        var overallTrend = MedicalSupplyDebriefHelper.ComputeOverallTrend(deltas);

        return await CallGptAsync(current, previous, deltas, overallTrend);
    }

    // ── Session summary builders ──────────────────────────────────────────────

    // Builds the current-session summary from Unity-provided DTO counts, avoiding
    // ADX raw-event and behavior queries that may not have data yet due to ingestion
    // delay. Hypothesis labels and intervention families are best-effort only;
    // failure to load them should never fail the debrief endpoint.
    private async Task<MedicalSupplySessionSummary> BuildSessionSummaryFromDtoAsync(
        MedicalSupplyCurrentSessionSummaryDto dto,
        string sessionId,
        double performanceScore,
        string errorsText)
    {
        var eventCounts = new MedicalSupplyRawEventCounts
        {
            WrongBinErrors = dto.WrongBinErrors,
            DamagedSupplyStocked = dto.DamagedSupplyStocked,
            UsableSupplyRejected = dto.UsableSupplyRejected,
            SafetyErrors = dto.SafetyErrors,
            SuccessCount = dto.SuccessCount,
        };

        MedicalSupplyBehaviorSummary? behaviorSummary = null;

        if (dto.AvgConfusion.HasValue
            || dto.AvgSafetyCompliance.HasValue
            || dto.AvgGoalUnderstanding.HasValue
            || !string.IsNullOrWhiteSpace(dto.LatestState))
        {
            behaviorSummary = new MedicalSupplyBehaviorSummary
            {
                AvgConfusion = dto.AvgConfusion ?? 0,
                AvgSafetyCompliance = dto.AvgSafetyCompliance ?? 0,
                AvgGoalUnderstanding = dto.AvgGoalUnderstanding ?? 0,
                DominantState = dto.LatestState ?? "",
                WindowCount = 0,
            };
        }

        // Best-effort enrichment. These should not be allowed to fail the debrief.
        var hypothesisLabels = await TryGetHypothesisLabelsAsync(sessionId);
        var interventionFamilies = await TryGetInterventionFamiliesAsync(sessionId);

        return new MedicalSupplySessionSummary
        {
            SessionId = sessionId,
            PerformanceScore = performanceScore,
            ErrorsText = errorsText,
            EventCounts = eventCounts,
            BehaviorSummary = behaviorSummary,
            HypothesisLabels = hypothesisLabels,
            InterventionFamilies = interventionFamilies,
            TotalErrors = dto.TotalErrors,
            TotalActions = dto.TotalActions,
        };
    }

    private async Task<MedicalSupplySessionSummary> BuildSessionSummaryAsync(
        string sessionId,
        double performanceScore,
        string errorsText)
    {
        // These debrief-specific ADX query methods already catch internally and
        // return empty/null summaries when unavailable.
        var eventCounts = await _debriefQuery.GetRawEventCountsAsync(sessionId);
        var behaviorSummary = await _debriefQuery.GetBehaviorSummaryAsync(sessionId);

        // Best-effort enrichment. These should not be allowed to fail the debrief.
        var hypothesisLabels = await TryGetHypothesisLabelsAsync(sessionId);
        var interventionFamilies = await TryGetInterventionFamiliesAsync(sessionId);

        return new MedicalSupplySessionSummary
        {
            SessionId = sessionId,
            PerformanceScore = performanceScore,
            ErrorsText = errorsText,
            EventCounts = eventCounts,
            BehaviorSummary = behaviorSummary,
            HypothesisLabels = hypothesisLabels,
            InterventionFamilies = interventionFamilies,
        };
    }

    // ── GPT call ─────────────────────────────────────────────────────────────

    private async Task<TrendAwareDebriefResponse> CallGptAsync(
        MedicalSupplySessionSummary current,
        MedicalSupplySessionSummary? previous,
        MedicalSupplyTrendDeltas deltas,
        string overallTrend)
    {
        var payload = MedicalSupplyDebriefHelper.BuildPromptPayload(current, previous, deltas, overallTrend);
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

        try
        {
            var credential   = new AzureKeyCredential(AzureOpenAiKey);
            var openAiClient = new AzureOpenAIClient(new Uri(AzureOpenAiEndpoint), credential);
            var chatClient   = openAiClient.GetChatClient(AzureOpenAiDeployment);

            ChatMessage[] messages =
            [
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage(payloadJson),
            ];

            ChatCompletion completion = chatClient.CompleteChat(messages);
            string raw = completion.Content[0].Text;

            _logger.LogDebug("GPT debrief raw response for session '{SessionId}': {Raw}",
                current.SessionId, raw);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<TrendAwareDebriefResponse>(raw, opts);

            if (response is null)
            {
                _logger.LogWarning("GPT debrief response deserialized to null for session '{SessionId}'.",
                    current.SessionId);
                return FallbackResponse(overallTrend, deltas, current);
            }

            // Trust code-computed trend over GPT's copy.
            response.OverallTrend   = overallTrend;
            response.WhatWentWell   ??= [];
            response.NeedsImprovement ??= [];
            response.NextShiftFocus ??= [];
            response.EvidenceSummary ??= [];
            response.RawModelJson   = raw;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPT debrief call failed for session '{SessionId}'.", current.SessionId);
            return FallbackResponse(overallTrend, deltas, current);
        }
    }

    private static TrendAwareDebriefResponse FallbackResponse(
        string overallTrend,
        MedicalSupplyTrendDeltas deltas,
        MedicalSupplySessionSummary current)
    {
        var evidence = new List<string>
        {
            $"Safety errors: {current.EventCounts.SafetyErrors}",
            $"Correct stocking actions: {current.EventCounts.SuccessCount}",
            $"Wrong-bin errors: {current.EventCounts.WrongBinErrors}",
        };
        if (deltas.HasPreviousData && deltas.SafetyErrorDelta.HasValue)
            evidence.Add($"Safety error change vs previous session: {deltas.SafetyErrorDelta:+#;-#;0}");

        return new TrendAwareDebriefResponse
        {
            Headline        = "Session Complete",
            OverallTrend    = overallTrend,
            WhatWentWell    = [],
            NeedsImprovement = [],
            TrendComparison = deltas.HasPreviousData
                ? "Comparison with a previous session is available."
                : "No previous session found for comparison.",
            NextShiftFocus  = [],
            CoachMessage    = "Review your session data and focus on supply inspection before stocking.",
            ConfidenceNote  = "Coaching narrative unavailable — please try again.",
            EvidenceSummary = evidence,
        };
    }

    private async Task<List<string>> TryGetHypothesisLabelsAsync(string sessionId)
    {
        try
        {
            var hypothesisRows = await _recursorQuery.GetLatestHypothesisSetsAsync(sessionId, 3);

            return hypothesisRows
                .SelectMany(r => MedicalSupplyDebriefHelper.ExtractHypothesisLabels(r.Hypotheses))
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load hypothesis labels for debrief session '{SessionId}'. Continuing without them.",
                sessionId);

            return [];
        }
    }

    private async Task<List<string>> TryGetInterventionFamiliesAsync(string sessionId)
    {
        try
        {
            var adaptationRows = await _recursorQuery.GetLatestAdaptationDecisionsAsync(sessionId, 1);

            return adaptationRows
                .SelectMany(r => MedicalSupplyDebriefHelper.ExtractJsonStringArray(r.InterventionFamilies))
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not load intervention families for debrief session '{SessionId}'. Continuing without them.",
                sessionId);

            return [];
        }
    }
}
