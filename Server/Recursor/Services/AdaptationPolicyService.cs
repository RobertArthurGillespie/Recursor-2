using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IAdaptationPolicyService
{
    AdaptationDecisionDocument? ApplyPolicy(
        SessionDocument session,
        SimCatalogDocument catalog,
        HypothesisSetDocument hypothesisSet,
        BehaviorStatePrediction? shadowPrediction = null,
        PersonalizedPolicySignals? personalizedSignals = null);
}

public class AdaptationPolicyService : IAdaptationPolicyService
{
    private readonly RecursorPoliciesOptions _policies;

    public AdaptationPolicyService(IOptions<RecursorPoliciesOptions> policiesOptions)
    {
        _policies = policiesOptions.Value;
    }

    public AdaptationDecisionDocument? ApplyPolicy(
    SessionDocument session,
    SimCatalogDocument catalog,
    HypothesisSetDocument hypothesisSet,
    BehaviorStatePrediction? shadowPrediction = null,
    PersonalizedPolicySignals? personalizedSignals = null)
    {
        if (hypothesisSet.Hypotheses.Count == 0)
            return null;

        bool hasStableMastery = hypothesisSet.Hypotheses.Any(h => h.Label == "stable_mastery_pattern");
        bool hasRelapse = hypothesisSet.Hypotheses.Any(h => h.Label == "relapse_pattern");
        bool hasRecovery = hypothesisSet.Hypotheses.Any(h => h.Label == "recovery_pattern");
        bool hasImproving = hypothesisSet.Hypotheses.Any(h => h.Label == "improving_pattern");

        IEnumerable<BehavioralHypothesis> sourceHypotheses = hypothesisSet.Hypotheses;

        if (hasStableMastery || hasImproving)
        {
            sourceHypotheses = hypothesisSet.Hypotheses
                .Where(h =>
                    h.Label == "stable_mastery_pattern" ||
                    h.Label == "recovery_pattern" ||
                    h.Label == "improving_pattern")
                .ToList();
        }
        else if (hasRelapse)
        {
            sourceHypotheses = hypothesisSet.Hypotheses
                .Where(h =>
                    h.Label == "relapse_pattern" ||
                    h.Label == "worsening_pattern" ||
                    h.Label == "learner-overload" ||
                    h.Label == "confusion_pattern" ||
                    h.Label == "attention-deficit" ||
                    h.Label == "goal-confusion" ||
                    h.Label == "hint_dependence_pattern" ||
                    h.Label == "hint-dependency")
                .ToList();
        }
        else if (hasRecovery)
        {
            sourceHypotheses = hypothesisSet.Hypotheses
                .Where(h =>
                    h.Label == "recovery_pattern" ||
                    h.Label == "improving_pattern")
                .ToList();
        }

        string? currentHintMode = GetCurrentHintMode(session);

        var interventionFamilies = sourceHypotheses
            .OrderByDescending(h => h.Confidence)
            .SelectMany(h => GetInterventionFamilies(h.Label))
            .Distinct()
            .ToList();

        // Staged hint-mode state machine — replaces all hint-related families with
        // exactly the one transition appropriate for the current hint mode.
        var hintFamilies = new HashSet<string>
        {
            "scaffold-hints", "hint-fade", "hint-remove", "hint-reduction", "hint-restore-minimal"
        };

        if (hasStableMastery || hasImproving)
        {
            interventionFamilies = interventionFamilies.Where(f => !hintFamilies.Contains(f)).ToList();

            // guided → minimal: any streak of mastery/improving windows is sufficient.
            // minimal → off: stricter — requires repeated true stable mastery, not just improving.
            if (string.Equals(currentHintMode, "guided", StringComparison.OrdinalIgnoreCase)
                && session.ConsecutiveSupportFadeEligibleWindows >= _policies.HintFadeRequiresSupportFadeEligibleWindows)
            {
                interventionFamilies.Add("hint-fade");   // guided → minimal
            }
            else if (string.Equals(currentHintMode, "minimal", StringComparison.OrdinalIgnoreCase)
                && session.ConsecutiveStableMasteryWindows >= _policies.HintRemoveRequiresStableMasteryWindows)
            {
                interventionFamilies.Add("hint-remove"); // minimal → off (requires stable mastery streak)
            }
        }
        else if (hasRelapse)
        {
            interventionFamilies = interventionFamilies.Where(f => !hintFamilies.Contains(f)).ToList();

            if (string.Equals(currentHintMode, "off", StringComparison.OrdinalIgnoreCase))
            {
                interventionFamilies.Add("hint-restore-minimal");   // off → minimal
            }
            else if (string.Equals(currentHintMode, "minimal", StringComparison.OrdinalIgnoreCase))
            {
                // Require 2 consecutive relapse windows before escalating minimal → guided.
                // This prevents a single bad batch from immediately re-introducing full guidance.
                if (session.ConsecutiveRelapseWindows >= 2)
                    interventionFamilies.Add("scaffold-hints");      // minimal → guided (confirmed relapse)
                else
                    interventionFamilies.Add("hint-restore-minimal"); // stay in minimal this window
            }
            else
            {
                interventionFamilies.Add("scaffold-hints");          // guided → guided
            }
        }
        else if (hasRecovery)
        {
            interventionFamilies = interventionFamilies.Where(f => !hintFamilies.Contains(f)).ToList();

            // Recovery alone does not build the support-fade-eligible streak, so hint reduction
            // here only fires if the session had a prior run of mastery/improving windows.
            // guided → minimal: fade-eligible streak (mastery or improving).
            // minimal → off: stricter — requires stable mastery streak only.
            if (string.Equals(currentHintMode, "guided", StringComparison.OrdinalIgnoreCase)
                && session.ConsecutiveSupportFadeEligibleWindows >= _policies.HintFadeRequiresSupportFadeEligibleWindows)
            {
                interventionFamilies.Add("hint-fade");   // guided -> minimal
            }
            else if (string.Equals(currentHintMode, "minimal", StringComparison.OrdinalIgnoreCase)
                && session.ConsecutiveStableMasteryWindows >= _policies.HintRemoveRequiresStableMasteryWindows)
            {
                interventionFamilies.Add("hint-remove"); // minimal -> off (requires stable mastery streak)
            }
            // off -> no hint family added; streak not met -> no hint family added this window
        }

        // ML guardrail: if shadow prediction indicates strong hint dependence,
        // veto any hint-reduction family that the rule engine just selected.
        // ML may only block support removal — it cannot create or increase support.
        string? mlVetoNote = null;
        bool mlGuardrailActive = shadowPrediction is not null && shadowPrediction.HintDependenceProbability >= 0.85;
        if (mlGuardrailActive)
        {
            bool mlGuardrailRemovedHintReduction = false;
            mlGuardrailRemovedHintReduction |= interventionFamilies.Remove("hint-fade");
            mlGuardrailRemovedHintReduction |= interventionFamilies.Remove("hint-remove");
            mlGuardrailRemovedHintReduction |= interventionFamilies.Remove("hint-reduction");

            if (mlGuardrailRemovedHintReduction)
                mlVetoNote = $"hint reduction blocked by ML assist (HintDependenceProbability={shadowPrediction!.HintDependenceProbability:0.00})";
        }

        // Next-window guardrail: if the baseline next-window model predicts elevated hint
        // dependence in the upcoming window, prevent or reverse hint-reduction steps now.
        // Operates only when EnableNextWindowHintDependenceGuardrail is true and the model
        // produced a non-null probability (i.e., the model file was loaded successfully).
        string? nextWindowGuardrailNote = null;
        bool nextWindowGuardrailTriggered = false;
        string? nextWindowGuardrailLevel = null;
        if (_policies.EnableNextWindowHintDependenceGuardrail
            && shadowPrediction?.HintDependenceNextProbability is double nextProb)
        {
            bool reductionPresent =
                interventionFamilies.Contains("hint-fade") ||
                interventionFamilies.Contains("hint-remove") ||
                interventionFamilies.Contains("hint-reduction");

            if (nextProb >= _policies.HintDependenceNextHighThreshold)
            {
                // High: veto all hint-reduction families; ensure some hint support is present;
                // softly reduce time pressure to ease cognitive load.
                interventionFamilies.Remove("hint-fade");
                interventionFamilies.Remove("hint-remove");
                interventionFamilies.Remove("hint-reduction");

                bool hintSupportPresent =
                    interventionFamilies.Contains("scaffold-hints") ||
                    interventionFamilies.Contains("hint-restore-minimal");

                string hintGuardrailAction = "hint reduction blocked";
                if (!hintSupportPresent)
                {
                    if (string.Equals(currentHintMode, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        interventionFamilies.Add("hint-restore-minimal"); // off → minimal
                        hintGuardrailAction = "restored minimal hint support";
                    }
                    else if (string.Equals(currentHintMode, "minimal", StringComparison.OrdinalIgnoreCase))
                    {
                        // Stay at minimal — do not escalate to guided during recovery/mastery windows.
                        interventionFamilies.Add("hint-restore-minimal");
                        hintGuardrailAction = "preserved minimal hint support";
                    }
                    // guided: implicit support already present; no escalation needed
                }

                if (!interventionFamilies.Contains("pace-support"))
                    interventionFamilies.Add("pace-support");

                nextWindowGuardrailTriggered = true;
                nextWindowGuardrailLevel = "high";
                nextWindowGuardrailNote =
                    $"next-window guardrail HIGH; currentHintMode={currentHintMode ?? "null"}; " +
                    $"nextProb={nextProb:0.00}; reductionPresent={reductionPresent}; {hintGuardrailAction}";
            }
            else if (nextProb >= _policies.HintDependenceNextModerateThreshold)
            {
                // Moderate: before vetoing, check whether sustained recovery evidence
                // outweighs the model's caution.  If the state machine already computed
                // a reduction AND the learner has built a long enough eligible streak with
                // an active mastery/improving/recovery hypothesis, downgrade the guardrail
                // so the reduction can proceed.  HIGH risk is never overridden this way.
                bool moderateRecoveryOverride =
                    _policies.EnableModerateGuardrailRecoveryOverride
                    && reductionPresent
                    && (hasStableMastery || hasImproving || hasRecovery)
                    && session.ConsecutiveSupportFadeEligibleWindows
                        >= _policies.ModerateGuardrailRecoveryOverrideRequiresEligibleWindows;

                nextWindowGuardrailTriggered = true;
                if (moderateRecoveryOverride)
                {
                    nextWindowGuardrailLevel = "moderate-overridden";
                    nextWindowGuardrailNote =
                        $"next-window guardrail MODERATE overridden by recovery evidence; " +
                        $"currentHintMode={currentHintMode ?? "null"}; nextProb={nextProb:0.00}; " +
                        $"reductionPresent={reductionPresent}; " +
                        $"SupportFadeEligibleWindows={session.ConsecutiveSupportFadeEligibleWindows}; " +
                        $"reduction allowed";
                }
                else
                {
                    interventionFamilies.Remove("hint-fade");
                    interventionFamilies.Remove("hint-remove");
                    interventionFamilies.Remove("hint-reduction");

                    nextWindowGuardrailLevel = "moderate";
                    nextWindowGuardrailNote =
                        $"next-window guardrail MODERATE; currentHintMode={currentHintMode ?? "null"}; " +
                        $"nextProb={nextProb:0.00}; reductionPresent={reductionPresent}; support held steady";
                }
            }
        }

        // Phase 8 — personalized hint-fade rule.
        // Fires only when the flag is on, no relapse is active, no next-window guardrail
        // triggered, the session is already in a success/challenge posture
        // (difficulty-increase and pace-increase both present), and the user's own
        // relative signals confirm they are below their personal baseline for confusion
        // and hint dependence, and not below baseline on goal understanding or attention.
        // This produces a gentle guided → minimal hint reduction supported by individual evidence.
        string? personalizedHintFadeNote = null;
        
        if (_policies.EnablePersonalizedHintFadeRule
            && personalizedSignals is not null
            && !hasRelapse
            && !mlGuardrailActive
            && !nextWindowGuardrailTriggered
            && string.Equals(currentHintMode, "guided", StringComparison.OrdinalIgnoreCase)
            && interventionFamilies.Contains("difficulty-increase")
            && interventionFamilies.Contains("pace-increase")
            && personalizedSignals.ConfusionDelta <= _policies.PersonalizedHintFadeConfusionDeltaThreshold
            && personalizedSignals.HintDependenceDelta <= _policies.PersonalizedHintFadeHintDependenceDeltaThreshold
            && !personalizedSignals.IsBelowBaselineGoalUnderstanding
            && !personalizedSignals.IsBelowBaselineAttention
            && personalizedSignals.CurrentConfusionScore <= _policies.PersonalizedHintFadeMaxCurrentConfusionScore
            && personalizedSignals.CurrentGoalUnderstanding >= _policies.PersonalizedHintFadeMinCurrentGoalUnderstanding)
        {
            interventionFamilies.Remove("scaffold-hints");
            interventionFamilies.Remove("hint-remove");
            interventionFamilies.Remove("hint-reduction");
            interventionFamilies.Remove("hint-restore-minimal");

            if (!interventionFamilies.Contains("hint-fade"))
                interventionFamilies.Add("hint-fade");

            personalizedHintFadeNote =
                $"personalized hint fade applied (ConfusionDelta={personalizedSignals.ConfusionDelta:0.00}, " +
                $"HintDependenceDelta={personalizedSignals.HintDependenceDelta:0.00}, " +
                $"CurrentConfusionScore={personalizedSignals.CurrentConfusionScore:0.00})";
        }

        var changes = new List<ParameterChange>();
        var usedParameters = new HashSet<string>();

        foreach (var family in interventionFamilies)
        {
            var change = BuildFamilyChange(family, catalog);
            if (change is not null && usedParameters.Add(change.Parameter))
                changes.Add(change);
        }

        if (changes.Count == 0 && !nextWindowGuardrailTriggered)
            return null;

        var reasoning = changes
            .Select(c => $"{c.Parameter} {c.Operation} {c.Value}")
            .ToList();

        if (mlVetoNote is not null)
            reasoning.Add(mlVetoNote);

        if (nextWindowGuardrailNote is not null)
            reasoning.Add(nextWindowGuardrailNote);

        if (personalizedHintFadeNote is not null)
            reasoning.Add(personalizedHintFadeNote);

        var decisionIndex = session.LatestAdaptationId is null ? 0
            : (int.TryParse(session.LatestAdaptationId.Split('-').Last(), out var idx) ? idx + 1 : 0);

        return new AdaptationDecisionDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentType = "AdaptationDecision",
            SessionId = session.SessionId,
            UserId = session.UserId,
            DecisionIndex = decisionIndex,
            SourceHypothesisSetId = hypothesisSet.Id,
            InterventionFamilies = interventionFamilies,
            ParameterChanges = changes,
            ReasoningSummary = string.Join("; ", reasoning),
            ExpiresAfterWindow = 2,
            HintDependenceNextProbability = shadowPrediction?.HintDependenceNextProbability,
            NextHintDependenceGuardrailTriggered = nextWindowGuardrailTriggered,
            NextHintDependenceGuardrailLevel = nextWindowGuardrailLevel,
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
            "hint_dependence_pattern" => ["hint-reduction"],
            "compound_confusion_hint_dependence" => ["difficulty-reduction", "scaffold-hints", "pace-support"],

            "recovery_pattern" => ["difficulty-increase", "pace-increase", "hint-fade"],

            "stable_mastery_pattern" => ["difficulty-increase", "pace-increase", "hint-remove"],
            "relapse_pattern" => ["difficulty-reduction", "pace-support", "scaffold-hints"],
            "improving_pattern" => ["difficulty-increase", "pace-increase", "hint-fade"],
            "worsening_pattern" => ["difficulty-reduction", "pace-support", "scaffold-hints"],

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
            "hint-restore-minimal" => BuildEnumChange("hintMode", "set", "minimal", catalog),

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