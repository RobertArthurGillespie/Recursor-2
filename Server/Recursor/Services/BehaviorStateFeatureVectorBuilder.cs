using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IBehaviorStateFeatureVectorBuilder
{
    BehaviorStateFeatureVector Build(
        SessionDocument session,
        RawEventBatch batch,
        BehaviorProfileDocument behaviorProfile,
        TrajectoryAnalysisResult trajectoryResult);
}

public class BehaviorStateFeatureVectorBuilder : IBehaviorStateFeatureVectorBuilder
{
    public BehaviorStateFeatureVector Build(
        SessionDocument session,
        RawEventBatch batch,
        BehaviorProfileDocument behaviorProfile,
        TrajectoryAnalysisResult trajectoryResult)
    {
        var scores = behaviorProfile.DimensionScores;
        var behaviorScores = behaviorProfile.BehaviorScores;
        var profile = session.CurrentDifficultyProfile;

        return new BehaviorStateFeatureVector
        {
            // Identity / context
            SessionId = session.SessionId,
            SimId = session.SimId,
            ScenarioId = session.ScenarioId,
            WindowIndex = behaviorProfile.WindowIndex,
            TaskType = ResolveTaskType(session.SimId),

            // Dimension scores
            AttentionDetection = GetScore(scores, "attentionDetection"),
            GoalUnderstanding = GetScore(scores, "goalUnderstanding"),
            ProcedureSequencing = GetScore(scores, "procedureSequencing"),
            PaceRegulation = GetScore(scores, "paceRegulation"),
            SelfCorrection = GetScore(scores, "selfCorrection"),
            FeedbackResponsiveness = GetScore(scores, "feedbackResponsiveness"),
            SafetyCompliance = GetScore(scores, "safetyCompliance"),
            TaskContinuity = GetScore(scores, "taskContinuity"),

            // Higher-order behavior scores
            ConfusionScore = behaviorScores?.ConfusionScore ?? 0.0,
            HesitationScore = behaviorScores?.HesitationScore ?? 0.0,
            ImpulsivityScore = behaviorScores?.ImpulsivityScore ?? 0.0,
            HintDependenceScore = behaviorScores?.HintDependenceScore ?? 0.0,

            // Trajectory features
            GoalTrend = trajectoryResult.GoalTrend,
            AttentionTrend = trajectoryResult.AttentionTrend,
            ConfusionTrend = trajectoryResult.ConfusionTrend,
            HintDependenceTrend = trajectoryResult.HintDependenceTrend,

            // Support / adaptive state
            CurrentHintMode = profile.TryGetValue("hintMode", out var hintMode) ? hintMode : "",
            CurrentDifficulty = ParseDouble(profile, "difficulty"),
            CurrentTimePressure = ParseDouble(profile, "timePressure"),
            CurrentErrorTolerance = ParseDouble(profile, "errorTolerance"),

            // Trajectory counters
            ConsecutiveStableMasteryWindows = session.ConsecutiveStableMasteryWindows,
            ConsecutiveRelapseWindows = session.ConsecutiveRelapseWindows,

            // Window summary features
            EventCountInWindow = batch.Events.Count,
            ErrorCountInWindow = batch.Events.Count(e => e.EventType == "error"),
            HintCountInWindow = batch.Events.Count(e => e.EventType == "hint_request"),
            StepCompleteCountInWindow = batch.Events.Count(e => e.EventType == "step_complete"),
        };
    }

    private static double GetScore(Dictionary<string, DimensionScore> scores, string key)
        => scores.TryGetValue(key, out var ds) ? ds.Score : 0.0;

    private static double ParseDouble(Dictionary<string, string> profile, string key)
        => profile.TryGetValue(key, out var raw) && double.TryParse(raw, out var val) ? val : 0.0;

    private static string ResolveTaskType(string simId) => simId switch
    {
        "sim-training-v1" => "target_selection",
        "sim-sequence-training-v1" => "ordered_sequence",
        _ => "unknown"
    };
}
