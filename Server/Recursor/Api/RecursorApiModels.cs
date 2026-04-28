using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Api;

public class StartSessionApiRequest
{
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
}

public class StartSessionApiResponse
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? Error { get; set; }
}

public class BatchApiResponse
{
    public bool Success { get; set; }
    public bool AdaptationProduced { get; set; }
    public List<ParameterChange> ParameterChanges { get; set; } = [];
    public List<string> HypothesisLabels { get; set; } = [];
    public string? ReasoningSummary { get; set; }
    public GptExplanationResult? Explanation { get; set; }
    public string? Error { get; set; }

    // Phase 10A: sequence-aware trajectory data — null when fewer than 2 windows available.
    public SequenceFeatureSummary? SequenceSummary { get; set; }
    public TrajectorySummary? TrajectorySummary { get; set; }
}

// Phase 10A: session timeline for the demo endpoint.
public class SessionTimelineResponse
{
    public string SessionId { get; set; } = "";
    public int WindowCount { get; set; }
    public List<WindowTimelineEntry> Windows { get; set; } = [];
    public TrajectorySummary? CurrentTrajectory { get; set; }
}

public class WindowTimelineEntry
{
    public int WindowIndex { get; set; }
    public double ConfusionScore { get; set; }
    public double GoalUnderstanding { get; set; }
    public double HintDependenceScore { get; set; }
    public string BehaviorState { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
