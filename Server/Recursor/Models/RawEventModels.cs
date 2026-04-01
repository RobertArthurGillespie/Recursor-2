namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class RawEventBatch
{
    public string SchemaVersion { get; set; } = "1.0";
    public string BatchId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string SimVersion { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public DateTime ClientTimestampUtc { get; set; }
    public int BatchSequence { get; set; }
    public List<RawEventRecord> Events { get; set; } = new();
}

public class RawEventRecord
{
    public string EventId { get; set; } = "";
    public long SequenceNumber { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Target { get; set; } = "";
    public Dictionary<string, object?> Context { get; set; } = new();
    public EventMetrics Metrics { get; set; } = new();
    public Dictionary<string, object?> Payload { get; set; } = new();
}

public class EventMetrics
{
    public double? Value { get; set; }
    public double? DurationMs { get; set; }
    public double? Distance { get; set; }
    public double? Score { get; set; }
    public Dictionary<string, double>? AdditionalMetrics { get; set; }
}
