using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IPolicyReliabilityWeightingService
{
    // guardrailSummary and shadowPrediction are used for Phase 9C/9D conditional tier evaluation.
    // Both may be null when the relevant pipeline steps did not fire or returned no result.
    AdaptationDecisionDocument Apply(
        AdaptationDecisionDocument decision,
        MultiSignalGuardrailSummary? guardrailSummary,
        BehaviorStatePrediction? shadowPrediction);
}

public class PolicyReliabilityWeightingService : IPolicyReliabilityWeightingService
{
    // Hint-reduction families own the hintMode parameter change.
    // The change is only removed if no hint-support family survives after risky families are stripped.
    private static readonly HashSet<string> HintReductionFamilies =
        new(StringComparer.OrdinalIgnoreCase) { "hint-fade", "hint-remove", "hint-reduction" };

    private static readonly HashSet<string> HintSupportFamilies =
        new(StringComparer.OrdinalIgnoreCase) { "scaffold-hints", "hint-restore-minimal" };

    // Direct ownership: family → the one ParameterChange it produces.
    private static readonly Dictionary<string, (string Parameter, string Operation)> FamilyParamOwnership =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["difficulty-increase"]  = ("difficulty",   "increase"),
            ["pace-increase"]        = ("timePressure", "increase"),
            ["difficulty-reduction"] = ("difficulty",   "decrease"),
            ["pace-support"]         = ("timePressure", "decrease"),
        };

    private readonly RecursorPolicyReliabilityOptions _options;
    private readonly ILogger<PolicyReliabilityWeightingService> _logger;

    public PolicyReliabilityWeightingService(
        IOptions<RecursorPolicyReliabilityOptions> options,
        ILogger<PolicyReliabilityWeightingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public AdaptationDecisionDocument Apply(
        AdaptationDecisionDocument decision,
        MultiSignalGuardrailSummary? guardrailSummary,
        BehaviorStatePrediction? shadowPrediction)
    {
        if (_options.Mode == "disabled")
            return decision;

        // Always stamp the mode so the ADX record reflects the Phase 8E configuration.
        decision.Phase8EReliabilityMode = _options.Mode;

        var globalRiskyFamilies = decision.InterventionFamilies
            .Where(f => _options.FamilyReliabilityTiers.TryGetValue(f, out var tier) && tier == "risky")
            .ToList();

        // Phase 9C/9D: evaluate conditional tiers against the original families list.
        var conditionalEval = EvaluateConditionalTiers(decision, guardrailSummary, shadowPrediction);

        // Conditional candidates: conditionally risky families not already globally risky.
        // Filtering here prevents double-removal and duplicate notes.
        var conditionalCandidates = conditionalEval.RiskyFamilies
            .Where(f => !globalRiskyFamilies.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (_options.Mode == "shadow")
        {
            decision.Phase8EReliabilityNotes = BuildShadowNotes(globalRiskyFamilies, conditionalEval, conditionalCandidates);
            if (decision.Phase8EReliabilityNotes is not null)
            {
                _logger.LogInformation(
                    "Phase 8E shadow: session {SessionId}. Notes={Notes}",
                    decision.SessionId, decision.Phase8EReliabilityNotes);
            }
            return decision;
        }

        // active mode
        return ApplyActiveMode(decision, globalRiskyFamilies, conditionalEval, conditionalCandidates);
    }

    // Phase 9D active-mode suppression. Global risky families are always removed.
    // Conditional risky candidates are additionally removed when EnableConditionalSuppression is true.
    private AdaptationDecisionDocument ApplyActiveMode(
        AdaptationDecisionDocument decision,
        List<string> globalRiskyFamilies,
        ConditionalEvalResult conditionalEval,
        List<string> conditionalCandidates)
    {
        var conditionalSuppressedFamilies = _options.EnableConditionalSuppression
            ? conditionalCandidates
            : new List<string>();

        var allFamiliesToRemove = globalRiskyFamilies
            .Concat(conditionalSuppressedFamilies)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allFamiliesToRemove.Count == 0)
        {
            // Nothing to suppress — record observations only (conditional matches, candidates).
            decision.Phase8EReliabilityNotes = BuildActiveModeNotes(
                new(), new(), conditionalEval, conditionalCandidates, new(), new());
            return decision;
        }

        var remainingFamilies = decision.InterventionFamilies
            .Where(f => !allFamiliesToRemove.Contains(f))
            .ToList();

        bool hintSupportSurvives = remainingFamilies.Any(f => HintSupportFamilies.Contains(f));

        // Collect params owned by globally risky families.
        var globalToRemove = new HashSet<ParameterChange>(ReferenceEqualityComparer.Instance);
        foreach (var family in globalRiskyFamilies)
            CollectOwnedParams(family, decision, hintSupportSurvives, globalToRemove, skipSet: null);

        // Collect params owned by conditionally suppressed families (skip already globally claimed).
        var conditionalToRemove = new HashSet<ParameterChange>(ReferenceEqualityComparer.Instance);
        foreach (var family in conditionalSuppressedFamilies)
            CollectOwnedParams(family, decision, hintSupportSurvives, conditionalToRemove, skipSet: globalToRemove);

        var allToRemove = new HashSet<ParameterChange>(ReferenceEqualityComparer.Instance);
        foreach (var p in globalToRemove) allToRemove.Add(p);
        foreach (var p in conditionalToRemove) allToRemove.Add(p);

        var removedGlobalParams = globalToRemove
            .Select(p => $"{p.Parameter}/{p.Operation}").Distinct().OrderBy(s => s).ToList();

        var removedConditionalParams = conditionalToRemove
            .Select(p => $"{p.Parameter}/{p.Operation}").Distinct().OrderBy(s => s).ToList();

        decision.InterventionFamilies = remainingFamilies;
        decision.ParameterChanges = decision.ParameterChanges
            .Where(p => !allToRemove.Contains(p))
            .ToList();

        decision.Phase8EReliabilityNotes = BuildActiveModeNotes(
            globalRiskyFamilies, removedGlobalParams,
            conditionalEval, conditionalCandidates,
            conditionalSuppressedFamilies, removedConditionalParams);

        _logger.LogInformation(
            "Phase 8E/9D active: suppressed {GlobalCount} globally risky, {CondCount} conditionally risky families " +
            "for session {SessionId}. Notes={Notes}",
            globalRiskyFamilies.Count, conditionalSuppressedFamilies.Count,
            decision.SessionId, decision.Phase8EReliabilityNotes);

        return decision;
    }

    // Adds a param change owned by family to target, skipping any already in skipSet.
    // hintSupportSurvives determines whether hint-reduction families lose the hintMode change.
    private static void CollectOwnedParams(
        string family,
        AdaptationDecisionDocument decision,
        bool hintSupportSurvives,
        HashSet<ParameterChange> target,
        HashSet<ParameterChange>? skipSet)
    {
        if (FamilyParamOwnership.TryGetValue(family, out var ownership))
        {
            foreach (var p in decision.ParameterChanges
                .Where(p => p.Parameter == ownership.Parameter && p.Operation == ownership.Operation))
            {
                if (skipSet is null || !skipSet.Contains(p))
                    target.Add(p);
            }
        }
        else if (HintReductionFamilies.Contains(family) && !hintSupportSurvives)
        {
            foreach (var p in decision.ParameterChanges.Where(p => p.Parameter == "hintMode"))
            {
                if (skipSet is null || !skipSet.Contains(p))
                    target.Add(p);
            }
        }
    }

    // Builds the Phase8EReliabilityNotes string for shadow mode.
    // Includes global risky detection, conditional matches, and conditional suppression candidates.
    // Returns null when there is nothing to record.
    private static string? BuildShadowNotes(
        List<string> globalRiskyFamilies,
        ConditionalEvalResult conditionalEval,
        List<string> conditionalCandidates)
    {
        var parts = new List<string>();

        if (globalRiskyFamilies.Count > 0)
            parts.Add($"risky-families-detected: [{string.Join(", ", globalRiskyFamilies)}] | mode=shadow; no changes applied");

        if (conditionalEval.MatchesNote is not null)
            parts.Add(conditionalEval.MatchesNote);

        if (conditionalCandidates.Count > 0)
            parts.Add($"conditional-risky-suppression-candidates: [{string.Join(", ", conditionalCandidates)}]");

        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    // Builds the Phase8EReliabilityNotes string for active mode.
    // Records both global and conditional suppression results.
    // Returns null when nothing was recorded.
    private static string? BuildActiveModeNotes(
        List<string> globalRiskyFamilies,
        List<string> removedGlobalParams,
        ConditionalEvalResult conditionalEval,
        List<string> conditionalCandidates,
        List<string> conditionalSuppressedFamilies,
        List<string> removedConditionalParams)
    {
        var parts = new List<string>();

        if (globalRiskyFamilies.Count > 0)
        {
            parts.Add($"risky-families-detected: [{string.Join(", ", globalRiskyFamilies)}]");
            parts.Add($"suppressed-params: [{string.Join(", ", removedGlobalParams)}]");
        }

        if (conditionalEval.MatchesNote is not null)
            parts.Add(conditionalEval.MatchesNote);

        if (conditionalCandidates.Count > 0)
            parts.Add($"conditional-risky-suppression-candidates: [{string.Join(", ", conditionalCandidates)}]");

        if (conditionalSuppressedFamilies.Count > 0)
        {
            parts.Add($"conditional-suppressed-families: [{string.Join(", ", conditionalSuppressedFamilies)}]");
            parts.Add($"conditional-suppressed-params: [{string.Join(", ", removedConditionalParams)}]");
        }

        // mode=active is added whenever any suppression was applied (global or conditional).
        bool anySuppressionApplied = globalRiskyFamilies.Count > 0 || conditionalSuppressedFamilies.Count > 0;
        if (anySuppressionApplied)
            parts.Add("mode=active");

        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    // Evaluates ConditionalFamilyReliabilityTiers against the current learner state.
    // Returns matches and the subset of families that matched a "risky" conditional tier.
    // This method never modifies the adaptation.
    private ConditionalEvalResult EvaluateConditionalTiers(
        AdaptationDecisionDocument decision,
        MultiSignalGuardrailSummary? guardrailSummary,
        BehaviorStatePrediction? shadowPrediction)
    {
        if (_options.ConditionalFamilyReliabilityTiers.Count == 0)
            return ConditionalEvalResult.Empty;

        var activeKeys = BuildActiveConditionKeys(guardrailSummary, shadowPrediction);

        if (activeKeys.Count == 0)
            return ConditionalEvalResult.Empty;

        var matches = new List<string>();
        var riskyFamilies = new List<string>();

        foreach (var family in decision.InterventionFamilies)
        {
            if (!_options.ConditionalFamilyReliabilityTiers.TryGetValue(family, out var familyConditions))
                continue;

            foreach (var key in activeKeys)
            {
                if (!familyConditions.TryGetValue(key, out var conditionalTier))
                    continue;

                matches.Add($"{family}={conditionalTier}[{key}]");

                if (string.Equals(conditionalTier, "risky", StringComparison.OrdinalIgnoreCase)
                    && !riskyFamilies.Contains(family, StringComparer.OrdinalIgnoreCase))
                {
                    riskyFamilies.Add(family);
                }

                _logger.LogInformation(
                    "Phase 9C conditional match: family '{Family}' matched condition '{Key}' → tier '{Tier}' " +
                    "for session {SessionId}.",
                    family, key, conditionalTier, decision.SessionId);
            }
        }

        if (matches.Count == 0)
            return ConditionalEvalResult.Empty;

        return new ConditionalEvalResult(
            matches,
            riskyFamilies,
            $"conditional-matches: {string.Join(", ", matches)}");
    }

    // Builds active condition key strings from the current learner state snapshot.
    // Keys use "=" as separator (not ":" which is a .NET config section separator).
    private static List<string> BuildActiveConditionKeys(
        MultiSignalGuardrailSummary? guardrailSummary,
        BehaviorStatePrediction? shadowPrediction)
    {
        var activeKeys = new List<string>();

        if (guardrailSummary is not null)
        {
            if (!string.IsNullOrWhiteSpace(guardrailSummary.OverallBehaviorState))
                activeKeys.Add($"GuardrailOverallBehaviorState={guardrailSummary.OverallBehaviorState}");

            if (!string.IsNullOrWhiteSpace(guardrailSummary.HintDependenceRiskLevel))
                activeKeys.Add($"GuardrailHintDependenceRiskLevel={guardrailSummary.HintDependenceRiskLevel}");
        }

        if (shadowPrediction?.PredictedConfusionRisk is { Length: > 0 } confusionRisk)
            activeKeys.Add($"PredictedConfusionRisk={confusionRisk}");

        return activeKeys;
    }

    private record ConditionalEvalResult(
        IReadOnlyList<string> Matches,
        IReadOnlyList<string> RiskyFamilies,
        string? MatchesNote)
    {
        public static readonly ConditionalEvalResult Empty =
            new(Array.Empty<string>(), Array.Empty<string>(), null);
    }
}
