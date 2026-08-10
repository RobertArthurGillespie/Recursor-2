using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 4 / Stage 13 corrective-pass test: exercises the real (non-mocked) ML.NET training
/// path end-to-end — train on synthetic data, save, reload, and predict — through the
/// simplest of the three temporal trainers (temporal-risk: raw 3-class labels, no label
/// collapsing/policy). Confirms the Stage 4 immutable-publish contract at the trainer level:
/// existing versions are rejected without retraining, and publishing never touches the legacy
/// flat model paths a running prediction service has already loaded.
/// </summary>
public class TemporalRiskTrainerImmutablePublishTests : IDisposable
{
    private readonly string _contentRoot;

    public TemporalRiskTrainerImmutablePublishTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "recursor-trainer-tests", Guid.NewGuid().ToString("N"));
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

    // 40 synthetic sessions x 3 horizons, labels cycled across low/medium/high with features
    // nudged by label so training has real (if noisy) signal to fit — enough rows/classes to
    // clear MinTrainableRows and the >=2-distinct-labels gate for every horizon.
    private static List<TemporalRiskTrainingRow> BuildSyntheticRows()
    {
        var rows = new List<TemporalRiskTrainingRow>();
        var labels = new[] { "low", "medium", "high" };
        var rng = new Random(7);

        for (int session = 0; session < 40; session++)
        {
            var label = labels[session % labels.Length];
            var bias = label == "low" ? 0.0 : label == "medium" ? 1.5 : 3.0;

            for (int horizon = 1; horizon <= 3; horizon++)
            {
                var features = new float[25];
                for (int i = 0; i < 25; i++)
                    features[i] = (float)(rng.NextDouble() + bias);

                rows.Add(new TemporalRiskTrainingRow
                {
                    SessionId = $"sess-{session}",
                    UserId = $"user-{session}",
                    SimId = "test-sim",
                    ScenarioId = "test-scenario",
                    SourceWindowIndex = 1,
                    TargetWindowIndex = 1 + horizon,
                    Horizon = horizon,
                    EmbeddingValues = features,
                    TargetNearTermRisk = label,
                    TargetBehaviorState = "",
                });
            }
        }

        return rows;
    }

    private TemporalRiskModelTrainingService BuildTrainer()
    {
        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var config = new ConfigurationBuilder().Build();
        var query = new FakeTrainingQueryService(BuildSyntheticRows());
        return new TemporalRiskModelTrainingService(
            query, config, env, NullLogger<TemporalRiskModelTrainingService>.Instance);
    }

    [Fact]
    public async Task TrainAsync_FirstRunForNewVersion_PublishesAllThreeHorizons()
    {
        var trainer = BuildTrainer();

        var report = await trainer.TrainAsync("temporal-risk-test-v1");

        Assert.True(report.Published, report.PublishError);
        Assert.Equal(3, report.Horizons.Count);
        Assert.All(report.Horizons, h => Assert.Null(h.Error));
        Assert.NotNull(report.PublishedVersionDirectory);
        Assert.True(File.Exists(Path.Combine(report.PublishedVersionDirectory!, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(report.PublishedVersionDirectory!, "h1.zip")));
        Assert.True(File.Exists(Path.Combine(report.PublishedVersionDirectory!, "h2.zip")));
        Assert.True(File.Exists(Path.Combine(report.PublishedVersionDirectory!, "h3.zip")));
    }

    [Fact]
    public async Task TrainAsync_SecondRunForSameVersion_IsRejectedAndDoesNotRetrain()
    {
        var trainer = BuildTrainer();
        var first = await trainer.TrainAsync("temporal-risk-test-v1");
        Assert.True(first.Published, first.PublishError);

        var second = await trainer.TrainAsync("temporal-risk-test-v1");

        Assert.False(second.Published);
        Assert.NotNull(second.PublishError);
        Assert.Equal(0, second.TotalRowsQueried); // rejected before ADX was even queried — no wasted retraining
        Assert.Empty(second.Horizons);
    }

    [Fact]
    public async Task TrainAsync_DifferentVersionLabel_PublishesIndependently()
    {
        var trainer = BuildTrainer();
        var v1 = await trainer.TrainAsync("temporal-risk-test-v1");
        Assert.True(v1.Published, v1.PublishError);

        var v2 = await trainer.TrainAsync("temporal-risk-test-v2");

        Assert.True(v2.Published, v2.PublishError);
        Assert.NotEqual(v1.PublishedVersionDirectory, v2.PublishedVersionDirectory);
    }

    [Fact]
    public async Task TrainAsync_DoesNotTouchLegacyFlatModelPath()
    {
        var trainer = BuildTrainer();

        var report = await trainer.TrainAsync("temporal-risk-test-v1");

        Assert.True(report.Published, report.PublishError);
        // Publishing a version never writes to (and therefore never silently activates) the
        // legacy flat path a running TemporalRiskPredictionService would have already loaded.
        var legacyPath = Path.Combine(_contentRoot, "Recursor", "TrainingModels", "temporal_risk_h1_v1.zip");
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public async Task TrainAsync_OneHorizonBelowMinimumRows_PublishesNothingForAnyHorizon()
    {
        // Horizon 2 gets too few rows to train (below the trainer's minimum-of-10 gate) while
        // horizons 1 and 3 have plenty — this must block publish entirely, not just for H2.
        var allRows = BuildSyntheticRows();
        var sparseRows = allRows.Where(r => r.Horizon != 2).Concat(allRows.Where(r => r.Horizon == 2).Take(3)).ToList();

        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var config = new ConfigurationBuilder().Build();
        var trainer = new TemporalRiskModelTrainingService(
            new FakeTrainingQueryService(sparseRows), config, env,
            NullLogger<TemporalRiskModelTrainingService>.Instance);

        var report = await trainer.TrainAsync("temporal-risk-test-v1");

        Assert.False(report.Published);
        Assert.NotNull(report.PublishError);
        Assert.Null(report.Horizons.Single(h => h.Horizon == 1).Error);
        Assert.NotNull(report.Horizons.Single(h => h.Horizon == 2).Error);
        Assert.Null(report.Horizons.Single(h => h.Horizon == 3).Error);
        Assert.False(ModelVersionPublisher.VersionExists(_contentRoot, TemporalRiskModelTrainingService.ModelFamily, "temporal-risk-test-v1"));
        Assert.False(Directory.Exists(ModelVersionPublisher.GetVersionDirectory(_contentRoot, TemporalRiskModelTrainingService.ModelFamily, "temporal-risk-test-v1")));
    }

    [Fact]
    public async Task TrainAsync_PublishedModelCanBeReloadedAndUsedForInference()
    {
        var trainer = BuildTrainer();
        var report = await trainer.TrainAsync("temporal-risk-test-v1");
        Assert.True(report.Published, report.PublishError);

        var mlContext = new MLContext(seed: 1);
        var h1Path = Path.Combine(report.PublishedVersionDirectory!, "h1.zip");
        var loadedModel = mlContext.Model.Load(h1Path, out _);
        var engine = mlContext.Model.CreatePredictionEngine<TemporalRiskModelInput, TemporalRiskModelOutput>(loadedModel);

        var prediction = engine.Predict(new TemporalRiskModelInput { Features = new float[25] });

        Assert.False(string.IsNullOrEmpty(prediction.PredictedLabel));
        Assert.True(prediction.Score.Length >= 2);
    }
}
