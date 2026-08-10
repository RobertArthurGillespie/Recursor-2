using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public interface ITemporalBehaviorStateModelTrainingService
{
    // Queries ADX for the same joined temporal training rows used by the other temporal
    // models, normalizes/excludes labels via BehaviorStateLabelPolicy, trains one
    // SdcaMaximumEntropy multiclass model per horizon (1-3) using a session-level
    // train/validation split, saves models to versioned paths, and returns a full report
    // with combined and per-simulation metrics. modelVersion overrides the
    // configured/default version for this run.
    Task<TemporalBehaviorStateTrainingReport> TrainAsync(string? modelVersion = null);
}

public class TemporalBehaviorStateModelTrainingService : ITemporalBehaviorStateModelTrainingService
{
    // Minimum rows required (after exclusion) before attempting to train a horizon.
    private const int MinTrainableRows = 10;

    // Stage 4: immutable-version family name — versions/{ModelFamily}/{version}/h{n}.zip + manifest.json.
    public const string ModelFamily = "temporal-behavior-state";

    // Stage 9 will make this configurable/stratified; recorded in the manifest either way so
    // every published version's split is reproducible from its manifest alone.
    private const int SplitSeed = 42;

    private readonly IAdxTemporalTrainingQueryService _queryService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TemporalBehaviorStateModelTrainingService> _logger;

