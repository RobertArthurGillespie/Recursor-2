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

    // Phase 10E: behavior-state prediction accuracy, computed only over windows whose
    // target is resolved (excludes pending and blank/"unknown" targets).
    public double? H1BehaviorStateAccuracy { get; set; }
    public double? H2BehaviorStateAccuracy { get; set; }
    public double? H3BehaviorStateAccuracy { get; set; }

    // Stage 10: the exact model version this timeline's predictions/accuracy were computed
    // against — always set (defaults to the configured version), never left ambiguous. Display
    // this alongside any accuracy figure so it's clear which version produced it.
    public string SelectedRiskModelVersion { get; set; } = "";
    public string SelectedBehaviorStateModelVersion { get; set; } = "";

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

    // Multiclass behavior-state predictions from this window (Phase 10E — shadow-only;
    // struggling/recovering/stable/mastering/conflicted). Never used to control adaptation.
    public List<TemporalBehaviorStatePredictionTimelineDto> BehaviorStatePredictions { get; set; } = [];

    // Observed future targets recorded when later windows were processed (Phase 10B)
    public List<TemporalTargetTimelineDto> TemporalPredictionTargets { get; set; } = [];

    // Prediction correctness: predicted risk vs observed target risk (null = target not yet available)
    public bool? H1PredictionCorrect { get; set; }
    public bool? H2PredictionCorrect { get; set; }
    public bool? H3PredictionCorrect { get; set; }

    // Behavior-state prediction correctness (Phase 10E). Distinct from H1/H2/H3PredictionCorrect
    // above, which compare near-term-risk predictions, not behavior-state predictions.
    // null = target not yet available or target is blank/"unknown" (never counted as incorrect).
    public bool? H1BehaviorStateCorrect { get; set; }
    public bool? H2BehaviorStateCorrect { get; set; }
    public bool? H3BehaviorStateCorrect { get; set; }
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

    // Explanation fields — null for rows persisted before this phase.
    public string? HypothesisLabelsJson { get; set; }
    public string? LearnerStateSummary { get; set; }
    public string? WhySupportChanged { get; set; }
    public string? CoachMessage { get; set; }
    public string? ConfidenceNote { get; set; }
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

public class TemporalBehaviorStatePredictionTimelineDto
{
    public int Horizon { get; set; }
    public string PredictedBehaviorState { get; set; } = "";
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

// Phase 10E: per-(ModelVersion, Horizon, ClassLabel) row for the multiclass behavior-state
// model comparison endpoint. One row per class rather than one row per (ModelVersion, Horizon)
// because the model is multiclass — the dashboard aggregates these into macro F1/accuracy.
public class BehaviorStateModelComparisonRow
{
    public string ModelVersion { get; set; } = "";
    public int Horizon { get; set; }
    public string ClassLabel { get; set; } = "";
    public int SupportCount { get; set; }
    public int PredictedCount { get; set; }
    public int TruePositive { get; set; }
    public double? Precision { get; set; }
    public double? Recall { get; set; }
    public double? F1 { get; set; }
}

// Phase 10E: coverage breakdown for the behavior-state predictor — how many predictions have
// a resolved target vs. are pending vs. landed on a missing/unknown/invalid target.
public class BehaviorStateCoverageRow
{
    public string ModelVersion { get; set; } = "";
    public int Horizon { get; set; }
    // "resolved" | "pending" | "missing-target" | "unknown-target" | "invalid-target"
    public string Coverage { get; set; } = "";
    public int Count { get; set; }
}

// Stage 11 (corrective pass): distinguishes an ADX query that genuinely returned zero rows
// from one that never ran or failed outright. Without this, an empty result and a broken
// query/auth/schema were indistinguishable to both the API consumer and the dashboard UI.
public enum DashboardQueryStatus
{
    Success,
    ServiceUnavailable,   // ADX client not configured for this environment
    AuthorizationFailed,  // ADX rejected the query on auth/permission grounds
    SchemaMissing,        // table/column referenced by the query does not exist
    QueryFailed,          // any other query execution failure
}

// Generic wrapper every Stage-11 dashboard-analytics endpoint returns instead of a bare list,
// so "zero rows" and "query failed" are never conflated.
public class DashboardQueryResult<T>
{
    public DashboardQueryStatus Status { get; set; } = DashboardQueryStatus.Success;
    public List<T> Rows { get; set; } = [];
    // Human-readable detail when Status != Success. Never includes raw exception stack traces.
    public string? ErrorMessage { get; set; }
}

// Stage 7 (corrective pass): single-item counterpart to DashboardQueryResult<T>, for endpoints
// that return one object (or none) rather than a list — e.g. a session timeline. Status ==
// Success with Value == null means the query ran and genuinely found nothing (e.g. no such
// session); any other Status means the query itself did not complete, and the caller must
// render a distinct diagnostic state rather than "no data".
public class DashboardSingleQueryResult<T>
{
    public DashboardQueryStatus Status { get; set; } = DashboardQueryStatus.Success;
    public T? Value { get; set; }
    public string? ErrorMessage { get; set; }
}

// Stage 7 (corrective pass): one (Actual, Predicted) cell of a behavior-state confusion matrix
// for a given (ModelVersion, Horizon), scoped by the same optional simId/scenarioId/modelVersion/
// horizon filters as GetBehaviorStateModelComparisonAsync. All 25 (5x5 canonical class) cells
// are always emitted for every (ModelVersion, Horizon) pair that has at least one evaluated row,
// even a cell with zero count — mirroring the existing "never let a class silently vanish" rule
// used for per-class precision/recall/F1.
public class BehaviorStateConfusionMatrixCell
{
    public string ModelVersion { get; set; } = "";
    public int Horizon { get; set; }
    public string ActualClass { get; set; } = "";
    public string PredictedClass { get; set; } = "";
    public int Count { get; set; }
}
