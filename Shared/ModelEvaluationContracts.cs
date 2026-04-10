namespace NCATAIBlazorFrontendTest.Shared;

// ── Shadow ML Evaluation Contracts ───────────────────────────────────────────
// These types are shared between the Server API and any future client consumers.
// They represent the observational evaluation layer for shadow ML models.
// They do not affect the live adaptation pipeline.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The supported shadow model identifiers.
/// Use the string constants — not raw strings — to avoid typos across the codebase.
/// </summary>
public static class ShadowModelType
{
    public const string Confusion = "confusion";
    public const string HintDependence = "hint_dependence";
    public const string StableMastery = "stable_mastery";

    public static bool IsValid(string? value) =>
        value is Confusion or HintDependence or StableMastery;
}

/// <summary>
/// Request body for POST /api/recursor/evaluation/shadow.
/// </summary>
public class ModelEvaluationRequest
{
    /// <summary>One of the ShadowModelType constants.</summary>
    public string Model { get; set; } = "";

    /// <summary>Optional filter to a specific sim. Null means all sims.</summary>
    public string? SimId { get; set; }

    /// <summary>Optional start of the evaluation window (inclusive, UTC).</summary>
    public DateTime? StartUtc { get; set; }

    /// <summary>Optional end of the evaluation window (inclusive, UTC).</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>
    /// Probability threshold used to compute confusion-matrix cells.
    /// Predictions >= threshold are treated as positive.
    /// Defaults to 0.6.
    /// </summary>
    public double Threshold { get; set; } = 0.6;

    /// <summary>
    /// Maximum number of disagreement rows to return.
    /// Defaults to 25. Clamped server-side to 100.
    /// </summary>
    public int DisagreementLimit { get; set; } = 25;
}

/// <summary>
/// Aggregate evaluation statistics for a single shadow model.
/// </summary>
public class ModelEvaluationSummary
{
    public string Model { get; set; } = "";
    public string? SimIdFilter { get; set; }
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public double Threshold { get; set; }

    // Row counts
    public long TotalRowCount { get; set; }
    public long PositiveLabelCount { get; set; }
    public long NegativeLabelCount { get; set; }

    // Prediction distribution by label
    public double AvgPredWhenPositive { get; set; }
    public double AvgPredWhenNegative { get; set; }

    // Overall prediction distribution
    public double MinPred { get; set; }
    public double MaxPred { get; set; }
    public double P50 { get; set; }
    public double P75 { get; set; }
    public double P90 { get; set; }

    // Confusion-matrix cells (at the configured threshold)
    public long TP { get; set; }
    public long FP { get; set; }
    public long TN { get; set; }
    public long FN { get; set; }

    // Derived metrics (NaN-safe: 0.0 when denominator is zero)
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double Specificity { get; set; }
    public double F1 { get; set; }

    // Model metadata seen across evaluated rows
    public List<string> ModelVersionsSeen { get; set; } = [];
    public List<string> InferenceModesSeen { get; set; } = [];
}

/// <summary>
/// A single row where the model's prediction contradicts the rule-derived label.
/// Includes contextual features to help diagnose why the model is wrong.
/// </summary>
public class ModelDisagreementRow
{
    public string SessionId { get; set; } = "";
    public string SimId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public int WindowIndex { get; set; }
    public string TaskType { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The rule-derived weak label (0 or 1).</summary>
    public int LabelValue { get; set; }

    /// <summary>The model's predicted probability for this model type.</summary>
    public double PredictedProbability { get; set; }

    // Adaptive state at the time of the window
    public string CurrentHintMode { get; set; } = "";
    public double CurrentDifficulty { get; set; }
    public double CurrentTimePressure { get; set; }
    public double CurrentErrorTolerance { get; set; }

    // Key feature scores (help explain what the model saw)
    public double ConfusionScore { get; set; }
    public double HintDependenceScore { get; set; }
    public double GoalTrend { get; set; }
    public double AttentionTrend { get; set; }
    public double ConfusionTrend { get; set; }
    public double HintDependenceTrend { get; set; }

    /// <summary>
    /// Short classification of the disagreement type:
    ///   false_positive_like  — label=0 but pred >= threshold
    ///   false_negative_like  — label=1 but pred below threshold
    ///   low_prob_labeled     — label=1 but pred very low (below half the threshold)
    ///   high_prob_unlabeled  — label outside {0,1} but pred >= threshold
    /// </summary>
    public string DisagreementClass { get; set; } = "";
}

/// <summary>
/// Full evaluation result returned by POST /api/recursor/evaluation/shadow.
/// </summary>
public class ModelEvaluationResult
{
    public ModelEvaluationSummary Summary { get; set; } = new();
    public List<ModelDisagreementRow> Disagreements { get; set; } = [];
}