    public TemporalBehaviorStateModelTrainingService(
        IAdxTemporalTrainingQueryService queryService,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<TemporalBehaviorStateModelTrainingService> logger)
    {
        _queryService = queryService;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public async Task<TemporalBehaviorStateTrainingReport> TrainAsync(string? modelVersion = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(modelVersion)
            ? (_configuration["Recursor:Models:TemporalBehaviorStateModelVersion"]
               ?? TemporalBehaviorStatePredictionService.ModelVersion)
            : modelVersion;
        var sanitizedVersion = SanitizeModelVersion(resolvedVersion);

        var report = new TemporalBehaviorStateTrainingReport
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
            _logger.LogWarning("[TemporalBehaviorStateModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        using var session = ModelVersionPublisher.TryBeginPublish(_env.ContentRootPath, ModelFamily, sanitizedVersion);
        if (session is null)
        {
            report.PublishError =
                $"Another training request for model version '{resolvedVersion}' (family '{ModelFamily}') " +
                "is already in progress.";
            _logger.LogWarning("[TemporalBehaviorStateModelTrainingService] {Error}", report.PublishError);
            return report;
        }

        var allRows = await _queryService.QueryTrainingRowsAsync();
        report.TotalRowsQueried = allRows.Count;

        _logger.LogInformation(
            "[TemporalBehaviorStateModelTrainingService] {Total} rows queried from ADX. Version={Version}.",
            allRows.Count, resolvedVersion);

        for (int horizon = 1; horizon <= 3; horizon++)
            report.Horizons.Add(TrainHorizon(horizon, allRows, resolvedVersion, session));

        // Stage 6 (corrective pass): coverage gaps never block publish (a model can still train
        // and be published with InsufficientCoverage or a class missing from validation) — but
        // they must never be silently absent from the top-level report either. This is appended
        // regardless of whether publish ultimately succeeds below.
        var coverageIssues = new List<string>();
        foreach (var h in report.Horizons)
        {
            if (h.CrossSimulationStatus == CrossSimulationValidationStatus.InsufficientCoverage)
                coverageIssues.Add($"H{h.Horizon}: {h.CrossSimulationStatusReason}");
            if (h.ClassesAbsentFromValidation.Count > 0)
                coverageIssues.Add(
                    $"H{h.Horizon}: class(es) absent from validation ({string.Join(", ", h.ClassesAbsentFromValidation)}) " +
                    "— per-class metrics for these are not validated this version.");
        }
        if (coverageIssues.Count > 0)
        {
            report.Warning +=
                " COVERAGE GAPS DETECTED — do not describe this version as fully cross-simulation or " +
                "per-class validated: " + string.Join(" | ", coverageIssues);
        }

        // All-or-nothing publish: if any horizon failed to train, save, or load-verify, nothing
        // for this version is published — a version is never left partially activated.
        if (report.Horizons.Count == 3 && report.Horizons.All(h => h.Error is null))
        {
            var manifest = BuildManifest(sanitizedVersion, resolvedVersion, report.Horizons);
            report.PublishedVersionDirectory = session.Complete(manifest);
            report.Published = true;
            _logger.LogInformation(
                "[TemporalBehaviorStateModelTrainingService] Published version '{Version}' to '{Dir}'.",
                resolvedVersion, report.PublishedVersionDirectory);
        }
        else
        {
            var failedCount = report.Horizons.Count(h => h.Error is not null);
            report.Published = false;
            report.PublishError =
                $"Publish skipped: {failedCount} of {report.Horizons.Count} horizon(s) failed to train, save, " +
                "or load-verify. No artifacts were published for this version (all-or-nothing).";
            _logger.LogWarning("[TemporalBehaviorStateModelTrainingService] {Error}", report.PublishError);
        }

        return report;
    }

    private static ModelVersionManifest BuildManifest(
        string sanitizedVersion, string resolvedVersion, List<TemporalBehaviorStateHorizonTrainingSummary> horizons)
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
                TrainingRowCount = h.RowCountTrainable,
                ValidationRowCount = h.Combined?.RowCount ?? 0,
                LabelDistribution = h.LabelDistribution,
                SimulationDistribution = h.BySimulation.ToDictionary(s => s.Scope, s => s.RowCount),
                ValidationMicroAccuracy = h.Combined?.MicroAccuracy ?? 0,
                ValidationMacroF1 = h.Combined?.MacroF1 ?? 0,
                TrainLabelDistribution = h.TrainLabelDistribution,
                ValidationLabelDistribution = h.ValidationLabelDistribution,
                TrainSimulationDistribution = h.TrainSimulationDistribution,
                ValidationSimulationDistribution = h.ValidationSimulationDistribution,
                TrainSessionCount = h.TrainSessionCount,
                ValidationSessionCount = h.ValidationSessionCount,
                ClassesAbsentFromTraining = h.ClassesAbsentFromTraining,
                ClassesAbsentFromValidation = h.ClassesAbsentFromValidation,
                SimulationsAbsentFromTraining = h.SimulationsAbsentFromTraining,
                SimulationsAbsentFromValidation = h.SimulationsAbsentFromValidation,
                CrossSimulationValidationStatus = h.CrossSimulationStatus.ToString(),
                CrossSimulationValidationReason = h.CrossSimulationStatusReason,
            });
        }

        return manifest;
    }

    // ── Per-horizon training ──────────────────────────────────────────────────

    private TemporalBehaviorStateHorizonTrainingSummary TrainHorizon(
        int horizon, List<TemporalRiskTrainingRow> allRows, string modelVersion, ModelVersionPublishSession session)
    {
        var summary = new TemporalBehaviorStateHorizonTrainingSummary { Horizon = horizon };

        var horizonRows = allRows.Where(r => r.Horizon == horizon).ToList();
        summary.RowCountBeforeExclusion = horizonRows.Count;

        var excluded = new Dictionary<string, int>();
        var trainable = new List<(TemporalRiskTrainingRow Row, string Label)>();

        foreach (var row in horizonRows)
        {
            var classification = ClassifyRow(row);
            if (!classification.Trainable)
            {
                excluded.TryGetValue(classification.ExclusionReason!, out var count);
                excluded[classification.ExclusionReason!] = count + 1;
                continue;
            }

            trainable.Add((row, classification.CanonicalLabel!));
        }

        summary.RowCountTrainable = trainable.Count;
        summary.ExcludedRowCountsByReason = excluded;
        summary.LabelDistribution = trainable
            .GroupBy(t => t.Label)
            .ToDictionary(g => g.Key, g => g.Count());

        var stagingPath = session.GetHorizonStagingPath(horizon);
        // Reported path is the version's eventual FINAL location, not the ephemeral staging
        // path — this never overlaps with the legacy flat path the running app has loaded.
        summary.ModelPath = Path.Combine(session.FinalDirectory, $"h{horizon}.zip");

        if (trainable.Count < MinTrainableRows)
        {
            summary.Error =
                $"Insufficient trainable rows for horizon {horizon}: {trainable.Count} " +
                $"(minimum {MinTrainableRows} required after label-policy exclusion).";
            _logger.LogWarning("[BehaviorStateH{H}] {Error}", horizon, summary.Error);
            return summary;
        }

        var distinctLabels = trainable.Select(t => t.Label).Distinct().ToList();
        if (distinctLabels.Count < 2)
        {
            summary.Error =
                $"Only one label class present for horizon {horizon}: '{distinctLabels.FirstOrDefault()}'. " +
                "At least 2 classes required for multiclass training.";
            _logger.LogWarning("[BehaviorStateH{H}] {Error}", horizon, summary.Error);
            return summary;
        }

        // Stage 9: grouped stratified session-level split — no session's windows appear in both
        // train and validation, AND sessions are stratified by (dominant label, SimId) so a
        // rare class or an under-represented simulation is not accidentally isolated entirely
        // on one side. See SplitSessionsForTraining for the algorithm.
        var split = SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: SplitSeed);

        summary.TrainSessionCount = split.TrainSessions.Count;
        summary.ValidationSessionCount = split.ValidationSessions.Count;
        summary.TrainLabelDistribution = split.TrainLabelDistribution;
        summary.ValidationLabelDistribution = split.ValidationLabelDistribution;
        summary.TrainSimulationDistribution = split.TrainSimDistribution;
        summary.ValidationSimulationDistribution = split.ValidationSimDistribution;
        summary.ClassesAbsentFromTraining = split.ClassesAbsentFromTraining;
        summary.ClassesAbsentFromValidation = split.ClassesAbsentFromValidation;
        summary.SimulationsAbsentFromTraining = split.SimulationsAbsentFromTraining;
        summary.SimulationsAbsentFromValidation = split.SimulationsAbsentFromValidation;
        summary.CrossSimulationStatus = split.CrossSimulationStatus;
        summary.CrossSimulationStatusReason = split.CrossSimulationStatusReason;

        if (split.CrossSimulationStatus == CrossSimulationValidationStatus.InsufficientCoverage)
        {
            _logger.LogWarning(
                "[BehaviorStateH{H}] Cross-simulation validation coverage INSUFFICIENT: {Reason}",
                horizon, split.CrossSimulationStatusReason);
        }
        if (split.ClassesAbsentFromValidation.Count > 0)
        {
            _logger.LogWarning(
                "[BehaviorStateH{H}] Class(es) present in training data but absent from validation " +
                "(per-class metrics for these are not validated): {Classes}",
                horizon, string.Join(", ", split.ClassesAbsentFromValidation));
        }

        if (split.ValidationError is not null)
        {
            summary.Error = $"Split validation failed for horizon {horizon}: {split.ValidationError}";
            _logger.LogWarning("[BehaviorStateH{H}] {Error}", horizon, summary.Error);
            return summary;
        }

        var trainRows = trainable.Where(t => split.TrainSessions.Contains(t.Row.SessionId)).ToList();
        var validationRows = trainable.Where(t => split.ValidationSessions.Contains(t.Row.SessionId)).ToList();

        try
        {
            var mlContext = new MLContext(seed: 42);

            var trainInputs = trainRows
                .Select(t => new TemporalRiskModelInput { Features = t.Row.EmbeddingValues, Label = t.Label })
                .ToList();
            var trainData = mlContext.Data.LoadFromEnumerable(trainInputs);

            var trainingPipeline =
                mlContext.Transforms.Conversion.MapValueToKey(
                    outputColumnName: "LabelKey",
                    inputColumnName:  "Label")
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName:   "LabelKey",
                    featureColumnName: "Features"));

            _logger.LogInformation(
                "[BehaviorStateH{H}] Training SdcaMaximumEntropy on {N} rows from {S} session(s) " +
                "({Labels} classes).",
                horizon, trainInputs.Count, split.TrainSessions.Count, distinctLabels.Count);

            var trainedModel = trainingPipeline.Fit(trainData);

            var keyToValueTransformer = mlContext.Transforms.Conversion
                .MapKeyToValue(outputColumnName: "PredictedLabel", inputColumnName: "PredictedLabel")
                .Fit(trainedModel.Transform(trainData));
            var predictionPipeline = trainedModel.Append(keyToValueTransformer);

            // Predict row-by-row (not a batched IDataView.Transform) so each prediction can be
            // paired explicitly with its row's SimId for the per-simulation metrics breakdown —
            // batched transforms don't carry SimId through the ML.NET feature/label columns.
            var engine = mlContext.Model
                .CreatePredictionEngine<TemporalRiskModelInput, TemporalBehaviorStateModelOutput>(predictionPipeline);

            var evalResults = validationRows
                .Select(t => (
                    Actual: t.Label,
                    Predicted: engine.Predict(new TemporalRiskModelInput { Features = t.Row.EmbeddingValues }).PredictedLabel,
                    SimId: t.Row.SimId))
                .ToList();

            var trainLabelsForMajority = trainRows.Select(t => t.Label).ToList();

            summary.Combined = ComputeMetricsSlice(
                "all",
                evalResults.Select(e => (e.Actual, e.Predicted)).ToList(),
                trainLabelsForMajority);

            summary.BySimulation = evalResults
                .GroupBy(e => e.SimId)
                .Select(g => ComputeMetricsSlice(
                    g.Key,
                    g.Select(e => (e.Actual, e.Predicted)).ToList(),
                    trainRows.Where(t => t.Row.SimId == g.Key).Select(t => t.Label)))
                .OrderBy(s => s.Scope, StringComparer.Ordinal)
                .ToList();

            _logger.LogInformation(
                "[BehaviorStateH{H}] MicroAccuracy={Acc:F4} MacroF1={F1:F4} MajorityBaseline={Base:F4} " +
                "(n={N} validation rows across {S} session(s)).",
                horizon, summary.Combined.MicroAccuracy, summary.Combined.MacroF1,
                summary.Combined.MajorityBaselineAccuracy, evalResults.Count, split.ValidationSessions.Count);

            // Staging directory already exists (created by ModelVersionPublishSession) — save
            // there first and verify it loads before it becomes eligible for atomic publish.
            mlContext.Model.Save(predictionPipeline, trainData.Schema, stagingPath);

            if (!ModelVersionPublishSession.TryVerifyLoadable(mlContext, stagingPath, out var loadError))
            {
                summary.Error = $"Model saved but failed load verification: {loadError}";
                _logger.LogError("[BehaviorStateH{H}] {Error}", horizon, summary.Error);
                return summary;
            }

            _logger.LogInformation(
                "[BehaviorStateH{H}] Model staged and load-verified at '{Path}' (final: '{Final}').",
                horizon, stagingPath, summary.ModelPath);
        }
        catch (Exception ex)
        {
            summary.Error = ex.Message;
            _logger.LogError(ex, "[BehaviorStateH{H}] Training failed.", horizon);
        }

        return summary;
    }

    // ── Label exclusion / row classification ────────────────────────────────────

    // Applies BehaviorStateLabelPolicy plus the embedding-length guard to a single training
    // row. Never fabricates a label: Missing, NoSignal ("unknown"), and Invalid rows are all
    // excluded from training, each under its own reason so the training report can show
    // exactly why rows were dropped.
    public static (bool Trainable, string? CanonicalLabel, string? ExclusionReason) ClassifyRow(
        TemporalRiskTrainingRow row)
    {
        if (row.EmbeddingValues.Length != 25)
            return (false, null, "missing-or-invalid-embedding");

        var result = BehaviorStateLabelPolicy.Normalize(row.TargetBehaviorState);
        if (!result.IsTrainable)
        {
            var reason = result.Status switch
            {
                BehaviorStateLabelStatus.Missing => "missing-target-label",
                BehaviorStateLabelStatus.NoSignal => "unknown-no-signal",
                BehaviorStateLabelStatus.Invalid => "invalid-label-literal",
                _ => "excluded-other",
            };
            return (false, null, reason);
        }

        return (true, result.CanonicalLabel, null);
    }

    // ── Session-level split ──────────────────────────────────────────────────────

    // Splits session IDs (not rows) into train/validation sets so every window from a given
    // session lands entirely on one side of the split — a random per-row split would let
    // near-duplicate adjacent windows from the same session leak between train and test.
    // Retained for callers that only have a bare session-ID list (no label/simulation context);
    // TrainHorizon uses the richer, stratified overload below.
    public static (HashSet<string> Train, HashSet<string> Validation) SplitSessionsForTraining(
        IReadOnlyList<string> sessionIds, double trainFraction, int seed)
    {
        var distinct = sessionIds.Distinct().ToList();
        var rng = new Random(seed);
        var shuffled = distinct.OrderBy(_ => rng.Next()).ToList();

        int trainCount = (int)Math.Round(shuffled.Count * trainFraction);
        // Always hold out at least one session for validation when more than one exists.
        if (shuffled.Count >= 2 && trainCount >= shuffled.Count)
            trainCount = shuffled.Count - 1;
        trainCount = Math.Max(0, trainCount);

        var train = shuffled.Take(trainCount).ToHashSet();
        var validation = shuffled.Skip(trainCount).ToHashSet();
        return (train, validation);
    }

    // Stage 9 (corrective pass): grouped STRATIFIED session-level split. Sessions remain the
    // unit of grouping (no session's rows ever split across train/validation), but sessions are
    // additionally stratified by (dominant label, SimId) so a rare class or an
    // under-represented simulation is not accidentally isolated entirely on one side purely by
    // chance, which a flat random shuffle over all sessions can easily do with a small dataset.
    //
    // Algorithm: group sessions by (dominant label in that session's trainable rows, SimId).
    // Strata with >=2 sessions are split individually so both sides get at least one session
    // from that exact (label, sim) combination. Strata with exactly 1 session (the class/sim
    // combination this session represents has no other example) cannot themselves be split, so
    // they are pooled together and the POOL is split proportionally by trainFraction — this
    // still spreads rare combinations across both sides in aggregate, rather than the pre-Stage-9
    // behavior of a single flat shuffle that could (and did, in small samples) put an entire
    // rare class or simulation on only one side of the split. Deterministic from `seed` — never
    // uses string.GetHashCode() (randomized per process in .NET) so results are reproducible
    // across runs and app restarts, matching this method's "recorded seed" contract.
    public static SessionSplitResult SplitSessionsForTraining(
        List<(TemporalRiskTrainingRow Row, string Label)> trainable, double trainFraction, int seed)
    {
        var perSession = trainable
            .GroupBy(t => t.Row.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                SimId = g.First().Row.SimId,
                DominantLabel = g.GroupBy(t => t.Label)
                    .OrderByDescending(lg => lg.Count())
                    .ThenBy(lg => lg.Key, StringComparer.Ordinal)
                    .First().Key,
            })
            .OrderBy(s => s.SessionId, StringComparer.Ordinal)
            .ToList();

        var train = new HashSet<string>();
        var validation = new HashSet<string>();
        var singletonPool = new List<string>();

        foreach (var stratum in perSession
                     .GroupBy(s => (s.DominantLabel, s.SimId))
                     .OrderBy(g => g.Key.DominantLabel, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.SimId, StringComparer.Ordinal))
        {
            var sessionIdsInStratum = stratum.Select(s => s.SessionId).ToList();
            if (sessionIdsInStratum.Count == 1)
            {
                singletonPool.Add(sessionIdsInStratum[0]);
                continue;
            }

            var strataSeed = unchecked(seed + StableStringHash($"{stratum.Key.DominantLabel}|{stratum.Key.SimId}"));
            var rng = new Random(strataSeed);
            var shuffled = sessionIdsInStratum.OrderBy(_ => rng.Next()).ToList();

            int trainCount = Math.Clamp((int)Math.Round(shuffled.Count * trainFraction), 1, shuffled.Count - 1);
            foreach (var sid in shuffled.Take(trainCount)) train.Add(sid);
            foreach (var sid in shuffled.Skip(trainCount)) validation.Add(sid);
        }

        if (singletonPool.Count > 0)
        {
            var poolRng = new Random(seed);
            var shuffledPool = singletonPool.OrderBy(_ => poolRng.Next()).ToList();
            int trainCount = Math.Clamp((int)Math.Round(shuffledPool.Count * trainFraction), 0, shuffledPool.Count);
            foreach (var sid in shuffledPool.Take(trainCount)) train.Add(sid);
            foreach (var sid in shuffledPool.Skip(trainCount)) validation.Add(sid);
        }

        var trainLabelDist = trainable.Where(t => train.Contains(t.Row.SessionId))
            .GroupBy(t => t.Label).ToDictionary(g => g.Key, g => g.Count());
        var valLabelDist = trainable.Where(t => validation.Contains(t.Row.SessionId))
            .GroupBy(t => t.Label).ToDictionary(g => g.Key, g => g.Count());
        var trainSimDist = trainable.Where(t => train.Contains(t.Row.SessionId))
            .GroupBy(t => t.Row.SimId).ToDictionary(g => g.Key, g => g.Count());
        var valSimDist = trainable.Where(t => validation.Contains(t.Row.SessionId))
            .GroupBy(t => t.Row.SimId).ToDictionary(g => g.Key, g => g.Count());

        // Stage 9 requirement: fail with a precise message when the resulting partition cannot
        // support multiclass training or has no held-out data at all — validated AFTER the
        // split, since stratification can (rarely, with a pathological input) still leave one
        // side degenerate.
        string? error = null;
        if (trainLabelDist.Count < 2)
        {
            error = $"Training partition contains fewer than two classes ({trainLabelDist.Count}) " +
                    "after the grouped stratified split — cannot train a multiclass model.";
        }
        else if (validation.Count == 0)
        {
            error = "Grouped stratified split produced zero validation sessions.";
        }

        var (classesAbsentFromTraining, classesAbsentFromValidation,
             simsAbsentFromTraining, simsAbsentFromValidation,
             crossSimStatus, crossSimReason) =
            ComputeCoverage(trainable, trainLabelDist, valLabelDist, trainSimDist, valSimDist);

        return new SessionSplitResult
        {
            TrainSessions = train,
            ValidationSessions = validation,
            TrainLabelDistribution = trainLabelDist,
            ValidationLabelDistribution = valLabelDist,
            TrainSimDistribution = trainSimDist,
            ValidationSimDistribution = valSimDist,
            ValidationError = error,
            ClassesAbsentFromTraining = classesAbsentFromTraining,
            ClassesAbsentFromValidation = classesAbsentFromValidation,
            SimulationsAbsentFromTraining = simsAbsentFromTraining,
            SimulationsAbsentFromValidation = simsAbsentFromValidation,
            CrossSimulationStatus = crossSimStatus,
            CrossSimulationStatusReason = crossSimReason,
        };
    }

    // Stage 6 (corrective pass): explicit coverage reporting — "absent from training/validation"
    // is computed relative to the set of classes/simulations actually present in the trainable
    // data (train ∪ validation), not the full canonical universe, since a class/sim that never
    // appears in the data at all is a different (pre-existing, already-gated) condition.
    //
    // CrossSimulationValidationStatus is deliberately never a training-blocking gate — a model
    // can still be trained and published with InsufficientCoverage; this only controls what the
    // report/manifest are allowed to claim about cross-simulation generalization.
    private static (
        List<string> ClassesAbsentFromTraining,
        List<string> ClassesAbsentFromValidation,
        List<string> SimulationsAbsentFromTraining,
        List<string> SimulationsAbsentFromValidation,
        CrossSimulationValidationStatus Status,
        string Reason)
        ComputeCoverage(
            List<(TemporalRiskTrainingRow Row, string Label)> trainable,
            Dictionary<string, int> trainLabelDist,
            Dictionary<string, int> valLabelDist,
            Dictionary<string, int> trainSimDist,
            Dictionary<string, int> valSimDist)
    {
        var allClasses = trainable.Select(t => t.Label).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        var allSims = trainable.Select(t => t.Row.SimId).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

        var classesAbsentFromTraining = allClasses.Where(c => !trainLabelDist.ContainsKey(c)).ToList();
        var classesAbsentFromValidation = allClasses.Where(c => !valLabelDist.ContainsKey(c)).ToList();
        var simsAbsentFromTraining = allSims.Where(s => !trainSimDist.ContainsKey(s)).ToList();
        var simsAbsentFromValidation = allSims.Where(s => !valSimDist.ContainsKey(s)).ToList();

        CrossSimulationValidationStatus status;
        string reason;
        if (allSims.Count <= 1)
        {
            status = CrossSimulationValidationStatus.NotApplicable;
            reason = allSims.Count == 0
                ? "No simulation data present in the trainable rows."
                : $"Only one simulation ('{allSims[0]}') is present in the trainable data — " +
                  "cross-simulation evaluation does not apply.";
        }
        else if (simsAbsentFromValidation.Count > 0 || simsAbsentFromTraining.Count > 0)
        {
            status = CrossSimulationValidationStatus.InsufficientCoverage;
            var parts = new List<string>();
            if (simsAbsentFromValidation.Count > 0)
                parts.Add($"absent from validation: {string.Join(", ", simsAbsentFromValidation)}");
            if (simsAbsentFromTraining.Count > 0)
                parts.Add($"absent from training: {string.Join(", ", simsAbsentFromTraining)}");
            reason = $"{allSims.Count} simulations present ({string.Join(", ", allSims)}), but coverage is " +
                     $"incomplete — {string.Join("; ", parts)}. Combined ('all') metrics must NOT be read as " +
                     "evidence of cross-simulation generalization.";
        }
        else
        {
            status = CrossSimulationValidationStatus.Passed;
            reason = $"All {allSims.Count} simulations present in the trainable data " +
                     $"({string.Join(", ", allSims)}) have at least one row on both sides of the split.";
        }

        return (classesAbsentFromTraining, classesAbsentFromValidation,
                simsAbsentFromTraining, simsAbsentFromValidation, status, reason);
    }

    // Stable, process-independent string hash for deriving a deterministic per-stratum RNG
    // seed. string.GetHashCode() is randomized per process in .NET Core by design and would
    // silently break reproducibility across runs/restarts.
    private static int StableStringHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (var c in value)
                hash = hash * 31 + c;
            return hash;
        }
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    // Computes micro accuracy, per-class precision/recall/F1, macro F1, majority-class
    // baseline accuracy, and a confusion matrix for one evaluation slice (all simulations
    // combined, or a single SimId). trainingLabelsForMajority is the label distribution of
    // the TRAINING split (not this slice) so the majority-class baseline reflects what a
    // naive "always predict the most common training label" model would score.
    //
    // Stage 8 (corrective pass): the canonical 5-class set (BehaviorStateLabelPolicy.CanonicalLabels)
    // is used unconditionally — not the classes merely observed in `pairs` — so a class the
    // model never predicts, or that has zero support in this particular slice, still appears
    // explicitly with support/predicted counts and a real (non-dropped) F1 of 0, and macro F1
    // is always averaged over the same 5 terms regardless of what this slice happens to
    // contain. `pairs` is expected to already contain only canonical labels (ClassifyRow /
    // BehaviorStateLabelPolicy excludes missing/unknown/invalid rows upstream); any
    // non-canonical value is simply excluded from the confusion matrix rather than corrupting it.
    public static BehaviorStateMetricsSlice ComputeMetricsSlice(
        string scope,
        List<(string Actual, string Predicted)> pairs,
        IEnumerable<string> trainingLabelsForMajority)
    {
        var slice = new BehaviorStateMetricsSlice { Scope = scope, RowCount = pairs.Count };

        var classes = BehaviorStateLabelPolicy.CanonicalLabels.OrderBy(c => c, StringComparer.Ordinal).ToList();

        var confusion = classes.ToDictionary(a => a, a => classes.ToDictionary(p => p, _ => 0));
        foreach (var (actual, predicted) in pairs)
        {
            if (confusion.TryGetValue(actual, out var row) && row.ContainsKey(predicted))
                row[predicted]++;
        }
        slice.ConfusionMatrix = confusion;

        int correct = pairs.Count(p => p.Actual == p.Predicted);
        slice.MicroAccuracy = pairs.Count > 0 ? (double)correct / pairs.Count : 0;

        var perClass = new List<BehaviorStateClassMetrics>();
        foreach (var cls in classes)
        {
            int tp = pairs.Count(p => p.Actual == cls && p.Predicted == cls);
            int fp = pairs.Count(p => p.Actual != cls && p.Predicted == cls);
            int fn = pairs.Count(p => p.Actual == cls && p.Predicted != cls);
            int support = pairs.Count(p => p.Actual == cls);
            int predictedCount = pairs.Count(p => p.Predicted == cls);

            // Undefined ratios (no support, or never predicted) are reported as 0, not null —
            // a supported class the model never predicts gets Precision=0 (0/0 by convention)
            // and Recall=0 (calculated normally: 0 true positives / support); a class with zero
            // support gets both as 0 by the same convention, kept consistent with the mirrored
            // KQL in AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql.
            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
            double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
            double f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;

            perClass.Add(new BehaviorStateClassMetrics
            {
                ClassLabel = cls,
                SupportCount = support,
                PredictedCount = predictedCount,
                Precision = precision,
                Recall = recall,
                F1 = f1,
            });
        }
        slice.PerClass = perClass;
        // Always divides by the full canonical class count (5), never by however many classes
        // happened to appear in this slice.
        slice.MacroF1 = perClass.Count > 0 ? perClass.Average(c => c.F1) : 0;

        var majorityClass = trainingLabelsForMajority
            .GroupBy(l => l)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        slice.MajorityClass = majorityClass;
        slice.MajorityBaselineAccuracy = majorityClass is null || pairs.Count == 0
            ? 0
            : (double)pairs.Count(p => p.Actual == majorityClass) / pairs.Count;

        return slice;
    }

    // ── Versioned artifact naming ─────────────────────────────────────────────

    // Strips any character that is not a letter, digit, dash, underscore, or dot.
    public static string SanitizeModelVersion(string version) =>
        new string(version.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    // "temporal-behavior-state-v2" -> "v2"; falls back to the sanitized full string.
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
    // (Recursor/TrainingModels/temporal_behavior_state_h{n}_{suffix}.zip) that the publisher
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
}
