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
    // modelVersion overrides the configured/default version for this run (Stage 4: immutable
    // versioning — introduced to bring this trainer in line with the elevated-risk and Phase
    // 10E trainers, which already supported per-run version labels).
    Task<TemporalRiskTrainingReport> TrainAsync(string? modelVersion = null);
}

public class TemporalRiskModelTrainingService : ITemporalRiskModelTrainingService
{
    // Stage 4: immutable-version family name — versions/{ModelFamily}/{version}/h{n}.zip + manifest.json.
    public const string ModelFamily = "temporal-risk";
    private const int SplitSeed = 42;

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

    public async Task<TemporalRiskTrainingReport> TrainAsync(string? modelVersion = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(modelVersion)
            ? (_configuration["Recursor:Models:TemporalRiskModelVersion"] ?? TemporalRiskPredictionService.ModelVersion)
            : modelVersion;
        var sanitizedVersion = SanitizeModelVersion(resolvedVersion);

        var report = new TemporalRiskTrainingReport
        {
            ModelVersion = resolvedVersion,
            GeneratedAtUtc = DateTime.UtcNow,
        };

        if (sanitizedVersion.Length == 0)
        {
            report.PublishError = $"Invalid modelVersion '{resolvedVersion}'.";
            return report;
        }

        // Stage 4: immutable versioning. An existing published version (manifest.json already
        // present) is never retrained/overwritten — callers must choose a new version label.
        if (ModelVersionPublisher.VersionExists(_env.ContentRootPath, ModelFamily, sanitizedVersion))
        {
            report.PublishError =
                $"Model version '{resolvedVersion}' already exists for family '{ModelFamily}' and is immutable. " +
                "Choose a new version label to train again.";
            _logger.LogWarning("[TemporalRiskModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        using var session = ModelVersionPublisher.TryBeginPublish(_env.ContentRootPath, ModelFamily, sanitizedVersion);
        if (session is null)
        {
            report.PublishError =
                $"Another training request for model version '{resolvedVersion}' (family '{ModelFamily}') " +
                "is already in progress.";
            _logger.LogWarning("[TemporalRiskModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        var allRows = await _queryService.QueryTrainingRowsAsync();
        report.TotalRowsQueried = allRows.Count;

        _logger.LogInformation(
            "[TemporalRiskModelTrainingService] {Total} rows queried from ADX. Version={Version}.",
            allRows.Count, resolvedVersion);

        for (int horizon = 1; horizon <= 3; horizon++)
            report.Horizons.Add(TrainHorizon(horizon, allRows, session));

        if (report.Horizons.Count == 3 && report.Horizons.All(h => h.Error is null))
        {
            var manifest = BuildManifest(resolvedVersion, report.Horizons);
            report.PublishedVersionDirectory = session.Complete(manifest);
            report.Published = true;
            _logger.LogInformation(
                "[TemporalRiskModelTrainingService] Published version '{Version}' to '{Dir}'.",
                resolvedVersion, report.PublishedVersionDirectory);
        }
        else
        {
            var failedCount = report.Horizons.Count(h => h.Error is not null);
            report.Published = false;
            report.PublishError =
                $"Publish skipped: {failedCount} of {report.Horizons.Count} horizon(s) failed to train, save, " +
                "or load-verify. No artifacts were published for this version (all-or-nothing).";
            _logger.LogWarning("[TemporalRiskModelTrainingService] {Error}", report.PublishError);
        }

        return report;
    }

    private static ModelVersionManifest BuildManifest(string resolvedVersion, List<TemporalHorizonTrainingSummary> horizons)
    {
        var manifest = new ModelVersionManifest
        {
            ModelFamily = ModelFamily,
            ModelVersion = resolvedVersion,
            CreatedAtUtc = DateTime.UtcNow,
            CompletionStatus = "Complete",
            FeatureEmbeddingVersion = Services.TemporalEmbeddingService.EmbeddingVersion,
            SplitSeed = SplitSeed,
        };

        foreach (var h in horizons)
        {
            manifest.Horizons.Add(new ModelVersionHorizonManifest
            {
                Horizon = h.Horizon,
                FileName = $"h{h.Horizon}.zip",
                TrainingRowCount = h.RowCount,
                LabelDistribution = h.LabelDistribution,
                ValidationMicroAccuracy = h.MacroAccuracy ?? 0,
                ValidationMacroF1 = 0,
            });
        }

        return manifest;
    }

    // Strips any character that is not a letter, digit, dash, underscore, or dot.
    public static string SanitizeModelVersion(string version) =>
        new string(version.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    // ── Per-horizon training ──────────────────────────────────────────────────

    private TemporalHorizonTrainingSummary TrainHorizon(
        int horizon, List<TemporalRiskTrainingRow> allRows, ModelVersionPublishSession session)
    {
        var summary = new TemporalHorizonTrainingSummary { Horizon = horizon };

        // Filter to this horizon; this trainer validates its own TargetNearTermRisk target —
        // the shared query service no longer applies any trainer-specific label rule (Stage 5).
        var horizonRows = allRows.Where(r => r.Horizon == horizon).ToList();
        var excluded = new Dictionary<string, int>();
        var rows = new List<TemporalRiskTrainingRow>();
        foreach (var r in horizonRows)
        {
            if (r.EmbeddingValues.Length != 25)
            {
                excluded.TryGetValue("missing-or-invalid-embedding", out var c1);
                excluded["missing-or-invalid-embedding"] = c1 + 1;
                continue;
            }
            if (string.IsNullOrEmpty(r.TargetNearTermRisk))
            {
                excluded.TryGetValue("missing-risk-target-label", out var c2);
                excluded["missing-risk-target-label"] = c2 + 1;
                continue;
            }
            rows.Add(r);
        }
        summary.ExcludedRowCountsByReason = excluded;

        summary.RowCount = rows.Count;
        summary.LabelDistribution = rows
            .GroupBy(r => r.TargetNearTermRisk)
            .ToDictionary(g => g.Key, g => g.Count());

        var stagingPath = session.GetHorizonStagingPath(horizon);
        summary.ModelPath = Path.Combine(session.FinalDirectory, $"h{horizon}.zip");

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

            // Stage 2 — prediction pipeline.
            // Fit the MapKeyToValue estimator (requires training data to resolve the key
            // vocabulary) then append the resulting transformer so that
            // TemporalRiskPredictionService.Predict() receives a string PredictedLabel.
            var keyToValueTransformer = mlContext.Transforms.Conversion
                .MapKeyToValue(outputColumnName: "PredictedLabel", inputColumnName: "PredictedLabel")
                .Fit(evalPredictions);
            var predictionPipeline = trainedModel.Append(keyToValueTransformer);

            mlContext.Model.Save(predictionPipeline, split.TrainSet.Schema, stagingPath);

            if (!ModelVersionPublishSession.TryVerifyLoadable(mlContext, stagingPath, out var loadError))
            {
                summary.Error = $"Model saved but failed load verification: {loadError}";
                _logger.LogError("[Horizon {H}] {Error}", horizon, summary.Error);
                return summary;
            }

            _logger.LogInformation(
                "[Horizon {H}] Model staged and load-verified at '{Path}' (final: '{Final}').",
                horizon, stagingPath, summary.ModelPath);
        }
        catch (Exception ex)
        {
            summary.Error = ex.Message;
            _logger.LogError(ex, "[Horizon {H}] Training failed.", horizon);
        }

        return summary;
    }
}
