using Microsoft.ML.Data;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

// Phase 10E: Multiclass behavior-state temporal predictor.
// Labels: the five canonical classes from BehaviorStateLabelPolicy
// (struggling / recovering / stable / mastering / conflicted).
// Input reuses TemporalRiskModelInput — same 25-feature embedding vector.
// Shadow-only: predictions are persisted for observability and never feed
// adaptation decisions, guardrails, or policy selection.

// ML.NET output from the multiclass behavior-state predictor.
// Score holds softmax probabilities, one per class, in the trained key-value order.
public class TemporalBehaviorStateModelOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    [ColumnName("Score")]
    public float[] Score { get; set; } = [];

    public float Confidence => Score is { Length: > 0 } ? Score.Max() : 0f;
}

// Shadow prediction for a single horizon, including per-class probabilities
// keyed by canonical label so the dashboard/evaluation layer never has to
// re-derive the label→score mapping.
public class TemporalBehaviorStateHorizonPrediction
{
    public string PredictedBehaviorState { get; set; } = "";
    public float Confidence { get; set; }
    public Dictionary<string, float> ClassProbabilities { get; set; } = [];
    public string ModelVersion { get; set; } = "";
}

// All three horizon predictions from one inference call.
// Any field may be null when the corresponding model is not loaded.
public class TemporalBehaviorStatePredictionResult
{
    public TemporalBehaviorStateHorizonPrediction? Horizon1 { get; set; }
    public TemporalBehaviorStateHorizonPrediction? Horizon2 { get; set; }
    public TemporalBehaviorStateHorizonPrediction? Horizon3 { get; set; }
}

// Per-class precision/recall/F1, used both combined and per-simulation.
public class BehaviorStateClassMetrics
{
    public string ClassLabel { get; set; } = "";
    public int SupportCount { get; set; }
    // Stage 8: how many rows this slice predicted as ClassLabel, regardless of SupportCount —
    // lets a caller see a class the model over/under-predicts even when it has zero support.
    public int PredictedCount { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1 { get; set; }
}

// Metrics computed over one evaluation slice (all sims, or a single SimId).
public class BehaviorStateMetricsSlice
{
    public string Scope { get; set; } = ""; // "all" or a SimId
    public int RowCount { get; set; }
    public double MicroAccuracy { get; set; }
    public double MacroF1 { get; set; }
    public double MajorityBaselineAccuracy { get; set; }
    public string? MajorityClass { get; set; }
    public List<BehaviorStateClassMetrics> PerClass { get; set; } = [];
    // Rows: actual class -> (predicted class -> count).
    public Dictionary<string, Dictionary<string, int>> ConfusionMatrix { get; set; } = [];
}

// Stage 6 (corrective pass): whether this split can actually support a cross-simulation
// generalization claim — distinct from whether training succeeded. A model can train
// successfully on a single simulation, or with one simulation entirely missing from one side
// of the split; neither of those states should ever be silently reported as "cross-simulation
// validated".
public enum CrossSimulationValidationStatus
{
    // Zero or one simulation is present in the trainable data — cross-simulation evaluation
    // does not apply (there is nothing to cross-validate across).
    NotApplicable,

    // More than one simulation is present in the trainable data, but at least one simulation
    // present on one side of the split (train or validation) has zero rows on the other side.
    // Combined ("all") metrics must NOT be read as evidence of cross-simulation generalization.
    InsufficientCoverage,

    // More than one simulation is present, and every simulation appearing on either side of the
    // split has at least one row on the other side too.
    Passed,
}

// Stage 9: result of the grouped stratified train/validation session split, including
// per-split label and simulation distributions and a precise failure reason when the resulting
// partition cannot support training.
public class SessionSplitResult
{
    public HashSet<string> TrainSessions { get; set; } = [];
    public HashSet<string> ValidationSessions { get; set; } = [];
    public Dictionary<string, int> TrainLabelDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationLabelDistribution { get; set; } = [];
    public Dictionary<string, int> TrainSimDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationSimDistribution { get; set; } = [];
    public string? ValidationError { get; set; }

