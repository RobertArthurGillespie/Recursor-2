namespace NCATAIBlazorFrontendTest.Shared;

// DTOs for POST /api/medical-supply/debrief/generate-trend-aware.
// Shared so Unity clients can serialize the request and deserialize the response.

// Unity sends this in the request body so the backend can use client-side counts
// instead of waiting for ADX ingestion to catch up after the session ends.
public class MedicalSupplyCurrentSessionSummaryDto
{
    public int WrongBinErrors { get; set; }
    public int DamagedSupplyStocked { get; set; }
    public int UsableSupplyRejected { get; set; }
    public int SafetyErrors { get; set; }
    public int SuccessCount { get; set; }
    public int TotalErrors { get; set; }
    public int TotalActions { get; set; }
    public double? AvgConfusion { get; set; }
    public double? AvgSafetyCompliance { get; set; }
    public double? AvgGoalUnderstanding { get; set; }
    public string? LatestState { get; set; }
}

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
    // When provided, the backend uses these counts instead of querying ADX for the
    // current session, avoiding failures caused by post-session ingestion delay.
    public MedicalSupplyCurrentSessionSummaryDto? CurrentSummary { get; set; }
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
