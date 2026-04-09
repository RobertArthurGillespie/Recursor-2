using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Maps a <see cref="BehaviorStateFeatureVector"/> (double/int fields) to a
/// <see cref="BehaviorStatePredictionInput"/> (float fields, ML.NET convention).
/// </summary>
public static class BehaviorStatePredictionMapper
{
    public static BehaviorStatePredictionInput Map(BehaviorStateFeatureVector source)
    {
        return new BehaviorStatePredictionInput
        {
            // Dimension scores
            AttentionDetection       = (float)source.AttentionDetection,
            GoalUnderstanding        = (float)source.GoalUnderstanding,
            ProcedureSequencing      = (float)source.ProcedureSequencing,
            PaceRegulation           = (float)source.PaceRegulation,
            SelfCorrection           = (float)source.SelfCorrection,
            FeedbackResponsiveness   = (float)source.FeedbackResponsiveness,
            SafetyCompliance         = (float)source.SafetyCompliance,
            TaskContinuity           = (float)source.TaskContinuity,

            // Higher-order behavior scores
            ConfusionScore           = (float)source.ConfusionScore,
            HesitationScore          = (float)source.HesitationScore,
            ImpulsivityScore         = (float)source.ImpulsivityScore,
            HintDependenceScore      = (float)source.HintDependenceScore,

            // Trajectory features
            GoalTrend                = (float)source.GoalTrend,
            AttentionTrend           = (float)source.AttentionTrend,
            ConfusionTrend           = (float)source.ConfusionTrend,
            HintDependenceTrend      = (float)source.HintDependenceTrend,

            // Adaptive state
            CurrentHintMode          = source.CurrentHintMode ?? "",
            CurrentDifficulty        = (float)source.CurrentDifficulty,
            CurrentTimePressure      = (float)source.CurrentTimePressure,
            CurrentErrorTolerance    = (float)source.CurrentErrorTolerance,

            // Trajectory counters
            ConsecutiveStableMasteryWindows = (float)source.ConsecutiveStableMasteryWindows,
            ConsecutiveRelapseWindows       = (float)source.ConsecutiveRelapseWindows,

            // Window summary features
            EventCountInWindow       = (float)source.EventCountInWindow,
            ErrorCountInWindow       = (float)source.ErrorCountInWindow,
            HintCountInWindow        = (float)source.HintCountInWindow,
            StepCompleteCountInWindow = (float)source.StepCompleteCountInWindow,

            // Context
            TaskType                 = source.TaskType ?? "",
        };
    }
}
