using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// ML.NET-backed implementation of <see cref="IBehaviorStatePredictionService"/>.
/// Supports four independent binary-classification models:
///   - HintDependence        (current-window)
///   - Confusion             (current-window)
///   - StableMastery         (current-window)
///   - HintDependenceNextWindow (baseline; next-window guardrail)
///
/// Each model is optional. A missing, unreadable, or corrupt model file causes a
/// console warning at startup and returns 0.0 / null for that model at runtime —
/// the app never crashes because of a single model's absence or failure.
///
/// The next-window model uses the same <see cref="BehaviorStatePredictionInput"/>
/// schema as the current-window models. Its result is surfaced as nullable fields
/// on <see cref="BehaviorStatePrediction"/> so callers can distinguish
/// "not configured" from a real zero probability.
///
/// PredictionEngine is created per call to avoid ML.NET thread-safety issues.
/// </summary>
public sealed class MlNetBehaviorStatePredictionService : IBehaviorStatePredictionService, IDisposable
{
    private readonly MLContext _mlContext;
    private readonly ITransformer? _hintDependenceModel;
    private readonly ITransformer? _confusionModel;
    private readonly ITransformer? _stableMasteryModel;
    private readonly ITransformer? _hintDependenceNextWindowModel;
    private readonly string _modelVersion;
    private bool _disposed;

    /// <param name="hintDependenceModelPath">Path to hint-dependence model .zip (optional).</param>
    /// <param name="confusionModelPath">Path to confusion model .zip (optional).</param>
    /// <param name="stableMasteryModelPath">Path to stable-mastery model .zip (optional).</param>
    /// <param name="hintDependenceNextWindowModelPath">
    ///   Path to the baseline next-window hint-dependence model .zip (optional).
    ///   This model predicts hint dependence in the *next* feature window using the same
    ///   input schema as the current-window models, with three columns intentionally
    ///   excluded from its feature set (HintDependenceScore, HintDependenceTrend,
    ///   HintCountInWindow) — those extra columns are ignored by ML.NET at inference time.
    /// </param>
    /// <param name="modelVersion">Version tag included in every prediction result.</param>
    public MlNetBehaviorStatePredictionService(
        string? hintDependenceModelPath,
        string? confusionModelPath                  = null,
        string? stableMasteryModelPath              = null,
        string? hintDependenceNextWindowModelPath   = null,
        string  modelVersion                        = "mlnet-multi-v1")
    {
        _modelVersion                  = modelVersion;
        _mlContext                     = new MLContext();
        _hintDependenceModel           = TryLoadModel(hintDependenceModelPath,           "HintDependence");
        _confusionModel                = TryLoadModel(confusionModelPath,                "Confusion");
        _stableMasteryModel            = TryLoadModel(stableMasteryModelPath,            "StableMastery");
        _hintDependenceNextWindowModel = TryLoadModel(hintDependenceNextWindowModelPath, "HintDependenceNextWindow");
    }

    public Task<BehaviorStatePrediction?> PredictAsync(BehaviorStateFeatureVector input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var mlInput = BehaviorStatePredictionMapper.Map(input);

        float hintDep  = PredictIfAvailable(_hintDependenceModel, mlInput, "HintDependence");
        float confusion = PredictIfAvailable(_confusionModel,     mlInput, "Confusion");
        float mastery  = PredictIfAvailable(_stableMasteryModel,  mlInput, "StableMastery");

        var prediction = new BehaviorStatePrediction
        {
            HintDependenceProbability = hintDep,
            ConfusionProbability      = confusion,
            StableMasteryProbability  = mastery,
            ModelVersion              = _modelVersion,
            InferenceMode             = "shadow-mlnet",
        };

        // Next-window guardrail — populate only when the model is loaded.
        if (_hintDependenceNextWindowModel is not null)
        {
            var nextOutput = PredictNextWindowIfAvailable(_hintDependenceNextWindowModel, mlInput);
            if (nextOutput is not null)
            {
                prediction.HintDependenceNextProbability   = nextOutput.Value.probability;
                prediction.HintDependenceNextPredictedLabel = nextOutput.Value.predictedLabel;
                prediction.HintDependenceNextModelVersion  = _modelVersion + "-next-baseline";
            }
        }

        return Task.FromResult<BehaviorStatePrediction?>(prediction);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to load a model from <paramref name="path"/>.
    /// Returns null (and logs a warning) if the path is absent, the file does not exist,
    /// or the load throws for any reason (e.g. corrupt or schema-incompatible file).
    /// Never throws.
    /// </summary>
    private ITransformer? TryLoadModel(string? path, string modelName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] {modelName} model path not configured — skipping.");
            return null;
        }

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] {modelName} model file not found at '{path}' — skipping.");
            return null;
        }

        try
        {
            var model = _mlContext.Model.Load(path, out _);
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] {modelName} model loaded from '{path}'.");
            return model;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] ERROR loading {modelName} model from '{path}': " +
                $"{ex.GetType().Name} — {ex.Message}. Prediction for this model will return 0.0.");
            return null;
        }
    }

    /// <summary>
    /// Runs inference for a single model. Returns 0.0 if the model is null or if
    /// engine creation or prediction throws for any reason.
    /// Never throws; a failure in one model does not affect the others.
    /// </summary>
    private float PredictIfAvailable(
        ITransformer? model,
        BehaviorStatePredictionInput input,
        string modelName)
    {
        if (model is null)
            return 0.0f;

        try
        {
            // PredictionEngine is not thread-safe — create one per call.
            var engine = _mlContext.Model
                .CreatePredictionEngine<BehaviorStatePredictionInput, BinaryPredictionOutput>(model);

            return engine.Predict(input).Probability;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] ERROR during {modelName} prediction: " +
                $"{ex.GetType().Name} — {ex.Message}. Returning 0.0 for this model.");
            return 0.0f;
        }
    }

    /// <summary>
    /// Runs next-window inference. Returns (probability, predictedLabel) on success,
    /// null on any failure. Uses the same input type as the current-window models —
    /// the baseline next-window model's pipeline ignores the three excluded columns
    /// (HintDependenceScore, HintDependenceTrend, HintCountInWindow).
    /// Never throws.
    /// </summary>
    private (float probability, bool predictedLabel)? PredictNextWindowIfAvailable(
        ITransformer model,
        BehaviorStatePredictionInput input)
    {
        try
        {
            var engine = _mlContext.Model
                .CreatePredictionEngine<BehaviorStatePredictionInput, BinaryPredictionOutput>(model);

            var output = engine.Predict(input);
            return (output.Probability, output.PredictedLabel);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MlNetBehaviorStatePredictionService] ERROR during HintDependenceNextWindow prediction: " +
                $"{ex.GetType().Name} — {ex.Message}. Returning null for next-window fields.");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
