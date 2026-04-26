using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IPhase8AGuardrailModifierService
{
    // Reviews a proposed adaptation and removes any parameter changes that violate
    // Phase 8A safety rules.  Never adds new changes.  Returns the (possibly modified)
    // document; updates Phase8AGuardrailNotes when any change is blocked.
    AdaptationDecisionDocument Apply(
        AdaptationDecisionDocument adaptation,
        MultiSignalGuardrailSummary guardrail,
        BehaviorStatePrediction? shadowPrediction);
}

public class Phase8AGuardrailModifierService : IPhase8AGuardrailModifierService
{
    // Families that reduce hint/support level (state machine output names).
    private static readonly HashSet<string> HintReductionFamilies =
        ["hint-fade", "hint-remove", "hint-reduction"];

    // Families that restore or increase hint/support level — must not be stripped
    // when rule D removes a co-present reduction family.
    private static readonly HashSet<string> HintRestorationFamilies =
        ["scaffold-hints", "hint-restore-minimal"];

    private readonly ILogger<Phase8AGuardrailModifierService> _logger;

    public Phase8AGuardrailModifierService(ILogger<Phase8AGuardrailModifierService> logger)
    {
        _logger = logger;
    }

    public AdaptationDecisionDocument Apply(
        AdaptationDecisionDocument adaptation,
        MultiSignalGuardrailSummary guardrail,
        BehaviorStatePrediction? shadowPrediction)
    {
        var blocked = new List<string>();
        var families = new List<string>(adaptation.InterventionFamilies);
        var changes  = new List<ParameterChange>(adaptation.ParameterChanges);

        // ── Rules A + B: block difficulty / time-pressure increases ──────────────
        // Rule A: OverallBehaviorState == "conflicted"
        // Rule B: OverallBehaviorState == "struggling"  OR  PredictedConfusionRisk == "high"
        bool blockIntensification =
            guardrail.OverallBehaviorState is "conflicted" or "struggling" ||
            shadowPrediction?.PredictedConfusionRisk == "high";

        if (blockIntensification)
        {
            string trigger =
                guardrail.OverallBehaviorState == "conflicted" ? "OverallBehaviorState=conflicted (rule A)" :
                guardrail.OverallBehaviorState == "struggling"  ? "OverallBehaviorState=struggling (rule B)" :
                                                                   "PredictedConfusionRisk=high (rule B)";

            var diffIncrease = changes.FirstOrDefault(
                c => c.Parameter == "difficulty" && c.Operation == "increase");
            if (diffIncrease is not null)
            {
                changes.Remove(diffIncrease);
                families.Remove("difficulty-increase");
                blocked.Add($"difficulty-increase blocked [{trigger}]");
                _logger.LogInformation(
                    "Phase 8A: difficulty-increase blocked. " +
                    "SessionId={SessionId} Trigger={Trigger} " +
                    "OverallState={State} ConfusionRisk={Risk} ConflictCount={Conflicts}",
                    adaptation.SessionId, trigger,
                    guardrail.OverallBehaviorState,
                    shadowPrediction?.PredictedConfusionRisk ?? "n/a",
                    guardrail.GuardrailConflictCount);
            }

            var paceIncrease = changes.FirstOrDefault(
                c => c.Parameter == "timePressure" && c.Operation == "increase");
            if (paceIncrease is not null)
            {
                changes.Remove(paceIncrease);
                families.Remove("pace-increase");
                blocked.Add($"timePressure-increase blocked [{trigger}]");
                _logger.LogInformation(
                    "Phase 8A: timePressure-increase blocked. " +
                    "SessionId={SessionId} Trigger={Trigger} " +
                    "OverallState={State} ConfusionRisk={Risk} ConflictCount={Conflicts}",
                    adaptation.SessionId, trigger,
                    guardrail.OverallBehaviorState,
                    shadowPrediction?.PredictedConfusionRisk ?? "n/a",
                    guardrail.GuardrailConflictCount);
            }
        }

        // ── Rule C: strong mastery permission (overrides rule D for Phase 8A only) ──
        // When all three conditions hold, Phase 8A will not apply its own hint-reduction
        // block below.  This does NOT override the existing HintDependenceProbability>=0.85
        // guardrail inside AdaptationPolicyService — that is a separate mechanism.
        bool strongMasteryPermission =
            shadowPrediction?.PredictedStableMasteryState == "strong" &&
            guardrail.GuardrailConflictCount == 0 &&
            guardrail.OverallBehaviorState is "mastering" or "stable";

        // ── Rule D: block hint reduction / support fading when risk is high ───────
        if (guardrail.HintDependenceRiskLevel == "high" && !strongMasteryPermission)
        {
            bool removedReductionFamilies =
                families.RemoveAll(f => HintReductionFamilies.Contains(f)) > 0;

            if (removedReductionFamilies)
            {
                // Only strip the hintMode ParameterChange when no hint-restoration family
                // remains — a restoration family also produces a hintMode change and must
                // not be removed (it adds support, not removes it).
                bool hasRestorationFamily = families.Any(f => HintRestorationFamilies.Contains(f));
                if (!hasRestorationFamily)
                {
                    var hintChange = changes.FirstOrDefault(c => c.Parameter == "hintMode");
                    if (hintChange is not null)
                        changes.Remove(hintChange);
                }

                blocked.Add("hint-reduction blocked [HintDependenceRiskLevel=high (rule D)]");
                _logger.LogInformation(
                    "Phase 8A: hint-reduction blocked. " +
                    "SessionId={SessionId} HintDependenceRiskLevel={RiskLevel} " +
                    "RestorationFamilyPresent={HasRestoration} ConflictCount={Conflicts}",
                    adaptation.SessionId,
                    guardrail.HintDependenceRiskLevel,
                    families.Any(f => HintRestorationFamilies.Contains(f)),
                    guardrail.GuardrailConflictCount);
            }
        }
        else if (strongMasteryPermission &&
                 guardrail.HintDependenceRiskLevel == "high" &&
                 families.Any(f => HintReductionFamilies.Contains(f)))
        {
            // Rule C is active and a reduction family is present — log the permission grant.
            _logger.LogInformation(
                "Phase 8A: rule C permits hint reduction despite HintDependenceRiskLevel=high. " +
                "SessionId={SessionId} StableMasteryState=strong ConflictCount=0 OverallState={State}",
                adaptation.SessionId, guardrail.OverallBehaviorState);
        }

        if (blocked.Count == 0)
        {
            _logger.LogDebug(
                "Phase 8A: no interventions for session {SessionId}. " +
                "OverallState={State} HintRisk={HintRisk} ConfusionRisk={ConfusionRisk}",
                adaptation.SessionId,
                guardrail.OverallBehaviorState,
                guardrail.HintDependenceRiskLevel,
                shadowPrediction?.PredictedConfusionRisk ?? "n/a");
            return adaptation;
        }

        string notes = string.Join("; ", blocked);

        adaptation.ParameterChanges    = changes;
        adaptation.InterventionFamilies = families;
        adaptation.ReasoningSummary     = string.IsNullOrEmpty(adaptation.ReasoningSummary)
            ? $"Phase8A: {notes}"
            : $"{adaptation.ReasoningSummary}; Phase8A: {notes}";
        adaptation.Phase8AGuardrailNotes = notes;

        _logger.LogInformation(
            "Phase 8A guardrail applied. SessionId={SessionId} " +
            "BlockedCount={BlockedCount} RemainingChanges={RemainingChanges} Notes={Notes}",
            adaptation.SessionId, blocked.Count, changes.Count, notes);

        return adaptation;
    }
}
