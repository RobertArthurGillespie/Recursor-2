using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services
{
    public interface IBehaviorScoringService
    {
        BehaviorScores Score(FeatureWindowDocument featureWindow);
    }

    public class BehaviorScoringService:IBehaviorScoringService
    {
        public BehaviorScores Score(FeatureWindowDocument featureWindow)
        {
            var features = featureWindow.Features;

            // These are already normalized-ish scores
            double attention = Clamp01(features.AttentionDetection);
            double feedback = Clamp01(features.FeedbackResponsiveness);
            double pace = Clamp01(features.PaceRegulation);
            double goal = Clamp01(features.GoalUnderstanding);
            double correction = Clamp01(features.SelfCorrection);

            // Core state scores
            double confusionScore =
                (0.45 * (1.0 - goal)) +
                (0.35 * (1.0 - attention)) +
                (0.20 * (1.0 - feedback));

            double hesitationScore =
                (0.60 * (1.0 - pace)) +
                (0.25 * (1.0 - goal)) +
                (0.15 * (1.0 - correction));

            // Fast pace only looks impulsive when paired with weak attention / understanding / correction
            double impulsivityScore =
                (0.45 * pace * (1.0 - attention)) +
                (0.30 * (1.0 - correction)) +
                (0.25 * (1.0 - goal));

            // Hint dependence should rise when feedback / hint engagement is high,
            // but should still depend on incomplete independent performance.
            double hintDependenceScore =
                (0.18 * feedback) +
                (0.32 * confusionScore) +
                (0.28 * (1.0 - goal)) +
                (0.22 * (1.0 - correction));

            // Clamp
            confusionScore = Clamp01(confusionScore);
            hesitationScore = Clamp01(hesitationScore);
            impulsivityScore = Clamp01(impulsivityScore);
            hintDependenceScore = Clamp01(hintDependenceScore);

            return new BehaviorScores
            {
                ConfusionScore = confusionScore,
                HesitationScore = hesitationScore,
                ImpulsivityScore = impulsivityScore,
                HintDependenceScore = hintDependenceScore,
                PredictedState = PredictState(confusionScore, hesitationScore, impulsivityScore, hintDependenceScore)
            };
        }

        private static string PredictState(
    double confusionScore,
    double hesitationScore,
    double impulsivityScore,
    double hintDependenceScore)
        {
            if (hintDependenceScore >= 0.68 && confusionScore >= 0.50)
                return "confused_and_hint_dependent";

            if (hintDependenceScore >= 0.75)
                return "hint_dependent";

            if (impulsivityScore >= 0.65)
                return "impulsive";

            if (hesitationScore >= 0.65)
                return "hesitant";

            if (confusionScore >= 0.60)
                return "confused";

            return "stable_or_mixed";
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static double NormalizeCount(int count, int maxUseful)
        {
            if (maxUseful <= 0) return 0.0;
            return Clamp01((double)count / maxUseful);
        }

        private static double NormalizeMs(double ms, double low, double high)
        {
            if (ms <= low) return 0.0;
            if (ms >= high) return 1.0;
            return (ms - low) / (high - low);
        }
    }
}
