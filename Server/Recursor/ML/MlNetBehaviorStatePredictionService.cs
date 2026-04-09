using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// ML.NET-backed implementation of <see cref="IBehaviorStatePredictionService"/>.
/// Phase 1: predicts only LabelHintDependence.
/// ConfusionProbability and StableMasteryProbability remain 0.0 until their models are trained.
///
/// This service loads the model file once at construction (singleton lifetime).
/// PredictionEngine is created per call to avoid ML.NET thread-safety issues.
/// </summary>
public sealed class MlNetBehaviorStatePredictionService : IBehaviorStatePredictionService, IDisposable
{
    private readonly MLContext _mlContext;
    private readonly ITransformer _hintDependenceModel;
    private readonly string _modelVersion;
    private bool _disposed;

    /// <param name="hintDependenceModelPath">Path to the saved ML.NET model .zip file.</param>
    /// <param name="modelVersion">Version tag included in every prediction result.</param>
    public MlNetBehaviorStatePredictionService(
        string hintDependenceModelPath,
        string modelVersion = "mlnet-hintdep-v1")
    {
        if (!File.Exists(hintDependenceModelPath))
            throw new FileNotFoundException(
                $"ML.NET hint-dependence model file not found: {hintDependenceModelPath}",
                hintDependenceModelPath);

        _modelVersion = modelVersion;
        _mlContext = new MLContext();
        _hintDependenceModel = _mlContext.Model.Load(hintDependenceModelPath, out _);
    }

    public Task<BehaviorStatePrediction?> PredictAsync(BehaviorStateFeatureVector input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var mlInput = BehaviorStatePredictionMapper.Map(input);

        // PredictionEngine is not thread-safe — create one per call.
        var engine = _mlContext.Model.CreatePredictionEngine<BehaviorStatePredictionInput, BinaryPredictionOutput>(
            _hintDependenceModel);

        var hintDep = engine.Predict(mlInput);

        var prediction = new BehaviorStatePrediction
        {
            ConfusionProbability       = 0.0,
            HintDependenceProbability  = hintDep.Probability,
            StableMasteryProbability   = 0.0,
            ModelVersion               = _modelVersion,
            InferenceMode              = "shadow-mlnet",
        };

        return Task.FromResult<BehaviorStatePrediction?>(prediction);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
