using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// ML.NET binary classification output.
/// Maps the standard output columns produced by binary classification trainers.
/// </summary>
public class BinaryPredictionOutput
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    /// <summary>Raw logit score before calibration.</summary>
    [ColumnName("Score")]
    public float Score { get; set; }

    /// <summary>Calibrated probability that the label is true (0.0–1.0).</summary>
    [ColumnName("Probability")]
    public float Probability { get; set; }
}
