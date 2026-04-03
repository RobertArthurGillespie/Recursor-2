using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IAdaptationPolicyService
{
    AdaptationDecisionDocument? ApplyPolicy(
        SessionDocument session,
        SimCatalogDocument catalog,
        HypothesisSetDocument hypothesisSet);
}

public class AdaptationPolicyService : IAdaptationPolicyService
{
    public AdaptationDecisionDocument? ApplyPolicy(
    SessionDocument session,
    SimCatalogDocument catalog,
    HypothesisSetDocument hypothesisSet)
    {
        if (hypothesisSet.Hypotheses.Count == 0)
            return null;

        bool hasRecovery = hypothesisSet.Hypotheses.Any(h => h.Label == "recovery_pattern");

        IEnumerable<BehavioralHypothesis> sourceHypotheses = hypothesisSet.Hypotheses;

        if (hasRecovery)
        {
            sourceHypotheses = hypothesisSet.Hypotheses
                .Where(h =>
                    h.Label == "recovery_pattern" ||
                    h.Label == "hint_dependence_pattern" ||
                    h.Label == "hint-dependency")
                .ToList();
        }

        string? currentHintMode = GetCurrentHintMode(session);

        var interventionFamilies = sourceHypotheses
            .OrderByDescending(h => h.Confidence)
            .SelectMany(h => GetInterventionFamilies(h.Label))
            .Distinct()
            .ToList();

        if (hasRecovery && string.Equals(currentHintMode, "minimal", StringComparison.OrdinalIgnoreCase))
        {
            interventionFamilies = interventionFamilies
                .Select(f => f == "hint-fade" ? "hint-remove" : f)
                .Distinct()
                .ToList();
        }

        var changes = new List<ParameterChange>();
        var usedParameters = new HashSet<string>();

        foreach (var family in interventionFamilies)
        {
            var change = BuildFamilyChange(family, catalog);
            if (change is not null && usedParameters.Add(change.Parameter))
                changes.Add(change);
        }

        if (changes.Count == 0)
            return null;

        var reasoning = changes
            .Select(c => $"{c.Parameter} {c.Operation} {c.Value}")
            .ToList();

        var decisionIndex = session.LatestAdaptationId is null ? 0
            : (int.TryParse(session.LatestAdaptationId.Split('-').Last(), out var idx) ? idx + 1 : 0);

        return new AdaptationDecisionDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "AdaptationDecision",
            SessionId = session.SessionId,
            DecisionIndex = decisionIndex,
            SourceHypothesisSetId = hypothesisSet.Id,
            InterventionFamilies = interventionFamilies,
            ParameterChanges = changes,
            ReasoningSummary = string.Join("; ", reasoning),
            ExpiresAfterWindow = 2
        };
    }

    private static IEnumerable<string> GetInterventionFamilies(string hypothesisLabel)
    {
        return hypothesisLabel switch
        {
            "attention-deficit" => ["difficulty-reduction"],
            "goal-confusion" => ["difficulty-reduction", "scaffold-hints"],
            "sequencing-difficulty" => ["difficulty-reduction", "scaffold-hints"],
            "excessive-slowness" => ["pace-support"],
            "pace-irregularity" => ["pace-support"],
            "low-self-correction" => ["scaffold-error-tolerance"],
            "hint-dependency" => ["hint-reduction"],
            "safety-risk" => ["difficulty-reduction", "pace-support"],
            "task-discontinuity" => ["distractor-reduction"],
            "learner-overload" => ["difficulty-reduction", "pace-support", "scaffold-hints"],

            "confusion_pattern" => ["difficulty-reduction", "scaffold-hints", "distractor-reduction"],
            "hesitation_pattern" => ["pace-support", "scaffold-hints"],
            "impulsivity_pattern" => ["pace-support", "scaffold-error-tolerance"],
            "hint_dependence_pattern" => ["scaffold-hints"],
            "compound_confusion_hint_dependence" => ["difficulty-reduction", "scaffold-hints", "pace-support"],

            "recovery_pattern" => ["difficulty-increase", "pace-increase", "hint-fade"],

            _ => ["difficulty-reduction"]
        };
    }

    private static ParameterChange? BuildFamilyChange(string family, SimCatalogDocument catalog)
    {
        return family switch
        {
            "difficulty-reduction" => BuildBoundedFloatChange("difficulty", "decrease", 0.10, catalog),
            "difficulty-increase" => BuildBoundedFloatChange("difficulty", "increase", 0.05, catalog),

            "pace-support" => BuildBoundedFloatChange("timePressure", "decrease", 0.10, catalog),
            "pace-increase" => BuildBoundedFloatChange("timePressure", "increase", 0.05, catalog),

            "scaffold-hints" => BuildEnumChange("hintMode", "set", "guided", catalog),
            "hint-fade" => BuildEnumChange("hintMode", "set", "minimal", catalog),
            "hint-remove" => BuildEnumChange("hintMode", "set", "off", catalog),

            "scaffold-error-tolerance" => BuildBoundedIntChange("errorTolerance", "increase", 1, catalog),
            "distractor-reduction" => BuildBoundedFloatChange("distractorDensity", "decrease", 0.15, catalog),

            "hint-reduction" => BuildEnumChange("hintMode", "set", "minimal", catalog),

            _ => null
        };
    }

    private static ParameterChange? BuildBoundedFloatChange(
        string paramName, string operation, double delta, SimCatalogDocument catalog)
    {
        var def = catalog.AdaptiveParameters.FirstOrDefault(p => p.Name == paramName);
        if (def is null) return null;

        double bounded = def.Min.HasValue && def.Max.HasValue
            ? Math.Min(delta, def.Max.Value - def.Min.Value)
            : delta;

        return new ParameterChange { Parameter = paramName, Operation = operation, Value = bounded };
    }

    private static ParameterChange? BuildBoundedIntChange(
        string paramName, string operation, int delta, SimCatalogDocument catalog)
    {
        var def = catalog.AdaptiveParameters.FirstOrDefault(p => p.Name == paramName);
        if (def is null) return null;

        int bounded = def.Min.HasValue && def.Max.HasValue
            ? (int)Math.Min(delta, def.Max.Value - def.Min.Value)
            : delta;

        return new ParameterChange { Parameter = paramName, Operation = operation, Value = bounded };
    }

    private static ParameterChange? BuildEnumChange(
        string paramName, string operation, string value, SimCatalogDocument catalog)
    {
        var def = catalog.AdaptiveParameters.FirstOrDefault(p => p.Name == paramName);
        if (def?.AllowedValues is null) return null;
        if (!def.AllowedValues.Contains(value)) return null;

        return new ParameterChange { Parameter = paramName, Operation = operation, Value = value };
    }

    private static string? GetCurrentHintMode(SessionDocument session)
    {
        if (session.CurrentDifficultyProfile is null)
            return null;

        if (session.CurrentDifficultyProfile.TryGetValue("hintMode", out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }
}