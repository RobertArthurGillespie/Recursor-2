using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public interface ITemporalElevatedRiskModelTrainingService
{
    // Queries ADX for the same joined temporal training rows used by the 3-class model,
    // converts labels to low / elevated, trains one SdcaMaximumEntropy binary model per
    // horizon (1–3), saves models to versioned paths, and returns a full training report.
    // modelVersion overrides the configured/default version for this run.
    Task<TemporalElevatedRiskTrainingReport> TrainAsync(string? modelVersion = null);
}

public class TemporalElevatedRiskModelTrainingService : ITemporalElevatedRiskModelTrainingService
{
    // Stage 4: immutable-version family name — versions/{ModelFamily}/{version}/h{n}.zip + manifest.json.
    public const string ModelFamily = "temporal-elevated-risk";
    private const int SplitSeed = 42;

    private readonly IAdxTemporalTrainingQueryService _queryService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TemporalElevatedRiskModelTrainingService> _logger;

    public TemporalElevatedRiskModelTrainingService(
        IAdxTemporalTrainingQueryService queryService,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<TemporalElevatedRiskModelTrainingService> logger)
    {
        _queryService = queryService;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public async Task<TemporalElevatedRiskTrainingReport> TrainAsync(string? modelVersion = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(modelVersion)
            ? (_configuration["Recursor:Models:TemporalElevatedRiskModelVersion"]
               ?? TemporalElevatedRiskPredictionService.ModelVersion)
            : modelVersion;
        var sanitizedVersion = SanitizeModelVersion(resolvedVersion);

        var report = new TemporalElevatedRiskTrainingReport
        {
            ModelVersion    = resolvedVersion,
            GeneratedAtUtc  = DateTime.UtcNow,
            Warning         = BuildTrainingWarning(resolvedVersion),
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
            _logger.LogWarning("[TemporalElevatedRiskModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        using var session = ModelVersionPublisher.TryBeginPublish(_env.ContentRootPath, ModelFamily, sanitizedVersion);
        if (session is null)
        {
            report.PublishError =
                $"Another training request for model version '{resolvedVersion}' (family '{ModelFamily}') " +
                "is already in progress.";
            _logger.LogWarning("[TemporalElevatedRiskModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        var allRows = await _queryService.QueryTrainingRowsAsync();
        report.TotalRowsQueried = allRows.Count;

        _logger.LogInformation(
            "[TemporalElevatedRiskModelTrainingService] {Total} rows queried from ADX. Version={Version}.",
            allRows.Count, resolvedVersion);

        for (int horizon = 1; horizon <= 3; horizon++)
            report.Horizons.Add(TrainHorizon(horizon, allRows, resolvedVersion, session));

        // All-or-nothing publish: if any horizon failed, nothing for this version is published.
        if (report.Horizons.Count == 3 && report.Horizons.All(h => h.Error is null))
        {
            var manifest = BuildManifest(sanitizedVersion, resolvedVersion, report.Horizons);
            report.PublishedVersionDirectory = session.Complete(manifest);
            report.Published = true;
            _logger.LogInformation(
                "[TemporalElevatedRiskModelTrainingService] Published version '{Version}' to '{Dir}'.",
                resolvedVersion, report.PublishedVersionDirectory);
        }
        else
        {
            var failedCount = report.Horizons.Count(h => h.Error is not null);
            report.Published = false;
            report.PublishError =
                $"Publish skipped: {failedCount} of {report.Horizons.Count} horizon(s) failed to train, save, " +
                "or load-verify. No artifacts were published for this version (all-or-nothing).";
            _logger.LogWarning("[TemporalElevatedRiskModelTrainingService] {Error}", report.PublishError);
        }

        return report;
    }

    private static ModelVersionManifest BuildManifest(
        string sanitizedVersion, string resolvedVersion, List<TemporalElevatedHorizonTrainingSummary> horizons)
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
                // Binary trainer reports accuracy/precision/recall/F1 for the "elevated" class
                // only (see TemporalElevatedHorizonTrainingSummary) — it does not compute a
                // macro F1 across a canonical class set the way the Phase 10E trainer does.
                ValidationMicroAccuracy = h.Accuracy ?? 0,
                ValidationMacroF1 = 0,
            });
        }

        return manifest;
    }

    // ── Per-horizon training ──────────────────────────────────────────────────

    private TemporalElevatedHorizonTrainingSummary TrainHorizon(
        int horizon, List<TemporalRiskTrainingRow> allRows, string modelVersion, ModelVersionPublishSession session)
    {
        var summary = new TemporalElevatedHorizonTrainingSummary { Horizon = horizon };

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

        // Report original 3-class label distribution before collapsing.
        summary.LabelDistribution = rows
            .GroupBy(r => r.TargetNearTermRisk)
            .ToDictionary(g => g.Key, g => g.Count());

        var stagingPath = session.GetHorizonStagingPath(horizon);
        summary.ModelPath = Path.Combine(session.FinalDirectory, $"h{horizon}.zip");

        if (rows.Count < 10)
        {
            summary.Error =
                $"Insufficient rows for horizon {horizon}: {rows.Count} (minimum 10 required).";
            _logger.LogWarning("[ElevatedH{H}] {Error}", horizon, summary.Error);
            return summary;
        }

        // Collapse labels: "low" → "low", "medium" | "high" → "elevated".
        var mlInputs = rows.Select(r => new TemporalRiskModelInput
        {
            Features = r.EmbeddingValues,
            Label    = CollapseLabel(r.TargetNearTermRisk),
        }).ToList();

        var distinctLabels = mlInputs.Select(r => r.Label).Distinct().ToList();
        if (distinctLabels.Count < 2)
        {
            summary.Error =
                $"Only one binary label class present for horizon {horizon}: '{distinctLabels.FirstOrDefault()}'. " +
                "At least 2 classes required.";
            _logger.LogWarning("[ElevatedH{H}] {Error}", horizon, summary.Error);
            return summary;
        }

        try
        {
            var mlContext = new MLContext(seed: 42);
            var data  = mlContext.Data.LoadFromEnumerable(mlInputs);
            var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

            var trainingPipeline =
                mlContext.Transforms.Conversion.MapValueToKey(
                    outputColumnName: "LabelKey",
                    inputColumnName:  "Label")
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName:   "LabelKey",
                    featureColumnName: "Features"));

            _logger.LogInformation(
                "[ElevatedH{H}] Training SdcaMaximumEntropy on {N} rows (binary: low / elevated).",
                horizon, mlInputs.Count);

            var trainedModel = trainingPipeline.Fit(split.TrainSet);

            // Append MapKeyToValue to get string PredictedLabel for evaluation and persistence.
            var evalPredictions = trainedModel.Transform(split.TestSet);
            var keyToValueTransformer = mlContext.Transforms.Conversion
                .MapKeyToValue(outputColumnName: "PredictedLabel", inputColumnName: "PredictedLabel")
                .Fit(evalPredictions);
            var predictionPipeline = trainedModel.Append(keyToValueTransformer);

            // Re-run predictions through the full pipeline (with string labels) for manual metrics.
            var evalWithStrings = predictionPipeline.Transform(split.TestSet);
            ComputeBinaryMetrics(mlContext, evalWithStrings, summary, horizon);

            mlContext.Model.Save(predictionPipeline, split.TrainSet.Schema, stagingPath);

            if (!ModelVersionPublishSession.TryVerifyLoadable(mlContext, stagingPath, out var loadError))
            {
                summary.Error = $"Model saved but failed load verification: {loadError}";
                _logger.LogError("[ElevatedH{H}] {Error}", horizon, summary.Error);
                return summary;
            }

            _logger.LogInformation(
                "[ElevatedH{H}] Model staged and load-verified at '{Path}' (final: '{Final}').",
                horizon, stagingPath, summary.ModelPath);
        }
        catch (Exception ex)
        {
            summary.Error = ex.Message;
            _logger.LogError(ex, "[ElevatedH{H}] Training failed.", horizon);
        }

