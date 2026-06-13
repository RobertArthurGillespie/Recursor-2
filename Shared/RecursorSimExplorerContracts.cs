namespace NCATAIBlazorFrontendTest.Shared;

// ── Phase 16A: Sim Explorer read-only dashboard contracts ──────────────────────
// Shared between Server (API responses) and Client (Blazor deserialization).

public class SimExplorerSimSummaryDto
{
    public string SimId { get; set; } = "";
    public int ScenarioCount { get; set; }
    public int UserCount { get; set; }
    public int SessionCount { get; set; }
    public int WindowCount { get; set; }
    public long RawEventCount { get; set; }
    public int AdaptationCount { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public class SimExplorerUserSummaryDto
{
    public string SimId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int ScenarioCount { get; set; }
    public int SessionCount { get; set; }
    public int WindowCount { get; set; }
    public long RawEventCount { get; set; }
    public int AdaptationCount { get; set; }
    public double? AvgConfusionScore { get; set; }
    public double? AvgGoalUnderstanding { get; set; }
    public double? AvgHintDependenceScore { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public class SimExplorerSessionSummaryDto
{
    public string SessionId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int WindowCount { get; set; }
    public long RawEventCount { get; set; }
    public int AdaptationCount { get; set; }
    public double? AvgConfusionScore { get; set; }
    public double? AvgGoalUnderstanding { get; set; }
    public double? AvgHintDependenceScore { get; set; }
    public long SafetyErrorCount { get; set; }
    public long ErrorCount { get; set; }
    public long SuccessCount { get; set; }
}

public class SimExplorerSessionDetailDto
{
    public string SessionId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public SessionTimelineDto? Timeline { get; set; }
    public List<SimExplorerRawEventDto> RawEvents { get; set; } = [];
    public bool RawEventsTruncated { get; set; }
    public List<SimExplorerEventCountDto> EventCountsByCategory { get; set; } = [];
    public List<SimExplorerEventCountDto> EventCountsByEventType { get; set; } = [];
}

public class SimExplorerRawEventDto
{
    public string EventId { get; set; } = "";
    public long SequenceNumber { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Target { get; set; } = "";
    public string MetricsJson { get; set; } = "";
    public string ContextJson { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}

public class SimExplorerEventCountDto
{
    public string Label { get; set; } = "";
    public long Count { get; set; }
}
