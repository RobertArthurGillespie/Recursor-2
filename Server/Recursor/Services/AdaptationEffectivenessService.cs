using System.Collections.Concurrent;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Services;

public interface IAdaptationEffectivenessService
{
    // Call after a real adaptation is committed for a session window.
    // preVector is the feature vector from the same pipeline invocation that produced the adaptation.
    void RecordAppliedAdaptation(string sessionId, AdaptationDecisionDocument adaptation, BehaviorStateFeatureVector preVector);

    // Call at the start of the next window's training-row block.
    // Returns a document if a pending evaluation exists and nextVector.WindowIndex == appliedAt + 1.
    // Returns null when no pending evaluation exists or the window is not immediately next.
    AdaptationEffectivenessDocument? EvaluateNextWindow(string sessionId, BehaviorStateFeatureVector nextVector);
}

public class AdaptationEffectivenessService : IAdaptationEffectivenessService
{
    private readonly ConcurrentDictionary<string, PendingAdaptationEvaluation> _pending = new();

    public void RecordAppliedAdaptation(
        string sessionId,
        AdaptationDecisionDocument adaptation,
        BehaviorStateFeatureVector preVector)
    {
        _pending[sessionId] = new PendingAdaptationEvaluation
        {
            AdaptationDecisionId  = adaptation.Id,
            DecisionIndex         = adaptation.DecisionIndex,
            UserId                = adaptation.UserId,
            InterventionFamilies  = new List<string>(adaptation.InterventionFamilies),
            ParameterChanges      = new List<ParameterChange>(adaptation.ParameterChanges),
            AppliedAtWindowIndex  = preVector.WindowIndex,
            PreConfusionScore     = preVector.ConfusionScore,
            PreHintDependenceScore = preVector.HintDependenceScore,
            PreGoalUnderstanding  = preVector.GoalUnderstanding,
        };
    }

    public AdaptationEffectivenessDocument? EvaluateNextWindow(
        string sessionId,
        BehaviorStateFeatureVector nextVector)
    {
        if (!_pending.TryGetValue(sessionId, out var pending))
            return null;

        if (nextVector.WindowIndex != pending.AppliedAtWindowIndex + 1)
            return null;

        _pending.TryRemove(sessionId, out _);

        var confusionDelta       = nextVector.ConfusionScore    - pending.PreConfusionScore;
        var hintDependenceDelta  = nextVector.HintDependenceScore - pending.PreHintDependenceScore;
        var goalUnderstandingDelta = nextVector.GoalUnderstanding - pending.PreGoalUnderstanding;

        var outcome = confusionDelta <= -0.05 ? "improved"
                    : confusionDelta >=  0.05 ? "worsened"
                    : "neutral";

        return new AdaptationEffectivenessDocument
        {
            Id                       = Guid.NewGuid().ToString(),
            SessionId                = sessionId,
            UserId                   = pending.UserId,
            AdaptationDecisionId     = pending.AdaptationDecisionId,
            AdaptationDecisionIndex  = pending.DecisionIndex,
            InterventionFamilies     = pending.InterventionFamilies,
            ParameterChanges         = pending.ParameterChanges,
            AppliedAtWindowIndex     = pending.AppliedAtWindowIndex,
            EvaluatedAtWindowIndex   = nextVector.WindowIndex,
            PreConfusionScore        = pending.PreConfusionScore,
            PreHintDependenceScore   = pending.PreHintDependenceScore,
            PreGoalUnderstanding     = pending.PreGoalUnderstanding,
            NextConfusionScore       = nextVector.ConfusionScore,
            NextHintDependenceScore  = nextVector.HintDependenceScore,
            NextGoalUnderstanding    = nextVector.GoalUnderstanding,
            ConfusionDelta           = confusionDelta,
            HintDependenceDelta      = hintDependenceDelta,
            GoalUnderstandingDelta   = goalUnderstandingDelta,
            Outcome                  = outcome,
        };
    }
}

internal sealed class PendingAdaptationEvaluation
{
    public string AdaptationDecisionId  { get; set; } = "";
    public int    DecisionIndex         { get; set; }
    public string UserId                { get; set; } = "";
    public List<string> InterventionFamilies { get; set; } = new();
    public List<ParameterChange> ParameterChanges { get; set; } = new();
    public int    AppliedAtWindowIndex  { get; set; }
    public double PreConfusionScore     { get; set; }
    public double PreHintDependenceScore { get; set; }
    public double PreGoalUnderstanding  { get; set; }
}
