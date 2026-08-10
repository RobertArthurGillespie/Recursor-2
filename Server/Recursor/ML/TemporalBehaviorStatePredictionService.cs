using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

public interface ITemporalBehaviorStatePredictionService
{
    // Returns multiclass shadow predictions (struggling/recovering/stable/mastering/conflicted)
    // for horizons 1-3. Any horizon whose model is not loaded returns a null prediction.
    // Never throws into the caller — all errors are caught and logged internally.
    // Purely observational: callers must never route this output into adaptation policy,
    // guardrails, or any parameter change.
    TemporalBehaviorStatePredictionResult Predict(TemporalEmbeddingVector embedding);
}

// Singleton — loaded once at startup; safe to call from scoped services.
// PredictionEngine is created per call to avoid ML.NET thread-safety issues.
public sealed class TemporalBehaviorStatePredictionService
    : ITemporalBehaviorStatePredictionService, IDisposable
{
    // Backward-compatible default — used when no version is configured.
    public const string ModelVersion = "temporal-behavior-state-v1";

    private readonly MLContext _mlContext = new(seed: 42);
    private readonly ITransformer? _h1Model;
    private readonly ITransformer? _h2Model;
    private readonly ITransformer? _h3Model;
    private readonly string[] _h1ClassNames;
    private readonly string[] _h2ClassNames;
    private readonly string[] _h3ClassNames;
    private readonly ILogger<TemporalBehaviorStatePredictionService> _logger;
    private readonly string _loadedModelVersion;
    private bool _disposed;

    // The version string stamped onto every prediction row from this service instance.
    public string LoadedModelVersion => _loadedModelVersion;

    public TemporalBehaviorStatePredictionService(
        ILogger<TemporalBehaviorStatePredictionService> logger,
        string? h1ModelPath,
        string? h2ModelPath,
        string? h3ModelPath,
        string? modelVersion = null)
    {
        _loadedModelVersion = string.IsNullOrWhiteSpace(modelVersion) ? ModelVersion : modelVersion;
        _logger = logger;

        (_h1Model, _h1ClassNames) = TryLoad(h1ModelPath, "H1");
        (_h2Model, _h2ClassNames) = TryLoad(h2ModelPath, "H2");
        (_h3Model, _h3ClassNames) = TryLoad(h3ModelPath, "H3");

        _logger.LogInformation(
            "[TemporalBehaviorStatePredictionService] Model status — H1={H1} H2={H2} H3={H3} Version={Version}.",
            _h1Model is not null, _h2Model is not null, _h3Model is not null, _loadedModelVersion);
    }

    public TemporalBehaviorStatePredictionResult Predict(TemporalEmbeddingVector embedding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (embedding.Values.Length != 25)
        {
            _logger.LogWarning(
                "[TemporalBehaviorStatePredictionService] Embedding has {Len} values (expected 25) — skipping.",
                embedding.Values.Length);
            return new TemporalBehaviorStatePredictionResult();
        }

        var input = new TemporalRiskModelInput
        {
            Features = embedding.Values.Select(d => (float)d).ToArray(),
        };

        return new TemporalBehaviorStatePredictionResult
        {
            Horizon1 = PredictIfLoaded(_h1Model, _h1ClassNames, input, 1),
            Horizon2 = PredictIfLoaded(_h2Model, _h2ClassNames, input, 2),
            Horizon3 = PredictIfLoaded(_h3Model, _h3ClassNames, input, 3),
        };
    }

    public void Dispose() => _disposed = true;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TemporalBehaviorStateHorizonPrediction? PredictIfLoaded(
        ITransformer? model,
        string[] classNames,
        TemporalRiskModelInput input,
        int horizon)
    {
        if (model is null) return null;

        try
        {
            var engine = _mlContext.Model
                .CreatePredictionEngine<TemporalRiskModelInput, TemporalBehaviorStateModelOutput>(model);
            var output = engine.Predict(input);

            var probabilities = new Dictionary<string, float>();
            if (classNames.Length > 0 && classNames.Length == output.Score.Length)
            {
                for (int i = 0; i < classNames.Length; i++)
                    probabilities[classNames[i]] = output.Score[i];
            }

            return new TemporalBehaviorStateHorizonPrediction
            {
                PredictedBehaviorState = output.PredictedLabel ?? "",
                Confidence             = output.Confidence,
                ClassProbabilities     = probabilities,
                ModelVersion           = _loadedModelVersion,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[TemporalBehaviorStatePredictionService] Horizon {H} prediction failed: {Msg}.",
                horizon, ex.Message);
            return null;
        }
    }

    private (ITransformer? Model, string[] ClassNames) TryLoad(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning(
                "[TemporalBehaviorStatePredictionService] {Label} model path not configured — skipping.", label);
            return (null, []);
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "[TemporalBehaviorStatePredictionService] {Label} model not found at '{Path}' — skipping.",
                label, path);
            return (null, []);
        }

        try
        {
            var model = _mlContext.Model.Load(path, out var inputSchema);
            var classNames = ExtractClassNames(model, inputSchema);
            _logger.LogInformation(
                "[TemporalBehaviorStatePredictionService] {Label} model loaded from '{Path}' ({N} classes).",
                label, path, classNames.Length);
            return (model, classNames);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[TemporalBehaviorStatePredictionService] Failed to load {Label} model from '{Path}': {Msg}.",
                label, path, ex.Message);
            return (null, []);
        }
    }

    // Reads the "Score" column's slot-name annotations to recover the ordered class-name
    // vocabulary the model was trained with, so ClassProbabilities can be keyed by label
    // (e.g. "stable") instead of an opaque array index. Falls back to an empty array (no
    // probabilities persisted, PredictedBehaviorState is unaffected) if the model doesn't
    // expose slot names for any reason.
    private static string[] ExtractClassNames(ITransformer model, DataViewSchema inputSchema)
    {
        try
        {
            var outputSchema = model.GetOutputSchema(inputSchema);
            var scoreColumn = outputSchema.GetColumnOrNull("Score");
            if (scoreColumn is null) return [];

            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            scoreColumn.Value.GetSlotNames(ref slotNames);
            return slotNames.DenseValues().Select(v => v.ToString()).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
