using System.Text.Json;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── User Relative Policy Advisor Service ──────────────────────────────────────
// Compares the latest actual adaptation decision against user-relative signals
// to produce a shadow baseline-aware recommendation.
//
// OBSERVATIONAL ONLY — does not touch the live adaptation pipeline.
// The shadow decision is computed for inspection and divergence analysis only.
//
// Rules are explicit and deterministic. No ML or speculative logic.
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserRelativePolicyAdvisorService
{
    /// <summary>
    /// Returns a PersonalizedPolicyShadowDecision comparing the user's latest
    /// actual adaptation decision against a baseline-aware shadow recommendation.
    /// Returns <c>null</c> when no user-relative signals or no actual decisions
    /// exist for the user, or when ADX is not configured.
    /// </summary>
    Task<PersonalizedPolicyShadowDecision?> GetShadowDecisionAsync(string userId);
}

public class UserRelativePolicyAdvisorService : IUserRelativePolicyAdvisorService
{
    // Mirrors the threshold used in UserRelativeSignalService.
    private const double FlagThreshold = 0.10;

    private readonly IUserRelativeSignalService _signalService;
    private readonly IAdxRecursorQueryService _queryService;
    private readonly ILogger<UserRelativePolicyAdvisorService> _logger;

    public UserRelativePolicyAdvisorService(
        IUserRelativeSignalService signalService,
        IAdxRecursorQueryService queryService,
        ILogger<UserRelativePolicyAdvisorService> logger)
    {
        _signalService = signalService;
        _queryService  = queryService;
        _logger        = logger;
    }

    public async Task<PersonalizedPolicyShadowDecision?> GetShadowDecisionAsync(string userId)
    {
        // Signals are fetched first — SessionId and CreatedAtUtc are needed to anchor
        // the decision lookup to the correct session context.
        var signals = await _signalService.GetUserRelativeSignalsAsync(userId);

        if (signals is null)
        {
            _logger.LogInformation(
                "Shadow policy advisor: no user-relative signals for userId '{UserId}'.", userId);
            return null;
        }

        // Strategy A/B: best-matching decision in the same session as the signals.
        // A = at or before signal timestamp; B = after (logged inside query service).
        var actual = await _queryService.GetBestMatchingDecisionForSignalsAsync(
            userId, signals.SessionId, signals.CreatedAtUtc);

        // Strategy C fallback: no decision found in the session — try user-latest.
        if (actual is null)
        {
            _logger.LogDebug(
                "Shadow policy advisor: no session-scoped decision found for " +
                "userId '{UserId}', sessionId '{SessionId}' — falling back to user-latest (strategy C).",
                userId, signals.SessionId);

            var userDecisions = await _queryService.GetLatestAdaptationDecisionsByUserAsync(userId, 1);
            actual = userDecisions.FirstOrDefault();
        }

        if (actual is null)
        {
            _logger.LogInformation(
                "Shadow policy advisor: no actual adaptation decisions for userId '{UserId}'.", userId);
            return null;
        }

        // Deserialize the JsonElement fields from the ADX row.
        var actualFamilies = DeserializeStringList(actual.InterventionFamilies);
        var actualChanges  = DeserializeParameterChanges(actual.ParameterChanges);

        // ── Shadow heuristic rules ─────────────────────────────────────────────
        // Start from the actual families and apply baseline-aware adjustments.
        var shadowFamilies = new List<string>(actualFamilies);
        var shadowReasons  = new List<string>();

        if (signals.IsAboveBaselineConfusion || signals.IsAboveBaselineHintDependence)
        {
            // User is struggling more than their own historical baseline.
            // Shadow: preserve or increase support; do not reduce hints or escalate difficulty.
            bool removedHintReduction =
                shadowFamilies.Remove("hint-fade")
                | shadowFamilies.Remove("hint-remove")
                | shadowFamilies.Remove("hint-reduction");

            // Also drop challenge-escalation families — user is not ready for more pressure.
            bool removedChallenge =
                shadowFamilies.Remove("difficulty-increase")
                | shadowFamilies.Remove("pace-increase");

            bool hasHintSupport =
                shadowFamilies.Contains("scaffold-hints")
                || shadowFamilies.Contains("hint-restore-minimal");

            if (!hasHintSupport)
            {
                shadowFamilies.Add("scaffold-hints");
                shadowReasons.Add("added scaffold-hints: confusion or hint-dependence above baseline");
            }
            else if (removedHintReduction)
            {
                shadowReasons.Add("blocked hint-reduction families: confusion or hint-dependence above baseline");
            }

            if (removedChallenge)
                shadowReasons.Add("removed difficulty/pace escalation: confusion or hint-dependence above baseline");

            if (!shadowFamilies.Contains("difficulty-reduction"))
            {
                shadowFamilies.Add("difficulty-reduction");
                shadowReasons.Add("added difficulty-reduction: confusion or hint-dependence above baseline");
            }
        }
        else if (signals.ConfusionDelta      < -FlagThreshold
              && signals.HintDependenceDelta < -FlagThreshold
              && !signals.IsBelowBaselineGoalUnderstanding
              && !signals.IsBelowBaselineAttention)
        {
            // User is performing notably better than their own baseline on all key signals.
            // Shadow: allow more challenge; reduce unnecessary support scaffolding.
            shadowFamilies.Remove("scaffold-hints");
            shadowFamilies.Remove("difficulty-reduction");

            bool alreadyFading =
                shadowFamilies.Contains("hint-fade")
                || shadowFamilies.Contains("hint-remove");

            if (!alreadyFading)
            {
                shadowFamilies.Add("hint-fade");
                shadowReasons.Add("added hint-fade: confusion and hint-dependence below baseline");
            }

            if (!shadowFamilies.Contains("difficulty-increase"))
            {
                shadowFamilies.Add("difficulty-increase");
                shadowReasons.Add("added difficulty-increase: confusion and hint-dependence below baseline");
            }
        }

        // ── Derive shadow parameter changes from shadow families ───────────────
        var usedParams    = new HashSet<string>();
        var shadowChanges = new List<ParameterChange>();

        foreach (var family in shadowFamilies)
        {
            var change = MapFamilyToChange(family);
            if (change is not null && usedParams.Add(change.Parameter))
                shadowChanges.Add(change);
        }

        // ── Comparison ────────────────────────────────────────────────────────
        var actualSet = new HashSet<string>(actualFamilies);
        var shadowSet = new HashSet<string>(shadowFamilies);
        var diverged  = !actualSet.SetEquals(shadowSet);

        string divergenceReason = "";
        if (diverged)
        {
            var added   = shadowSet.Except(actualSet).ToList();
            var dropped = actualSet.Except(shadowSet).ToList();
            var parts   = new List<string>();
            if (added.Count   > 0) parts.Add($"shadow adds: {string.Join(", ", added)}");
            if (dropped.Count > 0) parts.Add($"shadow drops: {string.Join(", ", dropped)}");
            divergenceReason = string.Join("; ", parts);
        }

        string shadowSummary = shadowReasons.Count > 0
            ? string.Join("; ", shadowReasons)
            : "Shadow recommendation matches live policy.";

        return new PersonalizedPolicyShadowDecision
        {
            UserId       = userId,
            SessionId    = signals.SessionId,
            WindowIndex  = signals.WindowIndex,
            CreatedAtUtc = signals.CreatedAtUtc,

            ActualDecisionSummary      = actual.ReasoningSummary,
            ActualInterventionFamilies = actualFamilies,
            ActualParameterChanges     = actualChanges,
            UserRelativeSummary        = signals.Summary,

            ShadowDecisionSummary      = shadowSummary,
            ShadowInterventionFamilies = shadowFamilies,
            ShadowParameterChanges     = shadowChanges,

            DecisionDiverged = diverged,
            DivergenceReason = divergenceReason,
        };
    }