        return summary;
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    private void ComputeBinaryMetrics(
        MLContext mlContext,
        Microsoft.ML.IDataView evalWithStrings,
        TemporalElevatedHorizonTrainingSummary summary,
        int horizon)
    {
        try
        {
            var evalRows = mlContext.Data
                .CreateEnumerable<ElevatedRiskEvalRow>(evalWithStrings, reuseRowObject: false)
                .ToList();

            if (evalRows.Count == 0)
            {
                summary.Error = $"No test rows for horizon {horizon} after split.";
                return;
            }

            int tp = 0, fp = 0, fn = 0, tn = 0;
            foreach (var row in evalRows)
            {
                bool actualElevated    = row.Label == "elevated";
                bool predictedElevated = row.PredictedLabel == "elevated";

                if (actualElevated && predictedElevated)      tp++;
                else if (!actualElevated && predictedElevated) fp++;
                else if (actualElevated && !predictedElevated) fn++;
                else                                           tn++;
            }

            summary.Accuracy          = evalRows.Count > 0 ? (double)(tp + tn) / evalRows.Count : 0;
            summary.ElevatedPrecision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
            summary.ElevatedRecall    = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
            summary.ElevatedF1        = summary.ElevatedPrecision + summary.ElevatedRecall > 0
                ? 2 * summary.ElevatedPrecision.Value * summary.ElevatedRecall.Value
                    / (summary.ElevatedPrecision.Value + summary.ElevatedRecall.Value)
                : 0;
            summary.FalsePositives = fp;
            summary.FalseNegatives = fn;

            _logger.LogInformation(
                "[ElevatedH{H}] Accuracy={Acc:F4} ElevatedPrecision={P:F4} ElevatedRecall={R:F4} " +
                "ElevatedF1={F1:F4} FP={FP} FN={FN}.",
                horizon,
                summary.Accuracy,
                summary.ElevatedPrecision,
                summary.ElevatedRecall,
                summary.ElevatedF1,
                fp, fn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ElevatedH{H}] Metrics computation failed.", horizon);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // "low" passes through; "medium" and "high" become "elevated".
    public static string CollapseLabel(string nearTermRisk) =>
        nearTermRisk == "low" ? "low" : "elevated";

    // Strips any character that is not a letter, digit, dash, underscore, or dot.
    public static string SanitizeModelVersion(string version) =>
        new string(version.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    // Extracts a short path suffix from a version string.
    // "temporal-elevated-risk-v2" → "v2"; falls back to the sanitized full string.
    public static string ExtractVersionSuffix(string modelVersion)
    {
        var sanitized = SanitizeModelVersion(modelVersion);
        if (string.IsNullOrEmpty(sanitized)) return "v1";

        var lastDash = sanitized.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < sanitized.Length - 1)
        {
            var candidate = sanitized[(lastDash + 1)..];
            if (candidate.Length > 0)
                return candidate;
        }
        return sanitized;
    }

    // Resolves the .zip path for a horizon + version.
    //
    // Stage 3 (corrective pass): this now matches the layout ModelVersionPublisher actually
    // writes to. Previously this auto-generated a flat filename
    // (Recursor/TrainingModels/temporal_elevated_risk_h{n}_{suffix}.zip) that the publisher
    // never wrote to, so a freshly trained/published version was unreachable by the runtime
    // resolver. An explicit config path, if set, is now always honored verbatim (no basename
    // matching) — silently discarding a configured override was itself a bug; version
    // consistency for an explicit override is checked separately via
    // ModelVersionManifestValidator.ValidateExplicitOverridePath, not by inspecting the
    // filename.
    // Public static so Program.cs can use the same logic as the trainer at startup.
    public static string ResolveVersionedModelPath(
        int horizon, string modelVersion, string contentRootPath, string? explicitConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            return Path.IsPathRooted(explicitConfigPath)
                ? explicitConfigPath
                : Path.Combine(contentRootPath, explicitConfigPath);
        }

        return Path.Combine(
            ModelVersionPublisher.GetVersionDirectory(contentRootPath, ModelFamily, modelVersion),
            $"h{horizon}.zip");
    }

    private static string BuildTrainingWarning(string modelVersion) =>
        "Shadow-only binary elevated-risk model (labels: low / elevated). " +
        "Predictions are logged and persisted to TemporalElevatedRiskPredictions for observability only. " +
        "This model does not affect adaptation decisions, policy suppression, or any live pipeline behavior. " +
        $"Restart/redeploy after training so TemporalElevatedRiskPredictionService loads model version {modelVersion}. " +
        "On Azure App Service, relative paths may fail if the app runs from a read-only package — " +
        "train locally and deploy the model zips, or configure an absolute writable path.";
}
