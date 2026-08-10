using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 5 corrective-pass tests: AdxTemporalTrainingQueryService must return structurally
/// valid rows without applying any trainer-specific target-label rule. Before this fix, a row
/// with a valid TargetBehaviorState but a blank TargetNearTermRisk was silently dropped before
/// it ever reached the Phase 10E behavior-state trainer. These tests prove: (1) such a row now
/// reaches and is used by the Phase 10E trainer, (2) the same row is still correctly excluded
/// by the risk / elevated-risk trainers (which validate their own target), and (3) each trainer
/// attributes the exclusion to its own reason.
/// </summary>
public class Stage5TrainerLabelDecouplingTests : IDisposable
{
    private readonly string _contentRoot;

    public Stage5TrainerLabelDecouplingTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "recursor-stage5-tests", Guid.NewGuid().ToString("N"));
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

    // 20 sessions, horizon 1 only, every row has a valid canonical behavior-state label and a
    // BLANK TargetNearTermRisk — exactly the row shape AdxTemporalTrainingQueryService used to
    // silently discard before it reached any trainer.
    private static List<TemporalRiskTrainingRow> BuildRowsWithValidBehaviorStateAndBlankRisk()
    {
        var canonicalLabels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        var rows = new List<TemporalRiskTrainingRow>();
        for (int i = 0; i < 20; i++)
        {
            rows.Add(new TemporalRiskTrainingRow
            {
                SessionId = $"sess-{i}",
                UserId = $"user-{i}",
                SimId = "test-sim",
                ScenarioId = "test-scenario",
                SourceWindowIndex = 1,
                TargetWindowIndex = 2,
                Horizon = 1,
                EmbeddingValues = new float[25],
                TargetNearTermRisk = "", // blank — the bug this stage fixes
                TargetBehaviorState = canonicalLabels[i % canonicalLabels.Length],
            });
        }
        return rows;
    }

    [Fact]
    public async Task PhaseE_BehaviorStateTrainer_UsesRowsWithBlankRiskLabel()
    {
        var rows = BuildRowsWithValidBehaviorStateAndBlankRisk();
        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var trainer = new TemporalBehaviorStateModelTrainingService(
            new FakeTrainingQueryService(rows), new ConfigurationBuilder().Build(), env,
            NullLogger<TemporalBehaviorStateModelTrainingService>.Instance);

        var report = await trainer.TrainAsync("stage5-behavior-state-test-v1");

        var h1 = report.Horizons.Single(h => h.Horizon == 1);
        Assert.Equal(20, h1.RowCountBeforeExclusion);
        // All 20 rows are trainable — a blank risk label must not exclude a row with a valid
        // behavior-state label from this trainer.
        Assert.Equal(20, h1.RowCountTrainable);
        Assert.DoesNotContain("missing-risk-target-label", h1.ExcludedRowCountsByReason.Keys);
    }

    [Fact]
    public async Task RiskTrainer_ExcludesSameRows_ForMissingRiskTargetLabel()
    {
        var rows = BuildRowsWithValidBehaviorStateAndBlankRisk();
        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var trainer = new TemporalRiskModelTrainingService(
            new FakeTrainingQueryService(rows), new ConfigurationBuilder().Build(), env,
            NullLogger<TemporalRiskModelTrainingService>.Instance);

        var report = await trainer.TrainAsync("stage5-risk-test-v1");

        var h1 = report.Horizons.Single(h => h.Horizon == 1);
        Assert.Equal(0, h1.RowCount);
        Assert.Equal(20, h1.ExcludedRowCountsByReason["missing-risk-target-label"]);
    }

    [Fact]
    public async Task ElevatedRiskTrainer_ExcludesSameRows_ForMissingRiskTargetLabel()
    {
        var rows = BuildRowsWithValidBehaviorStateAndBlankRisk();
        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var trainer = new TemporalElevatedRiskModelTrainingService(
            new FakeTrainingQueryService(rows), new ConfigurationBuilder().Build(), env,
            NullLogger<TemporalElevatedRiskModelTrainingService>.Instance);

        var report = await trainer.TrainAsync("stage5-elevated-test-v1");

        var h1 = report.Horizons.Single(h => h.Horizon == 1);
        Assert.Equal(0, h1.RowCount);
        Assert.Equal(20, h1.ExcludedRowCountsByReason["missing-risk-target-label"]);
    }

    [Fact]
    public async Task RiskTrainer_ExcludesRows_ForInvalidEmbeddingLength_UnderItsOwnReason()
    {
        var rows = BuildRowsWithValidBehaviorStateAndBlankRisk();
        foreach (var r in rows) r.TargetNearTermRisk = "low"; // give it a valid risk label...
        rows[0].EmbeddingValues = new float[3]; // ...but corrupt one row's embedding length

        var env = new FakeWebHostEnvironment { ContentRootPath = _contentRoot };
        var trainer = new TemporalRiskModelTrainingService(
            new FakeTrainingQueryService(rows), new ConfigurationBuilder().Build(), env,
            NullLogger<TemporalRiskModelTrainingService>.Instance);

        var report = await trainer.TrainAsync("stage5-risk-embedding-test-v1");

        var h1 = report.Horizons.Single(h => h.Horizon == 1);
        Assert.Equal(1, h1.ExcludedRowCountsByReason["missing-or-invalid-embedding"]);
        Assert.Equal(19, h1.RowCount);
    }
}
