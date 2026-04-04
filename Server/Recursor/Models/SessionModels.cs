namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class SessionDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "Session";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public long EventCount { get; set; }
    public int BatchCount { get; set; }
    // Tracks events received since the last feature window was generated.
    // Reset to 0 after each window. Used by FeatureExtractionService to decide
    // whether accumulation threshold has been reached.
    public long EventsSinceLastWindow { get; set; }
    public string CurrentStage { get; set; } = "";
    public Dictionary<string, string> CurrentDifficultyProfile { get; set; } = new();
    public string? LatestFeatureWindowId { get; set; }
    public string? LatestBehaviorProfileId { get; set; }
    public string? LatestHypothesisSetId { get; set; }
    public string? LatestAdaptationId { get; set; }
    public SessionSummary Summary { get; set; } = new();
    public List<TrajectorySnapshot> RecentSnapshots { get; set; } = new();
    public int ConsecutiveStableMasteryWindows { get; set; }
    public int ConsecutiveRelapseWindows { get; set; }
}

public class SessionSummary
{
    public double CompletionPercent { get; set; }
    public int ErrorCount { get; set; }
    public int HintCount { get; set; }
    public int SafetyViolationCount { get; set; }
}
