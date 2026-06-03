using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

// One joined row returned by IAdxTemporalTrainingQueryService.
// Holds the source window's embedding and the ground-truth label from the target window.
public class TemporalRiskTrainingRow
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int SourceWindowIndex { get; set; }
    public int TargetWindowIndex { get; set; }
    public int Horizon { get; set; }
    public float[] EmbeddingValues { get; set; } = [];  // 25 features converted from double[]
    public string TargetNearTermRisk { get; set; } = "";
    public string TargetBehaviorState { get; set; } = "";
    public double TargetConfusionScore { get; set; }
    public double TargetGoalUnderstanding { get; set; }
    public double TargetHintDependenceScore { get; set; }
}

// ML.NET input for temporal risk multiclass classification.
// Features is a fixed-length float vector matching TemporalEmbeddingService.FeatureNames (25 entries).
public class TemporalRiskModelInput
{
    [VectorType(25)]
    [ColumnName("Features")]
    public float[] Features { get; set; } = new float[25];

    [ColumnName("Label")]
    public string Label { get; set; } = "";
}

// ML.NET output from the multiclass predictor.
// Score holds softmax probabilities, one per class (low/medium/high).
public class TemporalRiskModelOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    [ColumnName("Score")]
    public float[] Score { get; set; } = [];

    // Maximum softmax score — a proxy for model confidence.
    public float Confidence => Score is { Length: > 0 } ? Score.Max() : 0f;
}

// Shadow prediction for a single horizon.
public class TemporalRiskHorizonPrediction
{
    public string PredictedNearTermRisk { get; set; } = "";
    public float Confidence { get; set; }
    public string ModelVersion { get; set; } = "";
}

// All three horizon predictions from one inference call.
// Any field may be null when the corresponding model is not loaded.
public class TemporalRiskPredictionResult
{
    public TemporalRiskHorizonPrediction? Horizon1 { get; set; }
    public TemporalRiskHorizonPrediction? Horizon2 { get; set; }
    public TemporalRiskHorizonPrediction? Horizon3 { get; set; }
}

// Per-horizon training metrics — included in the training endpoint response.
public class TemporalHorizonTrainingSummary
{
    public int Horizon { get; set; }
    public int RowCount { get; set; }
    public Dictionary<string, int> LabelDistribution { get; set; } = [];
    public double? MacroAccuracy { get; set; }
    public double? LogLoss { get; set; }
    public string? ModelPath { get; set; }
    public string? Error { get; set; }
}

// Full report returned by POST /api/recursor/train-temporal-risk.
public class TemporalRiskTrainingReport
{
    public string Warning { get; set; } =
        "Shadow-only baseline model. Predictions are logged/persisted for observability only. " +
        "This model does not affect adaptation decisions. " +
        "Restart or redeploy the app after training so TemporalRiskPredictionService reloads the " +
        "newly written model files (models are loaded once at startup). " +
        "On Azure App Service, relative model paths may fail if the app runs from a read-only " +
        "package — train locally and deploy the model zips, or configure an absolute writable path.";
    public int TotalRowsQueried { get; set; }
    public List<TemporalHorizonTrainingSummary> Horizons { get; set; } = [];
}
