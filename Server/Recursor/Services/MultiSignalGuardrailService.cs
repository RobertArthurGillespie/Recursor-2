using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IMultiSignalGuardrailService
{
    MultiSignalGuardrailSummary Evaluate(
        HypothesisSetDocument hypothesisSet,
        BehaviorStatePrediction? shadowPrediction,
        PersonalizedPolicySignals? personalizedSignals);
}

public class MultiSignalGuardrailService : IMultiSignalGuardrailService
{
    // User-relative delta thresholds for anomaly warnings (observability only, not policy).
    private const double UserConfusionDeltaWarningThreshold = 0.20;
    private const double UserHintDependenceDeltaWarningThreshold = 0.20;

    private readonly ILogger<MultiSignalGuardrailService> _logger;

    public MultiSignalGuardrailService(ILogger<MultiSignalGuardrailService> logger)
    {
        _logger = logger;
    }

    public MultiSignalGuardrailSummary Evaluate(
        HypothesisSetDocument hypothesisSet,
        BehaviorStatePrediction? shadowPrediction,
        PersonalizedPolicySignals? personalizedSignals)
    {
        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToHashSet();

        if (labels.Count == 0)
            return new MultiSignalGuardrailSummary { OverallBehaviorState = "unknown" };

        // Rule-engine label flags derived from hypothesis set.
        bool labelConfusion = labels.Overlaps(["confusion_pattern", "goal-confusion", "learner-overload"]);
        bool labelRelapse = labels.Contains("relapse_pattern") || labels.Contains("worsening_pattern");
        bool labelHintDependence = labels.Contains("hint_dependence_pattern") || labels.Contains("hint-dependency");
        bool labelStableMastery = labels.Contains("stable_mastery_pattern");
        bool labelImproving = labels.Contains("improving_pattern") || labels.Contains("recovery_pattern");

        var warnings = new List<string>();

        // ── Confusion signal agreement ────────────────────────────────────────────
        bool confusionSignalAgreement = false;
        bool rulesSayConfused = labelConfusion || labelRelapse;

        if (shadowPrediction?.PredictedConfusionRisk is string confusionRisk)
        {
            bool mlSaysConfused = confusionRisk is "moderate" or "high";
            confusionSignalAgreement = mlSaysConfused == rulesSayConfused;

            if (!confusionSignalAgreement)
            {
                warnings.Add(
                    $"confusion disagreement: rules={rulesSayConfused} " +
                    $"ml={confusionRisk} (prob={shadowPrediction.ConfusionProbability:0.000})");
            }
        }
        else if (shadowPrediction is not null)
        {
            // Confusion model not loaded; fall back to raw probability.
            bool mlSaysConfused = shadowPrediction.ConfusionProbability >= 0.60;
            confusionSignalAgreement = mlSaysConfused == rulesSayConfused;

            if (!confusionSignalAgreement)
            {
                warnings.Add(
                    $"confusion disagreement (no confusion model): rules={rulesSayConfused} " +
                    $"mlProb={shadowPrediction.ConfusionProbability:0.000}");
            }
        }

        // ── Stable mastery signal agreement ──────────────────────────────────────
        bool stableMasterySignalAgreement = false;

        if (shadowPrediction?.PredictedStableMasteryState is string masteryState)
        {
            bool mlSaysMastery = masteryState is "stable" or "strong";
            stableMasterySignalAgreement = mlSaysMastery == labelStableMastery;

            if (!stableMasterySignalAgreement)
            {
                warnings.Add(
                    $"stable mastery disagreement: rules={labelStableMastery} " +
                    $"ml={masteryState} (prob={shadowPrediction.StableMasteryProbability:0.000})");
            }
        }
        else if (shadowPrediction is not null)
        {
            bool mlSaysMastery = shadowPrediction.StableMasteryProbability >= 0.60;
            stableMasterySignalAgreement = mlSaysMastery == labelStableMastery;

            if (!stableMasterySignalAgreement)
            {
                warnings.Add(
                    $"stable mastery disagreement (no mastery model): rules={labelStableMastery} " +
                    $"mlProb={shadowPrediction.StableMasteryProbability:0.000}");
            }
        }

        // Explicit contradiction: ML predicts strong mastery but a relapse hypothesis is also present.
        if (shadowPrediction?.PredictedStableMasteryState is "strong" && labelRelapse)
        {
            warnings.Add("contradiction: ML predicts strong stable mastery but relapse hypothesis present");
        }

        // ── Hint dependence risk level ────────────────────────────────────────────
        double currentHintProb = shadowPrediction?.HintDependenceProbability ?? 0.0;
        double? nextHintProb = shadowPrediction?.HintDependenceNextProbability;

        string hintDependenceRiskLevel;
        if (currentHintProb >= 0.85 || (nextHintProb.HasValue && nextHintProb.Value >= 0.80))
            hintDependenceRiskLevel = "high";
        else if (currentHintProb >= 0.60 || (nextHintProb.HasValue && nextHintProb.Value >= 0.60))
            hintDependenceRiskLevel = "moderate";
        else if (currentHintProb >= 0.40 || labelHintDependence)
            hintDependenceRiskLevel = "low";
        else
            hintDependenceRiskLevel = "none";

        // Warn when ML elevates hint-dependence risk but rule engine did not flag it.
        if (hintDependenceRiskLevel is "moderate" or "high" && !labelHintDependence)
        {
            warnings.Add(
                $"hint dependence risk elevated by ML ({hintDependenceRiskLevel}) " +
                $"but no rule label; currentProb={currentHintProb:0.000}" +
                (nextHintProb.HasValue ? $" nextProb={nextHintProb.Value:0.000}" : ""));
        }

        // ── User-relative signal anomalies ────────────────────────────────────────
        if (personalizedSignals is not null)
        {
            if (personalizedSignals.ConfusionDelta > UserConfusionDeltaWarningThreshold
                && !labelConfusion && !labelRelapse)
            {
                warnings.Add(
                    $"user-relative confusion elevated (delta={personalizedSignals.ConfusionDelta:0.00}) " +
                    $"but no confusion/relapse hypothesis");
            }

            if (personalizedSignals.HintDependenceDelta > UserHintDependenceDeltaWarningThreshold
                && !labelHintDependence)
            {
                warnings.Add(
                    $"user-relative hint dependence elevated " +
                    $"(delta={personalizedSignals.HintDependenceDelta:0.00}) " +
                    $"but no hint-dependence hypothesis");
            }
        }

        int guardrailConflictCount = warnings.Count;

        // ── Overall behavior state ────────────────────────────────────────────────
        string overallBehaviorState;

        if (guardrailConflictCount >= 2)
        {
            overallBehaviorState = "conflicted";
        }
        else if (labelStableMastery && stableMasterySignalAgreement)
        {
            overallBehaviorState = "mastering";
        }
        else if (labelImproving && !labelConfusion && !labelRelapse)
        {
            overallBehaviorState = "recovering";
        }
        else if ((labelConfusion || labelRelapse) && confusionSignalAgreement)
        {
            overallBehaviorState = "struggling";
        }
        else if (labelStableMastery)
        {
            // Mastery hypothesis present but ML does not fully agree — treat as stable not conflicted.
            overallBehaviorState = "stable";
        }
        else if (labelImproving)
        {
            overallBehaviorState = "recovering";
        }
        else if (guardrailConflictCount >= 1)
        {
            overallBehaviorState = "conflicted";
        }
        else
        {
            overallBehaviorState = "stable";
        }

        return new MultiSignalGuardrailSummary
        {
            OverallBehaviorState = overallBehaviorState,
            ConfusionSignalAgreement = confusionSignalAgreement,
            StableMasterySignalAgreement = stableMasterySignalAgreement,
            HintDependenceRiskLevel = hintDependenceRiskLevel,
            GuardrailConflictCount = guardrailConflictCount,
            GuardrailWarnings = warnings
        };
    }
}
