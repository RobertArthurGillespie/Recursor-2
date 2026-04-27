using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IPolicyReliabilityWeightingService
{
    AdaptationDecisionDocument Apply(AdaptationDecisionDocument decision);
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

    public AdaptationDecisionDocument Apply(AdaptationDecisionDocument decision)
    {
        if (_options.Mode == "disabled")
            return decision;

        var riskyFamilies = decision.InterventionFamilies
            .Where(f => _options.FamilyReliabilityTiers.TryGetValue(f, out var tier) && tier == "risky")
            .ToList();

        // Always stamp the mode so the ADX record reflects the Phase 8E configuration even when nothing is risky.
        decision.Phase8EReliabilityMode = _options.Mode;

        if (riskyFamilies.Count == 0)
            return decision;

        var familyList = string.Join(", ", riskyFamilies);
        var baseNote = $"risky-families-detected: [{familyList}]";

        if (_options.Mode == "shadow")
        {
            decision.Phase8EReliabilityNotes = baseNote + " | mode=shadow; no changes applied";
            _logger.LogInformation(
                "Phase 8E shadow: would suppress {Count} risky families for session {SessionId}. Notes={Notes}",
                riskyFamilies.Count, decision.SessionId, decision.Phase8EReliabilityNotes);
            return decision;
        }

        // active — strip risky families and their owned parameter changes.
        var remainingFamilies = decision.InterventionFamilies
            .Where(f => !riskyFamilies.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        bool hintSupportSurvives = remainingFamilies.Any(f => HintSupportFamilies.Contains(f));

        var toRemove = new HashSet<ParameterChange>(ReferenceEqualityComparer.Instance);

        foreach (var family in riskyFamilies)
        {
            if (FamilyParamOwnership.TryGetValue(family, out var ownership))
            {
                foreach (var p in decision.ParameterChanges
                    .Where(p => p.Parameter == ownership.Parameter && p.Operation == ownership.Operation))
                {
                    toRemove.Add(p);
                }
            }
            else if (HintReductionFamilies.Contains(family) && !hintSupportSurvives)
            {
                foreach (var p in decision.ParameterChanges.Where(p => p.Parameter == "hintMode"))
                    toRemove.Add(p);
            }
        }

        var removedParams = toRemove
            .Select(p => $"{p.Parameter}/{p.Operation}")
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        decision.InterventionFamilies = remainingFamilies;
        decision.ParameterChanges = decision.ParameterChanges
            .Where(p => !toRemove.Contains(p))
            .ToList();

        decision.Phase8EReliabilityNotes =
            baseNote + $" | suppressed-params: [{string.Join(", ", removedParams)}] | mode=active";

        _logger.LogInformation(
            "Phase 8E active: suppressed {FamilyCount} risky families, removed {ParamCount} parameter changes " +
            "for session {SessionId}. Notes={Notes}",
            riskyFamilies.Count, toRemove.Count, decision.SessionId, decision.Phase8EReliabilityNotes);

        return decision;
    }
}
