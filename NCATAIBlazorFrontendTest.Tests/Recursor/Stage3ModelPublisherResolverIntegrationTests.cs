using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 3 corrective-pass integration test: this is the test that would have caught the actual
/// bug found in this pass — ModelVersionPublisher writes trained artifacts to
/// versions/{family}/{version}/h{n}.zip + manifest.json, but the runtime resolver
/// (ResolveVersionedModelPath, called from Program.cs at startup) computed a completely
/// different flat filename that the publisher never wrote to. A freshly trained/published
/// version was therefore silently unreachable by the running app.
///
/// This test exercises the REAL pipeline end-to-end, with no mocking of the resolver or
/// prediction service: train (real ML.NET) → publish (real ModelVersionPublisher) → resolve
/// (the exact ResolveVersionedModelPath static method Program.cs calls) → validate manifest
/// (ModelVersionManifestValidator, the exact validator Program.cs calls) → load (the real
/// TemporalBehaviorStatePredictionService / TemporalElevatedRiskPredictionService) → infer.
/// Any future publisher/resolver layout divergence will fail this test.
/// </summary>
public class Stage3ModelPublisherResolverIntegrationTests : IDisposable
{
    private readonly string _contentRoot;

    public Stage3ModelPublisherResolverIntegrationTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "recursor-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeTrainingQueryService : IAdxTemporalTrainingQueryService
    {
        private readonly List<TemporalRiskTrainingRow> _rows;
        public FakeTrainingQueryService(List<TemporalRiskTrainingRow> rows) => _rows = rows;
        public Task<List<TemporalRiskTrainingRow>> QueryTrainingRowsAsync() => Task.FromResult(_rows);
    }

    // 60 synthetic sessions x 3 horizons, three canonical behavior-state labels cycled with
    // features nudged per label so training has real (if noisy) signal — enough rows/classes/
    // sessions to clear MinTrainableRows, the >=2-distinct-labels gate, and the grouped-
    // stratified split's per-stratum minimums for every horizon.
    private static List<TemporalRiskTrainingRow> BuildSyntheticBehaviorStateRows()
    {
        var rows = new List<TemporalRiskTrainingRow>();
        var labels = new[] { "stable", "struggling", "mastering" };
        var rng = new Random(11);

        for (int session = 0; session < 60; session++)
        {
            var label = labels[session % labels.Length];
            var bias = label switch { "stable" => 0.0, "struggling" => 1.5, _ => 3.0 };

            for (int horizon = 1; horizon <= 3; horizon++)
            {
                var features = new double[25];
                for (int i = 0; i < 25; i++)
                    features[i] = rng.NextDouble() + bias;

                rows.Add(new TemporalRiskTrainingRow
                {
                    SessionId = $"sess-{session}",
                    UserId = $"user-{session}",
                    SimId = "test-sim",
                    ScenarioId = "test-scenario",
                    SourceWindowIndex = 1,
                    TargetWindowIndex = 1 + horizon,
                    Horizon = horizon,
                    EmbeddingValues = features.Select(v => (float)v).ToArray(),
                    TargetNearTermRisk = "low",
                    TargetBehaviorState = label,
                });
            }
        }

        return rows;
    }

    // Same shape, labeled for the binary elevated-risk trainer (low / medium / high near-term
    // risk — the trainer collapses medium+high into "elevated").
    private static List<TemporalRiskTrainingRow> BuildSyntheticElevatedRiskRows()
    {
        var rows = new List<TemporalRiskTrainingRow>();
        var labels = new[] { "low", "medium", "high" };
        var rng = new Random(13);

        for (int session = 0; session < 60; session++)
        {
            var label = labels[session % labels.Length];
            var bias = label == "low" ? 0.0 : label == "medium" ? 1.5 : 3.0;

            for (int horizon = 1; horizon <= 3; horizon++)
            {
                var features = new double[25];
                for (int i = 0; i < 25; i++)
                    features[i] = rng.NextDouble() + bias;

                rows.Add(new TemporalRiskTrainingRow
                {
                    SessionId = $"sess-{session}",
                    UserId = $"user-{session}",
                    SimId = "test-sim",
                    ScenarioId = "test-scenario",
                    SourceWindowIndex = 1,
                    TargetWindowIndex = 1 + horizon,
                    Horizon = horizon,
                    EmbeddingValues = features.Select(v => (float)v).ToArray(),
                    TargetNearTermRisk = label,
                    TargetBehaviorState = "",
                });
            }
        }

        return rows;
    }

