using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── User Profile → Policy Threshold Mapper (Phase 6A) ────────────────────────
// Projects the threshold override fields of UserBehaviorProfile into a
// UserPolicyThresholds instance for consumption by AdaptationPolicyService.
//
// Null fields are preserved as null — AdaptationPolicyService resolves them
// against the global RecursorPoliciesOptions defaults, exactly as it does
// when userThresholds is null or a field was never set.
// ─────────────────────────────────────────────────────────────────────────────

public static class UserProfileThresholdMapper
{
    public static UserPolicyThresholds ToUserPolicyThresholds(UserBehaviorProfile profile) =>
        new()
        {
            UserId = profile.UserId,

            ConfusionBlockDifficultyIncreaseThreshold          = profile.ConfusionBlockDifficultyIncreaseThreshold,

            HintFadeConfusionDeltaThreshold                    = profile.HintFadeConfusionDeltaThreshold,
            HintFadeHintDependenceDeltaThreshold               = profile.HintFadeHintDependenceDeltaThreshold,
            HintFadeMaxCurrentConfusionScore                   = profile.HintFadeMaxCurrentConfusionScore,
            HintFadeMinCurrentGoalUnderstanding                = profile.HintFadeMinCurrentGoalUnderstanding,

            DifficultyAccelerationConfusionDeltaThreshold      = profile.DifficultyAccelerationConfusionDeltaThreshold,
            DifficultyAccelerationHintDependenceDeltaThreshold = profile.DifficultyAccelerationHintDependenceDeltaThreshold,
            DifficultyAccelerationMaxCurrentConfusionScore     = profile.DifficultyAccelerationMaxCurrentConfusionScore,
            DifficultyAccelerationMinCurrentGoalUnderstanding  = profile.DifficultyAccelerationMinCurrentGoalUnderstanding,
            DifficultyAccelerationDelta                        = profile.DifficultyAccelerationDelta,
        };
}
