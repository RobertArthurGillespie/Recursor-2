using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── User Threshold Derivation Service (Phase 6C) ──────────────────────────────
// Derives per-user policy threshold overrides from accumulated longitudinal
// behavioral data in UserBehaviorProfile.
//
// Formulas are intentionally simple, conservative, and bounded.
// Only fires when TotalWindows >= 10; below that floor the profile has too little
// history to justify personalizing thresholds, so all threshold fields are
// left unchanged (null → global defaults remain active).
//
// This service mutates the threshold override fields of the UserBehaviorProfile
// in place. It is called by UserProfileUpdateService after baseline averages
// and rates have been updated and before the profile is upserted to the repository.
//
// Bounds applied per field:
//   ConfusionBlockDifficultyIncreaseThreshold : [0.02, 0.15]
//   delta thresholds (negative)               : [-0.15, -0.02]
//   max confusion score gates                 : [0.15, 0.45]
//   min goal understanding gates              : [0.50, 0.85]
//   acceleration delta                        : [0.05, 0.10]
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserThresholdDerivationService
{
    void DeriveThresholds(UserBehaviorProfile profile);
}

public class UserThresholdDerivationService : IUserThresholdDerivationService
{
    private const int MinimumWindowsRequired = 10;

    private const double ConfusionBlockMin  = 0.02;
    private const double ConfusionBlockMax  = 0.15;
    private const double DeltaMin           = -0.15;
    private const double DeltaMax           = -0.02;
    private const double MaxConfusionMin    = 0.15;
    private const double MaxConfusionMax    = 0.45;
    private const double MinGoalMin         = 0.50;
    private const double MinGoalMax         = 0.85;
    private const double AccelDeltaMin      = 0.05;
    private const double AccelDeltaMax      = 0.10;

    private readonly ILogger<UserThresholdDerivationService> _logger;

    public UserThresholdDerivationService(ILogger<UserThresholdDerivationService> logger)
    {
        _logger = logger;
    }

    public void DeriveThresholds(UserBehaviorProfile profile)
    {
        if (profile.TotalWindows < MinimumWindowsRequired)
            return;

        double confusionRate      = profile.ConfusionRate;
        double stableMasteryRate  = profile.StableMasteryRate;
        double hintDependenceRate = profile.HintDependenceRate;
        double avgConfusion       = profile.AvgConfusionScore;
        double avgGoal            = profile.AvgGoalUnderstanding;

        // ── Confusion-block difficulty-increase rule ──────────────────────────
        // Lower threshold for users with high confusion rate (more sensitive protection).
        // Higher threshold for stable, low-confusion users (looser protection).
        double confusionBlock =
            confusionRate >= 0.35 ? 0.03 :
            stableMasteryRate >= 0.60 && avgConfusion <= 0.20 ? 0.08 :
            0.05;
        profile.ConfusionBlockDifficultyIncreaseThreshold = Clamp(confusionBlock, ConfusionBlockMin, ConfusionBlockMax);

        // ── Personalized hint-fade rule ───────────────────────────────────────
        // Stable users need a smaller improvement signal to earn hint fade.
        // High hint-dependence users require stronger hint-dependence reduction.
        double hintFadeConfusionDelta = stableMasteryRate >= 0.60 ? -0.03 : -0.06;
        profile.HintFadeConfusionDeltaThreshold = Clamp(hintFadeConfusionDelta, DeltaMin, DeltaMax);

        double hintFadeHintDependenceDelta = hintDependenceRate >= 0.35 ? -0.08 : -0.04;
        profile.HintFadeHintDependenceDeltaThreshold = Clamp(hintFadeHintDependenceDelta, DeltaMin, DeltaMax);

        // Low-confusion users can tolerate a slightly higher absolute confusion gate.
        double hintFadeMaxConfusion = avgConfusion <= 0.20 ? 0.30 : 0.22;
        profile.HintFadeMaxCurrentConfusionScore = Clamp(hintFadeMaxConfusion, MaxConfusionMin, MaxConfusionMax);

        // High-goal users set a lower absolute goal-understanding floor.
        double hintFadeMinGoal = avgGoal >= 0.70 ? 0.60 : 0.70;
        profile.HintFadeMinCurrentGoalUnderstanding = Clamp(hintFadeMinGoal, MinGoalMin, MinGoalMax);

        // ── Personalized difficulty acceleration rule ─────────────────────────
        // Conservative acceleration for stable users; strict for weaker users.
        double accelConfusionDelta = stableMasteryRate >= 0.60 ? -0.04 : -0.08;
        profile.DifficultyAccelerationConfusionDeltaThreshold = Clamp(accelConfusionDelta, DeltaMin, DeltaMax);

        double accelHintDependenceDelta = hintDependenceRate <= 0.20 ? -0.03 : -0.06;
        profile.DifficultyAccelerationHintDependenceDeltaThreshold = Clamp(accelHintDependenceDelta, DeltaMin, DeltaMax);

        double accelMaxConfusion = avgConfusion <= 0.20 ? 0.30 : 0.22;
        profile.DifficultyAccelerationMaxCurrentConfusionScore = Clamp(accelMaxConfusion, MaxConfusionMin, MaxConfusionMax);

        double accelMinGoal = avgGoal >= 0.70 ? 0.60 : 0.75;
        profile.DifficultyAccelerationMinCurrentGoalUnderstanding = Clamp(accelMinGoal, MinGoalMin, MinGoalMax);

        // Only the strongest, most stable users earn an accelerated difficulty step.
        double accelDelta = stableMasteryRate >= 0.70 && avgConfusion <= 0.20 ? 0.10 : 0.08;
        profile.DifficultyAccelerationDelta = Clamp(accelDelta, AccelDeltaMin, AccelDeltaMax);

        _logger.LogInformation(
            "Phase 6C thresholds derived for userId='{UserId}': TotalWindows={TotalWindows}, " +
            "StableMasteryRate={StableMasteryRate:F2}, ConfusionRate={ConfusionRate:F2}, " +
            "HintDependenceRate={HintDependenceRate:F2}. " +
            "ConfusionBlock={ConfusionBlock:F3}, AccelDelta={AccelDelta:F3}, " +
            "HintFadeConfusionDelta={HintFadeConfusionDelta:F3}",
            profile.UserId,
            profile.TotalWindows,
            stableMasteryRate,
            confusionRate,
            hintDependenceRate,
            profile.ConfusionBlockDifficultyIncreaseThreshold,
            profile.DifficultyAccelerationDelta,
            profile.HintFadeConfusionDeltaThreshold);
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));
}
