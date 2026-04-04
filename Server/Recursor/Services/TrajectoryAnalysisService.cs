using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface ITrajectoryAnalysisService
{
    TrajectoryAnalysisResult Analyze(SessionDocument session, BehaviorProfileDocument currentProfile);
}

public class TrajectoryAnalysisService : ITrajectoryAnalysisService
{
    public TrajectoryAnalysisResult Analyze(SessionDocument session, BehaviorProfileDocument currentProfile)
    {
        var snapshots = session.RecentSnapshots;

        if (snapshots.Count < 2)
        {
            return new TrajectoryAnalysisResult { HasEnoughHistory = false };
        }

        // Extract current dimension values.
        var dimScores = currentProfile.DimensionScores;
        var behaviorScores = currentProfile.BehaviorScores;

        double currentGoal = dimScores.TryGetValue("goalUnderstanding", out var gScore) ? gScore.Score : 0.0;
        double currentAttention = dimScores.TryGetValue("attentionDetection", out var aScore) ? aScore.Score : 0.0;
        double currentConfusion = behaviorScores?.ConfusionScore ?? 0.0;
        double currentHintDependence = behaviorScores?.HintDependenceScore ?? 0.0;

        // Average across all previous snapshots.
        double avgGoal = snapshots.Average(s => s.GoalUnderstanding);
        double avgAttention = snapshots.Average(s => s.AttentionDetection);
        double avgConfusion = snapshots.Average(s => s.ConfusionScore);
        double avgHintDependence = snapshots.Average(s => s.HintDependenceScore);

        double confusionTrend = currentConfusion - avgConfusion;
        double hintDependenceTrend = currentHintDependence - avgHintDependence;
        double goalTrend = currentGoal - avgGoal;
        double attentionTrend = currentAttention - avgAttention;

        var result = new TrajectoryAnalysisResult
        {
            HasEnoughHistory = true,
            ConfusionTrend = confusionTrend,
            HintDependenceTrend = hintDependenceTrend,
            GoalTrend = goalTrend,
            AttentionTrend = attentionTrend
        };

        // A. stable_mastery_pattern
        bool stableMastery = behaviorScores is not null
            && currentGoal >= 0.75
            && currentAttention >= 0.75
            && currentConfusion < 0.35
            && currentHintDependence < 0.35
            && avgGoal >= 0.70
            && avgAttention >= 0.70;

        if (stableMastery)
        {
            result.IsStableHighPerformance = true;
            result.TrajectoryLabels.Add("stable_mastery_pattern");
        }

        // B. relapse_pattern
        bool relapse = avgGoal >= 0.70
            && (confusionTrend > 0.20
                || hintDependenceTrend > 0.20
                || goalTrend < -0.20
                || attentionTrend < -0.20);

        if (relapse)
        {
            result.IsRelapsing = true;
            result.TrajectoryLabels.Add("relapse_pattern");
        }

        // C. improving_pattern
        bool improving = goalTrend > 0.10
            && attentionTrend > 0.10
            && confusionTrend < -0.10;

        if (improving)
        {
            result.IsImproving = true;
            result.TrajectoryLabels.Add("improving_pattern");
        }

        // D. worsening_pattern
        bool worsening = goalTrend < -0.10
            && attentionTrend < -0.10
            && confusionTrend > 0.10;

        if (worsening)
        {
            result.IsWorsening = true;
            result.TrajectoryLabels.Add("worsening_pattern");
        }

        return result;
    }
}
