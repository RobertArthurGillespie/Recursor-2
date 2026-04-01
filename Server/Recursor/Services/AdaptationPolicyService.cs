using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IAdaptationPolicyService
{
    // Returns an AdaptationDecisionDocument if any policy fires, or null if nothing applies.
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

        // Collect intervention families from ALL hypotheses, ordered by confidence.
        // Distinct preserves first-occurrence ordering, so the highest-confidence
        // hypothesis wins when two hypotheses map to the same family.
        var interventionFamilies = hypothesisSet.Hypotheses
            .OrderByDescending(h => h.Confidence)
            .SelectMany(h => GetInterventionFamilies(h.Label))
            .Distinct()
            .ToList();

        // Build one bounded parameter change per family.
        // If two families target the same parameter, the first (highest-confidence) wins.
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

    // ── Hypothesis → intervention family mapping ──────────────────────────────
    // Each hypothesis label maps to one or more intervention families.
    // Multiple families per hypothesis allow compound responses.

    private static IEnumerable<string> GetInterventionFamilies(string hypothesisLabel)
    {
        return hypothesisLabel switch
        {
            "attention-deficit"     => ["difficulty-reduction"],
            "goal-confusion"        => ["difficulty-reduction", "scaffold-hints"],
            "sequencing-difficulty" => ["difficulty-reduction", "scaffold-hints"],
            "excessive-slowness"    => ["pace-support"],
            "pace-irregularity"     => ["pace-support"],
            "low-self-correction"   => ["scaffold-error-tolerance"],
            "hint-dependency"       => ["hint-reduction"],
            "safety-risk"           => ["difficulty-reduction", "pace-support"],
            "task-discontinuity"    => ["distractor-reduction"],
            "learner-overload"      => ["difficulty-reduction", "pace-support", "scaffold-hints"],
            _                       => ["difficulty-reduction"]
        };
    }

    // ── Intervention family → parameter change ────────────────────────────────
    // Each family targets one named parameter in the sim catalog.
    // Returns null if the parameter is not present in this sim's catalog.

    private static ParameterChange? BuildFamilyChange(string family, SimCatalogDocument catalog)
    {
        return family switch
        {
            "difficulty-reduction"     => BuildBoundedFloatChange("difficulty",       "decrease", 0.10,  catalog),
            "pace-support"             => BuildBoundedFloatChange("timePressure",     "decrease", 0.10,  catalog),
            "scaffold-hints"           => BuildEnumChange(        "hintMode",         "set",      "guided",  catalog),
            "scaffold-error-tolerance" => BuildBoundedIntChange(  "errorTolerance",   "increase", 1,     catalog),
            "distractor-reduction"     => BuildBoundedFloatChange("distractorDensity","decrease", 0.15,  catalog),
            "hint-reduction"           => BuildEnumChange(        "hintMode",         "set",      "minimal", catalog),
            _                          => null
        };
    }

    // ── Bounded change builders ───────────────────────────────────────────────

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
}
