namespace NCATAIBlazorFrontendTest.Shared;

// ── Phase 14A: Internal Recursor Evaluation Dashboard contracts ───────────────
// Shared between Server (API responses) and Client (Blazor deserialization).

public class DashboardSessionSummaryDto
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int WindowCount { get; set; }
    public int AdaptationCount { get; set; }
    public int PredictionCount { get; set; }
}

public class SessionTimelineDto
{
    public string SessionId { get; set; } = "";
    public int TotalWindows { get; set; }
    public int TotalAdaptations { get; set; }
    public int TotalPredictions { get; set; }
    public double? AveragePredictionConfidence { get; set; }
    public double? H1PredictionAccuracy { get; set; }
    public double? H2PredictionAccuracy { get; set; }
    public double? H3PredictionAccuracy { get; set; }
    public List<WindowTimelineDto> Windows { get; set; } = [];
}

public class WindowTimelineDto
{
    public int WindowIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string UserId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";

    // Behavior scores
    public double ConfusionScore { get; set; }
    public double GoalUnderstanding { get; set; }
    public double HintDependenceScore { get; set; }

    // Guardrail state (Phase 7C)
    public string GuardrailOverallBehaviorState { get; set; } = "";
    public string GuardrailHintDependenceRiskLevel { get; set; } = "";

    // Sequence features (Phase 10A)
    public int SequenceWindowCount { get; set; }
    public double VolatilityScore { get; set; }
    public double MomentumScore { get; set; }
    public double StabilityScore { get; set; }

    // Trajectory-derived near-term risk
    public string PredictedNearTermRisk { get; set; } = "";

    // Adaptation decision fired at this window (null when no adaptation)
    public AdaptationDecisionTimelineDto? AdaptationDecision { get; set; }

    // Temporal risk model predictions from this window (Phase 10C — 3-class: low/medium/high)
    public List<TemporalRiskPredictionTimelineDto> TemporalRiskPredictions { get; set; } = [];

    // Binary elevated-risk predictions from this window (Phase 10D-1 — low / elevated)
    public List<TemporalElevatedRiskPredictionTimelineDto> ElevatedRiskPredictions { get; set; } = [];

    // Observed future targets recorded when later windows were processed (Phase 10B)
    public List<TemporalTargetTimelineDto> TemporalPredictionTargets { get; set; } = [];

    // Prediction correctness: predicted risk vs observed target risk (null = target not yet available)
    public bool? H1PredictionCorrect { get; set; }
    public bool? H2PredictionCorrect { get; set; }
    public bool? H3PredictionCorrect { get; set; }
}

public class AdaptationDecisionTimelineDto
{
    public DateTime CreatedAtUtc { get; set; }
    public int DecisionIndex { get; set; }
    public string InterventionFamiliesJson { get; set; } = "";
    public string ParameterChangesJson { get; set; } = "";
    public string ReasoningSummary { get; set; } = "";
    public string? Phase8AGuardrailNotes { get; set; }
    public string? Phase8EReliabilityNotes { get; set; }
    public string? Phase10TrajectoryNotes { get; set; }
}

public class TemporalRiskPredictionTimelineDto
{
    public int Horizon { get; set; }
    public string PredictedNearTermRisk { get; set; } = "";
    public double Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
}

public class TemporalElevatedRiskPredictionTimelineDto
{
    public int Horizon { get; set; }
    public string PredictedElevatedRisk { get; set; } = "";
    public double Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
}

public class TemporalTargetTimelineDto
{
    public int Horizon { get; set; }
    public int TargetWindowIndex { get; set; }
    public string TargetNearTermRisk { get; set; } = "";
    public string TargetBehaviorState { get; set; } = "";
    public double TargetConfusionScore { get; set; }
    public double TargetGoalUnderstanding { get; set; }
    public double TargetHintDependenceScore { get; set; }
}

// Phase 10D-4: per-(ModelVersion, Horizon) summary row for the elevated-risk model comparison endpoint.
public class ElevatedRiskModelComparisonRow
{
    public string ModelVersion { get; set; } = "";
    public int Horizon { get; set; }
    public int Total { get; set; }
    public int Correct { get; set; }
    public double Accuracy { get; set; }
    public int ActualElevated { get; set; }
    public int TruePositive { get; set; }
    public int FalseNegative { get; set; }
    public int FalsePositive { get; set; }
    public double? ElevatedPrecision { get; set; }
    public double? ElevatedRecall { get; set; }
    public double? ElevatedF1 { get; set; }
}
