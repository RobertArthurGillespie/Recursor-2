using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

// ── User Relative Signal Service ──────────────────────────────────────────────
// Compares a user's most recent behavior-state window against their historical
// baseline to produce a UserRelativeSignals snapshot.
//
// Read-only diagnostic layer — does NOT connect to adaptation policy,
// guardrail logic, or ML inference.
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserRelativeSignalService
{
    /// <summary>
    /// Returns a UserRelativeSignals snapshot comparing the user's most recent
    /// BehaviorStateTrainingRow against their lifetime baseline averages.
    /// Returns <c>null</c> when no data exists for the user or ADX is not configured.
    /// </summary>
    Task<UserRelativeSignals?> GetUserRelativeSignalsAsync(string userId);
}

public class UserRelativeSignalService : IUserRelativeSignalService
{
    // Threshold for flagging a dimension as meaningfully above or below baseline.
    private const double FlagThreshold = 0.10;

    private readonly IAdxUserBaselineQueryService _baselineService;
    private readonly ILogger<UserRelativeSignalService> _logger;

    public UserRelativeSignalService(
        IAdxUserBaselineQueryService baselineService,
        ILogger<UserRelativeSignalService> logger)
    {
        _baselineService = baselineService;
        _logger          = logger;
    }

    public async Task<UserRelativeSignals?> GetUserRelativeSignalsAsync(string userId)
    {
        // Fetch baseline and latest training row in parallel.
        var baselineTask    = _baselineService.GetUserBaselineAsync(userId);
        var latestRowTask   = _baselineService.GetLatestTrainingRowByUserAsync(userId);

        await Task.WhenAll(baselineTask, latestRowTask);

        var baseline   = baselineTask.Result;
        var latestRow  = latestRowTask.Result;

        if (baseline is null || latestRow is null)
        {
            _logger.LogInformation(
                "UserRelativeSignals not computable for userId '{UserId}': baseline={HasBaseline}, latestRow={HasRow}.",
                userId, baseline is not null, latestRow is not null);
            return null;
        }

        // ── Deltas ────────────────────────────────────────────────────────────
        var confusionDelta           = latestRow.ConfusionScore      - baseline.AvgConfusionScore;
        var hintDependenceDelta      = latestRow.HintDependenceScore  - baseline.AvgHintDependenceScore;
        var goalUnderstandingDelta   = latestRow.GoalUnderstanding    - baseline.AvgGoalUnderstanding;
        var attentionDelta           = latestRow.AttentionDetection   - baseline.AvgAttentionDetection;
        var procedureSequencingDelta = latestRow.ProcedureSequencing  - baseline.AvgProcedureSequencing;
        var selfCorrectionDelta      = latestRow.SelfCorrection       - baseline.AvgSelfCorrection;
        var taskContinuityDelta      = latestRow.TaskContinuity       - baseline.AvgTaskContinuity;

        // ── Flags ─────────────────────────────────────────────────────────────
        var aboveConfusion     = confusionDelta        >  FlagThreshold;
        var aboveHintDependence = hintDependenceDelta  >  FlagThreshold;
        var belowGoal          = goalUnderstandingDelta < -FlagThreshold;
        var belowAttention     = attentionDelta         < -FlagThreshold;

        var signals = new UserRelativeSignals
        {
            UserId      = userId,
            SessionId   = latestRow.SessionId,
            WindowIndex = latestRow.WindowIndex,
            CreatedAtUtc = latestRow.CreatedAtUtc,

            CurrentConfusionScore      = latestRow.ConfusionScore,
            CurrentHintDependenceScore = latestRow.HintDependenceScore,
            CurrentGoalUnderstanding   = latestRow.GoalUnderstanding,
            CurrentAttentionDetection  = latestRow.AttentionDetection,
            CurrentProcedureSequencing = latestRow.ProcedureSequencing,
            CurrentSelfCorrection      = latestRow.SelfCorrection,
            CurrentTaskContinuity      = latestRow.TaskContinuity,

            BaselineConfusionScore      = baseline.AvgConfusionScore,
            BaselineHintDependenceScore = baseline.AvgHintDependenceScore,
            BaselineGoalUnderstanding   = baseline.AvgGoalUnderstanding,
            BaselineAttentionDetection  = baseline.AvgAttentionDetection,
            BaselineProcedureSequencing = baseline.AvgProcedureSequencing,
            BaselineSelfCorrection      = baseline.AvgSelfCorrection,
            BaselineTaskContinuity      = baseline.AvgTaskContinuity,

            ConfusionDelta           = confusionDelta,
            HintDependenceDelta      = hintDependenceDelta,
            GoalUnderstandingDelta   = goalUnderstandingDelta,
            AttentionDelta           = attentionDelta,
            ProcedureSequencingDelta = procedureSequencingDelta,
            SelfCorrectionDelta      = selfCorrectionDelta,
            TaskContinuityDelta      = taskContinuityDelta,

            IsAboveBaselineConfusion         = aboveConfusion,
            IsAboveBaselineHintDependence    = aboveHintDependence,
            IsBelowBaselineGoalUnderstanding = belowGoal,
            IsBelowBaselineAttention         = belowAttention,

            Summary = BuildSummary(aboveConfusion, aboveHintDependence, belowGoal, belowAttention)
        };

        return signals;
    }

    // ── Summary builder ───────────────────────────────────────────────────────

    private static string BuildSummary(
        bool aboveConfusion,
        bool aboveHintDependence,
        bool belowGoal,
        bool belowAttention)
    {
        var findings = new List<string>();

        if (aboveConfusion)      findings.Add("confusion elevated vs baseline");
        if (aboveHintDependence) findings.Add("hint dependence elevated");
        if (belowGoal)           findings.Add("goal understanding below baseline");
        if (belowAttention)      findings.Add("attention below baseline");

        if (findings.Count == 0)
            return "Performance is near baseline across tracked dimensions.";

        var sentence = string.Join("; ", findings);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..] + ".";
    }
}
