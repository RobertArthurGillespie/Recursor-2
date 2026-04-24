using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── User Profile Update Service (Phase 6B) ────────────────────────────────────
// Updates the longitudinal UserBehaviorProfile for a user after each produced
// behavior window. Uses an incremental mean (Welford's online algorithm) so
// running averages are computed in O(1) without replaying history.
//
// Only updates behavioral baseline fields. Threshold override fields are never
// touched — those come from manual tuning or a future auto-calibration phase.
//
// Side effects:
//   1. Upserts the updated profile into IUserProfileRepository (in-memory cache).
//   2. Appends a UserBehaviorProfileUpdate row to ADX (per-window history).
//   3. Appends a UserBehaviorProfile snapshot row to ADX (audit / cold-start seed).
//      ADX is append-only; latest snapshot is retrieved with arg_max(UpdatedAtUtc, *).
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserProfileUpdateService
{
    Task UpdateProfileAsync(
        string userId,
        string sessionId,
        int windowIndex,
        BehaviorProfileDocument behaviorProfile,
        IReadOnlyList<string> hypothesisLabels);
}

public class UserProfileUpdateService : IUserProfileUpdateService
{
    private readonly IUserProfileRepository _profileRepository;
    private readonly IAdxIngestionService _adxIngestion;
    private readonly ILogger<UserProfileUpdateService> _logger;

    public UserProfileUpdateService(
        IUserProfileRepository profileRepository,
        IAdxIngestionService adxIngestion,
        ILogger<UserProfileUpdateService> logger)
    {
        _profileRepository = profileRepository;
        _adxIngestion = adxIngestion;
        _logger = logger;
    }

    public async Task UpdateProfileAsync(
        string userId,
        string sessionId,
        int windowIndex,
        BehaviorProfileDocument behaviorProfile,
        IReadOnlyList<string> hypothesisLabels)
    {
        var now = DateTime.UtcNow;

        // ── Extract behavioral values from this window ─────────────────────────
        var confusionScore      = behaviorProfile.BehaviorScores?.ConfusionScore      ?? 0.0;
        var hintDependenceScore = behaviorProfile.BehaviorScores?.HintDependenceScore ?? 0.0;
        var hesitationScore     = behaviorProfile.BehaviorScores?.HesitationScore     ?? 0.0;

        var goalUnderstanding  = GetDimensionScore(behaviorProfile, "goalUnderstanding");
        var attentionDetection = GetDimensionScore(behaviorProfile, "attentionDetection");
        var procedureSequencing = GetDimensionScore(behaviorProfile, "procedureSequencing");
        var selfCorrection     = GetDimensionScore(behaviorProfile, "selfCorrection");
        var taskContinuity     = GetDimensionScore(behaviorProfile, "taskContinuity");

        var hasStableMastery  = hypothesisLabels.Contains("stable_mastery_pattern");
        var hasConfusion      = hypothesisLabels.Contains("confusion_pattern")      || hypothesisLabels.Contains("goal-confusion");
        var hasHintDependence = hypothesisLabels.Contains("hint_dependence_pattern") || hypothesisLabels.Contains("hint-dependency");

        // ── Load or create profile ────────────────────────────────────────────
        var profile = await _profileRepository.GetAsync(userId);
        bool isNew = profile is null;

        if (isNew)
        {
            profile = new UserBehaviorProfile
            {
                UserId       = userId,
                CreatedAtUtc = now,
            };
        }

        // ── Update counts ─────────────────────────────────────────────────────
        profile!.TotalWindows += 1;
        int newN = profile.TotalWindows;

        if (profile.LastSessionId != sessionId)
        {
            profile.TotalSessions += 1;
            profile.LastSessionId  = sessionId;
        }

        profile.UpdatedAtUtc = now;

        // ── Incremental mean (Welford's algorithm) ────────────────────────────
        profile.AvgConfusionScore      = IncrementalMean(profile.AvgConfusionScore,      confusionScore,      newN);
        profile.AvgHintDependenceScore = IncrementalMean(profile.AvgHintDependenceScore, hintDependenceScore, newN);
        profile.AvgHesitationScore     = IncrementalMean(profile.AvgHesitationScore,     hesitationScore,     newN);
        profile.AvgGoalUnderstanding   = IncrementalMean(profile.AvgGoalUnderstanding,   goalUnderstanding,   newN);
        profile.AvgAttentionDetection  = IncrementalMean(profile.AvgAttentionDetection,  attentionDetection,  newN);
        profile.AvgProcedureSequencing = IncrementalMean(profile.AvgProcedureSequencing, procedureSequencing, newN);
        profile.AvgSelfCorrection      = IncrementalMean(profile.AvgSelfCorrection,      selfCorrection,      newN);
        profile.AvgTaskContinuity      = IncrementalMean(profile.AvgTaskContinuity,      taskContinuity,      newN);

        profile.StableMasteryRate  = IncrementalMean(profile.StableMasteryRate,  hasStableMastery  ? 1.0 : 0.0, newN);
        profile.ConfusionRate      = IncrementalMean(profile.ConfusionRate,      hasConfusion      ? 1.0 : 0.0, newN);
        profile.HintDependenceRate = IncrementalMean(profile.HintDependenceRate, hasHintDependence ? 1.0 : 0.0, newN);

        // Threshold overrides are never touched here — they come from manual tuning.

        // ── Persist updated profile ───────────────────────────────────────────
        await _profileRepository.UpsertAsync(profile);

        _logger.LogInformation(
            isNew
                ? "Created user behavior profile for userId '{UserId}' (session {SessionId}, window {WindowIndex})."
                : "Updated user behavior profile for userId '{UserId}' (session {SessionId}, window {WindowIndex}, TotalWindows={TotalWindows}).",
            userId, sessionId, windowIndex, profile.TotalWindows);

        // ── ADX: append per-window update row ────────────────────────────────
        var updateRow = AdxRowMapper.MapUserBehaviorProfileUpdate(
            userId, sessionId, windowIndex, now,
            confusionScore, hintDependenceScore, hesitationScore,
            goalUnderstanding, attentionDetection, procedureSequencing, selfCorrection, taskContinuity,
            hasStableMastery, hasConfusion, hasHintDependence);

        await _adxIngestion.IngestUserBehaviorProfileUpdateAsync(updateRow);

        // ── ADX: append full profile snapshot (arg_max on UpdatedAtUtc gives latest) ─
        await _adxIngestion.IngestUserBehaviorProfileAsync(AdxRowMapper.MapUserBehaviorProfile(profile));
    }

    // Welford's online mean: mean_n = mean_{n-1} + (value - mean_{n-1}) / n
    // Returns value directly for the first window (n == 1) to avoid floating-point
    // drift from the initial 0.0 default.
    internal static double IncrementalMean(double oldMean, double newValue, int newCount)
        => newCount <= 1 ? newValue : oldMean + (newValue - oldMean) / newCount;

    private static double GetDimensionScore(BehaviorProfileDocument profile, string key)
        => profile.DimensionScores.TryGetValue(key, out var ds) ? ds.Score : 0.0;
}