    // ── Shadow family → parameter mapping ────────────────────────────────────
    // Advisory only — uses fixed nominal deltas, no catalog bounds needed.

    private static ParameterChange? MapFamilyToChange(string family) => family switch
    {
        "difficulty-reduction"  => new ParameterChange { Parameter = "difficulty",   Operation = "decrease", Value = 0.10 },
        "difficulty-increase"   => new ParameterChange { Parameter = "difficulty",   Operation = "increase", Value = 0.05 },
        "pace-support"          => new ParameterChange { Parameter = "timePressure", Operation = "decrease", Value = 0.10 },
        "pace-increase"         => new ParameterChange { Parameter = "timePressure", Operation = "increase", Value = 0.05 },
        "scaffold-hints"        => new ParameterChange { Parameter = "hintMode",     Operation = "set",      Value = "guided" },
        "hint-fade"             => new ParameterChange { Parameter = "hintMode",     Operation = "set",      Value = "minimal" },
        "hint-remove"           => new ParameterChange { Parameter = "hintMode",     Operation = "set",      Value = "off" },
        "hint-restore-minimal"  => new ParameterChange { Parameter = "hintMode",     Operation = "set",      Value = "minimal" },
        "hint-reduction"        => new ParameterChange { Parameter = "hintMode",     Operation = "set",      Value = "minimal" },
        _                       => null
    };

    // ── JsonElement helpers ───────────────────────────────────────────────────

    private static List<string> DeserializeStringList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];
        return element.Deserialize<List<string>>() ?? [];
    }

    private static List<ParameterChange> DeserializeParameterChanges(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];
        return element.Deserialize<List<ParameterChange>>() ?? [];
    }
}
