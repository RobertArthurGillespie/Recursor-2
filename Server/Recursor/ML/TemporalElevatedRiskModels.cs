using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

// Phase 10D-1: Binary elevated-risk predictor.
// Labels: "low" (TargetNearTermRisk == "low") or "elevated" (medium + high collapsed).
// Input reuses TemporalRiskModelInput — same 25-feature embedding vector.

// ML.NET output from the binary elevated-risk predictor.
// Score holds softmax probabilities for [elevated, low] (alphabetical key order).
public class TemporalElevatedRiskModelOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    [ColumnName("Score")]
    public float[] Score { get; set; } = [];

    public float Confidence => Score is { Length: > 0 } ? Score.Max() : 0f;
}

// Shadow prediction for a single horizon (binary: low / elevated).
public class TemporalElevatedRiskHorizonPrediction
{
    public string PredictedElevatedRisk { get; set; } = "";
    public float Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
}

// All three horizon predictions from one elevated-risk inference call.
// Any field may be null when the corresponding model is not loaded.
public class TemporalElevatedRiskPredictionResult
{
    public TemporalElevatedRiskHorizonPrediction? Horizon1 { get; set; }
    public TemporalElevatedRiskHorizonPrediction? Horizon2 { get; set; }
    public TemporalElevatedRiskHorizonPrediction? Horizon3 { get; set; }
}

// Per-horizon training metrics — binary model reports class-specific metrics for "elevated".
public class TemporalElevatedHorizonTrainingSummary
{
    public int Horizon { get; set; }
    public int RowCount { get; set; }
    public Dictionary<string, int> LabelDistribution { get; set; } = [];
    public double? Accuracy { get; set; }
    public double? ElevatedPrecision { get; set; }
    public double? ElevatedRecall { get; set; }
    public double? ElevatedF1 { get; set; }
    public int? FalsePositives { get; set; }
    public int? FalseNegatives { get; set; }
    public string? ModelPath { get; set; }
    public string? Error { get; set; }
}

// Full report returned by POST /api/recursor/train-temporal-elevated-risk.
public class TemporalElevatedRiskTrainingReport
{
    // Set by the trainer to the resolved model version used for this training run.
    public string ModelVersion { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    // Default warning for bare construction (e.g., in tests); trainer replaces this with a version-specific message.
    public string Warning { get; set; } =
        "Shadow-only binary elevated-risk model (labels: low / elevated). " +
        "Predictions are logged and persisted to TemporalElevatedRiskPredictions for observability only. " +
        "This model does not affect adaptation decisions, policy suppression, or any live pipeline behavior. " +
        "Restart or redeploy the app after training so TemporalElevatedRiskPredictionService reloads the " +
        "newly written model files (models are loaded once at startup). " +
        "On Azure App Service, relative paths may fail if the app runs from a read-only package — " +
        "train locally and deploy the model zips, or configure an absolute writable path.";
    public int TotalRowsQueried { get; set; }
    public List<TemporalElevatedHorizonTrainingSummary> Horizons { get; set; } = [];
}

// Helper used only during evaluation — maps test-set rows to actual + predicted labels.
internal class ElevatedRiskEvalRow
{
    [ColumnName("Label")]
    public string Label { get; set; } = "";

    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";
}
