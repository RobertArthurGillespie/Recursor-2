using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public interface ITemporalRiskModelTrainingService
{
    // Queries ADX, trains one SdcaMaximumEntropy multiclass model per horizon (1–3),
    // saves models to configured paths, and returns a full training report.
    Task<TemporalRiskTrainingReport> TrainAsync();
}

public class TemporalRiskModelTrainingService : ITemporalRiskModelTrainingService
{
    private readonly IAdxTemporalTrainingQueryService _queryService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TemporalRiskModelTrainingService> _logger;

    public TemporalRiskModelTrainingService(
        IAdxTemporalTrainingQueryService queryService,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<TemporalRiskModelTrainingService> logger)
    {
        _queryService = queryService;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public async Task<TemporalRiskTrainingReport> TrainAsync()
    {
        var report = new TemporalRiskTrainingReport();

        var allRows = await _queryService.QueryTrainingRowsAsync();
        report.TotalRowsQueried = allRows.Count;

        _logger.LogInformation(
            "[TemporalRiskModelTrainingService] {Total} rows queried from ADX.", allRows.Count);

        for (int horizon = 1; horizon <= 3; horizon++)
            report.Horizons.Add(TrainHorizon(horizon, allRows));

        return report;
    }

    // ── Per-horizon training ──────────────────────────────────────────────────

    private TemporalHorizonTrainingSummary TrainHorizon(int horizon, List<TemporalRiskTrainingRow> allRows)
    {
        var summary = new TemporalHorizonTrainingSummary { Horizon = horizon };

        // Filter to this horizon; skip rows with wrong-length vectors or empty labels.
        var rows = allRows
            .Where(r => r.Horizon == horizon
                && r.EmbeddingValues.Length == 25
                && !string.IsNullOrEmpty(r.TargetNearTermRisk))
            .ToList();

        summary.RowCount = rows.Count;
        summary.LabelDistribution = rows
            .GroupBy(r => r.TargetNearTermRisk)
            .ToDictionary(g => g.Key, g => g.Count());

        var modelPath = ResolveModelPath($"Recursor:Models:TemporalRiskH{horizon}ModelPath");
        summary.ModelPath = modelPath;

        if (rows.Count < 10)
        {
            summary.Error =
                $"Insufficient rows for horizon {horizon}: {rows.Count} (minimum 10 required).";
            _logger.LogWarning("[Horizon {H}] {Error}", horizon, summary.Error);
            return summary;
        }

        var distinctLabels = rows.Select(r => r.TargetNearTermRisk).Distinct().ToList();
        if (distinctLabels.Count < 2)
        {
            summary.Error =
                $"Only one label class present for horizon {horizon}: '{distinctLabels.FirstOrDefault()}'. " +
                "At least 2 classes required for multiclass training.";
            _logger.LogWarning("[Horizon {H}] {Error}", horizon, summary.Error);
            return summary;
        }

        try
        {
            var mlContext = new MLContext(seed: 42);

            var mlInputs = rows.Select(r => new TemporalRiskModelInput
            {
                Features = r.EmbeddingValues,
                Label    = r.TargetNearTermRisk,
            }).ToList();

            var data  = mlContext.Data.LoadFromEnumerable(mlInputs);
            var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

            // Stage 1 — training pipeline.
            // MapKeyToValue is intentionally omitted here so that Evaluate receives
            // key-typed PredictedLabel, which matches the key-typed LabelKey column.
            // Appending MapKeyToValue before Evaluate would cause a type mismatch and
            // produce wrong or erroring metrics in ML.NET 3.x.
            var trainingPipeline =
                mlContext.Transforms.Conversion.MapValueToKey(
                    outputColumnName: "LabelKey",
                    inputColumnName:  "Label")
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName:   "LabelKey",
                    featureColumnName: "Features"));

            _logger.LogInformation(
                "[Horizon {H}] Training SdcaMaximumEntropy on {N} rows ({Labels} classes).",
                horizon, mlInputs.Count, distinctLabels.Count);

            var trainedModel = trainingPipeline.Fit(split.TrainSet);

            // Evaluate while PredictedLabel is still key-typed — both columns are
            // key-typed, so MulticlassClassification.Evaluate works correctly.
            var evalPredictions = trainedModel.Transform(split.TestSet);
            var metrics = mlContext.MulticlassClassification.Evaluate(
                evalPredictions,
                labelColumnName:          "LabelKey",
                predictedLabelColumnName: "PredictedLabel");

            summary.MacroAccuracy = metrics.MacroAccuracy;
            summary.LogLoss       = metrics.LogLoss;

            _logger.LogInformation(
                "[Horizon {H}] MacroAccuracy={Acc:F4} LogLoss={LL:F4}.",
                horizon, metrics.MacroAccuracy, metrics.LogLoss);

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                summary.Error =
                    $"No model path configured for horizon {horizon} " +
                    $"(key: Recursor:Models:TemporalRiskH{horizon}ModelPath). Model not saved.";
                _logger.LogWarning("[Horizon {H}] {Error}", horizon, summary.Error);
            }
            else
            {
                var dir = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Stage 2 — prediction pipeline.
                // Fit the MapKeyToValue estimator (requires training data to resolve the key
                // vocabulary) then append the resulting transformer so that
                // TemporalRiskPredictionService.Predict() receives a string PredictedLabel.
                var keyToValueTransformer = mlContext.Transforms.Conversion
                    .MapKeyToValue(outputColumnName: "PredictedLabel", inputColumnName: "PredictedLabel")
                    .Fit(evalPredictions);
                var predictionPipeline = trainedModel.Append(keyToValueTransformer);

                mlContext.Model.Save(predictionPipeline, split.TrainSet.Schema, modelPath);
                _logger.LogInformation("[Horizon {H}] Model saved to '{Path}'.", horizon, modelPath);
            }
        }
        catch (Exception ex)
        {
            summary.Error = ex.Message;
            _logger.LogError(ex, "[Horizon {H}] Training failed.", horizon);
        }

        return summary;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? ResolveModelPath(string configKey)
    {
        var configured = _configuration[configKey];
        if (string.IsNullOrWhiteSpace(configured)) return null;
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }
}
