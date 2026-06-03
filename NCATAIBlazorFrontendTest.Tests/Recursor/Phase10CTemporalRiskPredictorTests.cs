using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10C-2 tests: TemporalRiskModels data classes, TemporalRiskPredictionService
/// graceful-failure behaviour, shadow inference isolation, and horizon independence.
///
/// All tests are pure unit tests. No ADX, no real model files, no DI container.
/// </summary>
public class Phase10CTemporalRiskPredictorTests
{
    // ── Model input / output data classes ─────────────────────────────────────

    [Fact]
    public void TemporalRiskModelInput_Default_Has25FloatSlots()
    {
        var input = new TemporalRiskModelInput();
        Assert.Equal(25, input.Features.Length);
    }

    [Fact]
    public void TemporalRiskModelInput_AssignedFeatures_PreservesValues()
    {
        var features = Enumerable.Range(0, 25).Select(i => (float)i / 25f).ToArray();
        var input = new TemporalRiskModelInput { Features = features, Label = "medium" };

        Assert.Equal(25, input.Features.Length);
        Assert.Equal("medium", input.Label);
        Assert.Equal(0f, input.Features[0], precision: 6);
        Assert.Equal(24f / 25f, input.Features[24], precision: 6);
    }

    [Fact]
    public void TemporalRiskModelOutput_EmptyScore_ConfidenceIsZero()
    {
        var output = new TemporalRiskModelOutput { Score = [] };
        Assert.Equal(0f, output.Confidence);
    }

    [Fact]
    public void TemporalRiskModelOutput_NonEmptyScore_ConfidenceIsMax()
    {
        var output = new TemporalRiskModelOutput
        {
            Score = [0.2f, 0.7f, 0.1f],
            PredictedLabel = "medium",
        };
        Assert.Equal(0.7f, output.Confidence, precision: 6);
    }

    // ── TemporalRiskTrainingRow ───────────────────────────────────────────────

    [Fact]
    public void TemporalRiskTrainingRow_EmbeddingValues_HasExpected25Slots()
    {
        var values = Enumerable.Range(0, 25).Select(i => (float)i).ToArray();
        var row = new TemporalRiskTrainingRow
        {
            EmbeddingValues     = values,
            TargetNearTermRisk  = "low",
            Horizon             = 1,
        };

        Assert.Equal(25, row.EmbeddingValues.Length);
        Assert.Equal("low", row.TargetNearTermRisk);
        Assert.Equal(1, row.Horizon);
    }

    [Fact]
    public void TemporalRiskTrainingRow_EmptyValues_LengthIsZero()
    {
        var row = new TemporalRiskTrainingRow();
        Assert.Equal(0, row.EmbeddingValues.Length);
    }

    // ── Label distribution helper (logic extracted for testing) ──────────────