    // Stage 6 (corrective pass): explicit coverage reporting, computed relative to the set of
    // classes/simulations actually present in the trainable data (train ∪ validation) for this
    // horizon — "absent from training" or "absent from validation" means present in the data
    // overall but with zero rows on that specific side.
    public List<string> ClassesAbsentFromTraining { get; set; } = [];
    public List<string> ClassesAbsentFromValidation { get; set; } = [];
    public List<string> SimulationsAbsentFromTraining { get; set; } = [];
    public List<string> SimulationsAbsentFromValidation { get; set; } = [];
    public CrossSimulationValidationStatus CrossSimulationStatus { get; set; }
    public string CrossSimulationStatusReason { get; set; } = "";
}

// Per-horizon training result — exclusion accounting, combined metrics, and
// a breakdown per simulation so combined-metric strength can never be used
// to imply cross-simulation generalization.
public class TemporalBehaviorStateHorizonTrainingSummary
{
    public int Horizon { get; set; }
    public int RowCountBeforeExclusion { get; set; }
    public int RowCountTrainable { get; set; }
    public Dictionary<string, int> ExcludedRowCountsByReason { get; set; } = [];
    public Dictionary<string, int> LabelDistribution { get; set; } = [];
    public int TrainSessionCount { get; set; }
    public int ValidationSessionCount { get; set; }
    // Stage 9: distributions of the grouped stratified split, reported separately for train and
    // validation so a caller can see whether a rare class or simulation ended up isolated on
    // one side (rather than only being able to infer this from the final metrics).
    public Dictionary<string, int> TrainLabelDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationLabelDistribution { get; set; } = [];
    public Dictionary<string, int> TrainSimulationDistribution { get; set; } = [];
    public Dictionary<string, int> ValidationSimulationDistribution { get; set; } = [];
    // Stage 6 (corrective pass): mirrors SessionSplitResult's coverage reporting so a caller can
    // see it directly on the training report without re-deriving it from the raw distributions.
    public List<string> ClassesAbsentFromTraining { get; set; } = [];
    public List<string> ClassesAbsentFromValidation { get; set; } = [];
    public List<string> SimulationsAbsentFromTraining { get; set; } = [];
    public List<string> SimulationsAbsentFromValidation { get; set; } = [];
    public CrossSimulationValidationStatus CrossSimulationStatus { get; set; }
    public string CrossSimulationStatusReason { get; set; } = "";
    public BehaviorStateMetricsSlice? Combined { get; set; }
    public List<BehaviorStateMetricsSlice> BySimulation { get; set; } = [];
    public string? ModelPath { get; set; }
    public string? Error { get; set; }
}

// Full report returned by POST /api/recursor/train-temporal-behavior-state.
public class TemporalBehaviorStateTrainingReport
{
    public string ModelVersion { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string Warning { get; set; } =
        "Shadow-only multiclass behavior-state model (labels: struggling / recovering / stable / " +
        "mastering / conflicted). Predictions are logged and persisted to TemporalBehaviorStatePredictions " +
        "for observability only. This model does not affect adaptation decisions, guardrails, policy " +
        "selection, hint behavior, difficulty, time pressure, or any other simulation parameter. " +
        "Restart or redeploy the app after training so TemporalBehaviorStatePredictionService reloads the " +
        "newly written model files (models are loaded once at startup). " +
        "On Azure App Service, relative paths may fail if the app runs from a read-only package — " +
        "train locally and deploy the model zips, or configure an absolute writable path. " +
        "Combined (all-simulation) metrics must not be read as proof of cross-simulation generalization — " +
        "check the per-simulation breakdown.";
    public int TotalRowsQueried { get; set; }
    public List<TemporalBehaviorStateHorizonTrainingSummary> Horizons { get; set; } = [];

    // ── Stage 4: immutable/atomic artifact publishing ───────────────────────
    // True only when all three horizons trained, saved, and load-verified successfully and the
    // version directory (with manifest.json) was atomically published. When false, no artifacts
    // for this version were published — check Horizons[].Error for per-horizon failure detail
    // and PublishError for why the run as a whole did not publish.
    public bool Published { get; set; }
    public string? PublishError { get; set; }
    public string? PublishedVersionDirectory { get; set; }
}
