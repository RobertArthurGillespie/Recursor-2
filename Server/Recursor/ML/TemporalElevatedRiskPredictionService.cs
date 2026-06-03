using Microsoft.Extensions.Logging;
using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public interface ITemporalElevatedRiskPredictionService
{
    // Returns binary shadow predictions (low / elevated) for horizons 1–3.
    // Any horizon whose model is not loaded returns a null prediction.
    // Never throws into the caller — all errors are caught and logged internally.
    TemporalElevatedRiskPredictionResult Predict(TemporalEmbeddingVector embedding);
}

// Singleton — loaded once at startup; safe to call from scoped services.
// PredictionEngine is created per call to avoid ML.NET thread-safety issues.
public sealed class TemporalElevatedRiskPredictionService
    : ITemporalElevatedRiskPredictionService, IDisposable
{
    // Backward-compatible default — used when no version is configured.
    public const string ModelVersion = "temporal-elevated-risk-v1";

    private readonly MLContext _mlContext = new(seed: 42);
    private readonly ITransformer? _h1Model;
    private readonly ITransformer? _h2Model;
    private readonly ITransformer? _h3Model;
    private readonly ILogger<TemporalElevatedRiskPredictionService> _logger;
    private readonly string _loadedModelVersion;
    private bool _disposed;

    // The version string stamped onto every prediction row from this service instance.
    public string LoadedModelVersion => _loadedModelVersion;

    public TemporalElevatedRiskPredictionService(
        ILogger<TemporalElevatedRiskPredictionService> logger,
        string? h1ModelPath,
        string? h2ModelPath,
        string? h3ModelPath,
        string? modelVersion = null)
    {
        _loadedModelVersion = string.IsNullOrWhiteSpace(modelVersion) ? ModelVersion : modelVersion;
        _logger  = logger;
        _h1Model = TryLoad(h1ModelPath, "H1");
        _h2Model = TryLoad(h2ModelPath, "H2");
        _h3Model = TryLoad(h3ModelPath, "H3");

        _logger.LogInformation(
            "[TemporalElevatedRiskPredictionService] Model status — H1={H1} H2={H2} H3={H3} Version={Version}.",
            _h1Model is not null, _h2Model is not null, _h3Model is not null, _loadedModelVersion);
    }

    public TemporalElevatedRiskPredictionResult Predict(TemporalEmbeddingVector embedding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (embedding.Values.Length != 25)
        {
            _logger.LogWarning(
                "[TemporalElevatedRiskPredictionService] Embedding has {Len} values (expected 25) — skipping.",
                embedding.Values.Length);
            return new TemporalElevatedRiskPredictionResult();
        }

        var input = new TemporalRiskModelInput
        {
            Features = embedding.Values.Select(d => (float)d).ToArray(),
        };

        return new TemporalElevatedRiskPredictionResult
        {
            Horizon1 = PredictIfLoaded(_h1Model, input, 1),
            Horizon2 = PredictIfLoaded(_h2Model, input, 2),
            Horizon3 = PredictIfLoaded(_h3Model, input, 3),
        };
    }

    public void Dispose() => _disposed = true;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TemporalElevatedRiskHorizonPrediction? PredictIfLoaded(
        ITransformer? model,
        TemporalRiskModelInput input,
        int horizon)
    {
        if (model is null) return null;

        try
        {
            var engine = _mlContext.Model
                .CreatePredictionEngine<TemporalRiskModelInput, TemporalElevatedRiskModelOutput>(model);
            var output = engine.Predict(input);
            return new TemporalElevatedRiskHorizonPrediction
            {
                PredictedElevatedRisk = output.PredictedLabel ?? "",
                Confidence            = output.Confidence,
                ModelVersion          = _loadedModelVersion,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[TemporalElevatedRiskPredictionService] Horizon {H} prediction failed: {Msg}.",
                horizon, ex.Message);
            return null;
        }
    }

    private ITransformer? TryLoad(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning(
                "[TemporalElevatedRiskPredictionService] {Label} model path not configured — skipping.", label);
            return null;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "[TemporalElevatedRiskPredictionService] {Label} model not found at '{Path}' — skipping.",
                label, path);
            return null;
        }

        try
        {
            var model = _mlContext.Model.Load(path, out _);
            _logger.LogInformation(
                "[TemporalElevatedRiskPredictionService] {Label} model loaded from '{Path}'.", label, path);
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[TemporalElevatedRiskPredictionService] Failed to load {Label} model from '{Path}': {Msg}.",
                label, path, ex.Message);
            return null;
        }
    }
}