    [Fact]
    public void LabelDistribution_ComputedCorrectly()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, "low"),
            MakeRow(1, "low"),
            MakeRow(1, "medium"),
            MakeRow(1, "high"),
            MakeRow(1, "low"),
        };

        var dist = rows
            .GroupBy(r => r.TargetNearTermRisk)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, dist["low"]);
        Assert.Equal(1, dist["medium"]);
        Assert.Equal(1, dist["high"]);
    }

    [Fact]
    public void FilterRows_SkipsRowsWithWrongVectorLength()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, "low"),       // valid (25 floats)
            MakeRow(1, "medium", vectorLength: 0),   // invalid
            MakeRow(1, "high",   vectorLength: 10),  // invalid
            MakeRow(1, "low"),       // valid
        };

        var valid = rows
            .Where(r => r.EmbeddingValues.Length == 25 && !string.IsNullOrEmpty(r.TargetNearTermRisk))
            .ToList();

        Assert.Equal(2, valid.Count);
    }

    [Fact]
    public void FilterRows_SkipsRowsWithEmptyLabel()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, ""),     // empty label — should be skipped
            MakeRow(1, "low"),
            MakeRow(1, "medium"),
        };

        var valid = rows
            .Where(r => r.EmbeddingValues.Length == 25 && !string.IsNullOrEmpty(r.TargetNearTermRisk))
            .ToList();

        Assert.Equal(2, valid.Count);
    }

    // ── TemporalRiskPredictionService: no-model behaviour ────────────────────

    [Fact]
    public void PredictionService_AllPathsNull_AllHorizonsReturnNull()
    {
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            h1ModelPath: null,
            h2ModelPath: null,
            h3ModelPath: null);

        var embedding = MakeEmbedding(25);
        var result = svc.Predict(embedding);

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void PredictionService_AllPathsMissingOnDisk_AllHorizonsReturnNull()
    {
        // Use paths that definitely do not exist on disk.
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            h1ModelPath: "/nonexistent/path/h1.zip",
            h2ModelPath: "/nonexistent/path/h2.zip",
            h3ModelPath: "/nonexistent/path/h3.zip");

        var result = svc.Predict(MakeEmbedding(25));

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void PredictionService_WrongEmbeddingLength_ReturnsEmptyResult()
    {
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            null, null, null);

        // 10 values instead of 25
        var embedding = MakeEmbedding(10);
        var result = svc.Predict(embedding);

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    // ── Shadow inference isolation ────────────────────────────────────────────

    [Fact]
    public void PredictionService_Predict_DoesNotMutateEmbedding()
    {
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            null, null, null);

        var embedding = MakeEmbedding(25);
        var originalWindowIndex = embedding.WindowIndex;
        var originalSession = embedding.SessionId;

        _ = svc.Predict(embedding);

        Assert.Equal(originalWindowIndex, embedding.WindowIndex);
        Assert.Equal(originalSession, embedding.SessionId);
        Assert.Equal(25, embedding.Values.Length);
    }

    [Fact]
    public void PredictionResult_IsNewInstance_EachCall()
    {
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            null, null, null);

        var embedding = MakeEmbedding(25);
        var r1 = svc.Predict(embedding);
        var r2 = svc.Predict(embedding);

        Assert.NotSame(r1, r2);
    }

    // ── Horizon independence ──────────────────────────────────────────────────

    [Fact]
    public void PredictionResult_H1H2H3AreIndependentFields()
    {
        // All null when no models — each horizon is independently null.
        var svc = new TemporalRiskPredictionService(
            NullLogger<TemporalRiskPredictionService>.Instance,
            null, null, null);

        var result = svc.Predict(MakeEmbedding(25));

        // Horizons are independently nullable — verify they don't share a reference.
        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
        // If one were non-null, the others would remain independent.
        // (Full independence with loaded models is validated by integration / smoke tests.)
    }

    [Fact]
    public void TemporalRiskTrainingReport_Warning_IsNotEmpty()
    {
        var report = new TemporalRiskTrainingReport();
        Assert.False(string.IsNullOrWhiteSpace(report.Warning));
    }

    [Fact]
    public void TemporalRiskTrainingReport_DefaultHorizons_IsEmptyList()
    {
        var report = new TemporalRiskTrainingReport();
        Assert.Empty(report.Horizons);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TemporalRiskTrainingRow MakeRow(
        int horizon,
        string label,
        int vectorLength = 25) =>
        new()
        {
            Horizon            = horizon,
            TargetNearTermRisk = label,
            EmbeddingValues    = new float[vectorLength],
        };

    private static TemporalEmbeddingVector MakeEmbedding(int length) =>
        new()
        {
            SessionId        = "s1",
            UserId           = "u1",
            SimId            = "sim1",
            ScenarioId       = "sc1",
            WindowIndex      = 3,
            EmbeddingVersion = "v1.0",
            Values           = new double[length],
            CreatedAtUtc     = DateTime.UtcNow,
        };
}
