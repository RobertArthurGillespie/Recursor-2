using NCATAIBlazorFrontendTest.Server.Recursor.Models;

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
    public string? ReasoningSummary { get; set; }
    public string? Error { get; set; }
}