    [Fact]
    public async Task BehaviorState_TrainPublishResolveLoadInfer_EndToEnd_UsesTheSameArtifactsThroughout()
    {
        const string version = "temporal-behavior-state-resolver-test-v1";

        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var config = new ConfigurationBuilder().Build();
        var trainer = new TemporalBehaviorStateModelTrainingService(
            new FakeTrainingQueryService(BuildSyntheticBehaviorStateRows()),
            config, env, NullLogger<TemporalBehaviorStateModelTrainingService>.Instance);

        // 1-3: train and publish (real ML.NET, real ModelVersionPublisher).
        var report = await trainer.TrainAsync(version);
        Assert.True(report.Published, report.PublishError);
        Assert.Equal(3, report.Horizons.Count);
        Assert.All(report.Horizons, h => Assert.Null(h.Error));

        // 4/5: this is the manifest Program.cs itself validates before wiring up the prediction
        // service — must not throw for a cleanly published version.
        var validateEx = Record.Exception(() =>
            ModelVersionManifestValidator.ValidateVersionDirectory(
                _contentRoot, TemporalBehaviorStateModelTrainingService.ModelFamily, version));
        Assert.Null(validateEx);

        // 6: resolve using the EXACT static method Program.cs calls at startup — no test-only
        // path-building logic. This is what would have failed before the Stage 3 fix.
        var h1Path = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(1, version, _contentRoot, null);
        var h2Path = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(2, version, _contentRoot, null);
        var h3Path = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(3, version, _contentRoot, null);

        // The resolver must point at exactly what the publisher wrote — not a parallel,
        // never-populated flat filename.
        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h1.zip"), h1Path);
        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h2.zip"), h2Path);
        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h3.zip"), h3Path);
        Assert.True(File.Exists(h1Path));
        Assert.True(File.Exists(h2Path));
        Assert.True(File.Exists(h3Path));

        // 7: load via the real runtime prediction service, exactly as Program.cs constructs it.
        using var predictionService = new TemporalBehaviorStatePredictionService(
            NullLogger<TemporalBehaviorStatePredictionService>.Instance, h1Path, h2Path, h3Path, version);

        Assert.Equal(version, predictionService.LoadedModelVersion);

        // 8: infer.
        var embedding = new TemporalEmbeddingVector
        {
            SessionId = "sess-infer",
            UserId = "user-infer",
            Values = Enumerable.Repeat(0.5, 25).ToArray(),
        };
        var prediction = predictionService.Predict(embedding);

        // 9: all three horizons loaded and correspond to the requested version.
        Assert.NotNull(prediction.Horizon1);
        Assert.NotNull(prediction.Horizon2);
        Assert.NotNull(prediction.Horizon3);

        foreach (var horizonPrediction in new[] { prediction.Horizon1!, prediction.Horizon2!, prediction.Horizon3! })
        {
            Assert.Equal(version, horizonPrediction.ModelVersion);
            Assert.Contains(horizonPrediction.PredictedBehaviorState, BehaviorStateLabelPolicy.CanonicalLabels);
            Assert.InRange(horizonPrediction.Confidence, 0.0, 1.0);
            Assert.NotEmpty(horizonPrediction.ClassProbabilities);
            Assert.All(horizonPrediction.ClassProbabilities.Values, p => Assert.InRange(p, 0.0f, 1.0f));
        }
    }

    [Fact]
    public async Task ElevatedRisk_TrainPublishResolveLoadInfer_EndToEnd_UsesTheSameArtifactsThroughout()
    {
        const string version = "temporal-elevated-risk-resolver-test-v1";

        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var config = new ConfigurationBuilder().Build();
        var trainer = new TemporalElevatedRiskModelTrainingService(
            new FakeTrainingQueryService(BuildSyntheticElevatedRiskRows()),
            config, env, NullLogger<TemporalElevatedRiskModelTrainingService>.Instance);

        var report = await trainer.TrainAsync(version);
        Assert.True(report.Published, report.PublishError);
        Assert.Equal(3, report.Horizons.Count);
        Assert.All(report.Horizons, h => Assert.Null(h.Error));

        var validateEx = Record.Exception(() =>
            ModelVersionManifestValidator.ValidateVersionDirectory(
                _contentRoot, TemporalElevatedRiskModelTrainingService.ModelFamily, version));
        Assert.Null(validateEx);

        var h1Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(1, version, _contentRoot, null);
        var h2Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(2, version, _contentRoot, null);
        var h3Path = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(3, version, _contentRoot, null);

        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h1.zip"), h1Path);
        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h2.zip"), h2Path);
        Assert.Equal(Path.Combine(report.PublishedVersionDirectory!, "h3.zip"), h3Path);
        Assert.True(File.Exists(h1Path));
        Assert.True(File.Exists(h2Path));
        Assert.True(File.Exists(h3Path));

        using var predictionService = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance, h1Path, h2Path, h3Path, version);

        Assert.Equal(version, predictionService.LoadedModelVersion);

        var embedding = new TemporalEmbeddingVector
        {
            SessionId = "sess-infer",
            UserId = "user-infer",
            Values = Enumerable.Repeat(0.5, 25).ToArray(),
        };
        var prediction = predictionService.Predict(embedding);

        Assert.NotNull(prediction.Horizon1);
        Assert.NotNull(prediction.Horizon2);
        Assert.NotNull(prediction.Horizon3);

        foreach (var horizonPrediction in new[] { prediction.Horizon1!, prediction.Horizon2!, prediction.Horizon3! })
        {
            Assert.Equal(version, horizonPrediction.ModelVersion);
            Assert.False(string.IsNullOrEmpty(horizonPrediction.PredictedElevatedRisk));
            Assert.InRange(horizonPrediction.Confidence, 0.0, 1.0);
        }
    }

    [Fact]
    public void ManifestValidator_ThrowsClearly_WhenVersionDirectoryExistsWithoutManifest()
    {
        // Simulates a leftover/corrupted partial publish — must fail loudly, not load silently.
        var versionDirectory = ModelVersionPublisher.GetVersionDirectory(
            _contentRoot, "temporal-behavior-state", "broken-v1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllBytes(Path.Combine(versionDirectory, "h1.zip"), new byte[] { 1, 2, 3 });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModelVersionManifestValidator.ValidateVersionDirectory(_contentRoot, "temporal-behavior-state", "broken-v1"));

        Assert.Contains("manifest.json", ex.Message);
    }

    [Fact]
    public void ManifestValidator_DoesNotThrow_WhenNothingPublishedYetForThisVersion()
    {
        // The ordinary "no model trained yet" state must remain a tolerated non-error.
        var ex = Record.Exception(() =>
            ModelVersionManifestValidator.ValidateVersionDirectory(
                _contentRoot, "temporal-behavior-state", "never-trained-v1"));

        Assert.Null(ex);
    }
}
