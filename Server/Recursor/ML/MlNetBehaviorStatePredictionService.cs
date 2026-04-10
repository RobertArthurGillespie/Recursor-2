using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// ML.NET-backed implementation of <see cref="IBehaviorStatePredictionService"/>.
/// Supports three independent binary-classification models:
///   - HintDependence
///   - Confusion
///   - StableMastery
///
/// Each model is optional. A missing, unreadable, or corrupt model file causes a
/// console warning at startup and returns 0.0 for that model at runtime — the app
/// never crashes because of a single model's absence or failure.
///
/// PredictionEngine is created per call to avoid ML.NET thread-safety issues.
/// </summary>
public sealed class MlNetBehaviorStatePredictionService : IBehaviorStatePredictionService, IDisposable
{
    private readonly MLContext _mlContext;
    private readonly ITransformer? _hintDependenceModel;
    private readonly ITransformer? _confusionModel;
    private readonly ITransformer? _stableMasteryModel;
    private readonly string _modelVersion;
    private bool _disposed;

    /// <param name="hintDependenceModelPath">Path to hint-dependence model .zip (optional).</param>
    /// <param name="confusionModelPath">Path to confusion model .zip (optional).</param>
    /// <param name="stableMasteryModelPath">Path to stable-mastery model .zip (optional).</param>
    /// <param name="modelVersion">Version tag included in every prediction result.</param>
    public MlNetBehaviorStatePredictionService(
        string? hintDependenceModelPath,
        string? confusionModelPath     = null,
        string? stableMasteryModelPath = null,
        string  modelVersion           = "mlnet-multi-v1")
    {
        _modelVersion        = modelVersion;
        _mlContext           = new MLContext();
        _hintDependenceModel = TryLoadModel(hintDependenceModelPath, "HintDependence");
        _confusionModel      = TryLoadModel(confusionModelPath,      "Confusion");
        _stableMasteryModel  = TryLoadModel(stableMasteryModelPath,  "StableMastery");
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

    public void Dispose()
    {
        _disposed = true;
    }
}
