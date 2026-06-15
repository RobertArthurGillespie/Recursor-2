namespace NCATAIBlazorFrontendTest.Shared;

// DTOs for POST /api/medical-supply/debrief/generate-trend-aware.
// Shared so Unity clients can serialize the request and deserialize the response.

public class TrendAwareDebriefRequest
{
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string CurrentSessionId { get; set; } = "";
    public double PerformanceScore { get; set; }
    public string ErrorsText { get; set; } = "";
    public string? PreviousSessionId { get; set; }
    public bool IncludePreviousSession { get; set; } = true;
}

public class TrendAwareDebriefResponse
{
    public string Headline { get; set; } = "";
    public string OverallTrend { get; set; } = "insufficient_data";
    public List<string> WhatWentWell { get; set; } = [];
    public List<string> NeedsImprovement { get; set; } = [];
    public string TrendComparison { get; set; } = "";
    public List<string> NextShiftFocus { get; set; } = [];
    public string CoachMessage { get; set; } = "";
    public string ConfidenceNote { get; set; } = "";
    public List<string> EvidenceSummary { get; set; } = [];
    public string? RawModelJson { get; set; }
}
