using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10D-1 tests: elevated-risk binary predictor.
/// Covers label conversion, metric helpers, graceful missing-model handling,
/// shadow inference isolation, and dashboard DTO construction.
///
/// All tests are pure unit tests. No ADX, no real model files, no DI container.
/// </summary>
public class Phase10D1ElevatedRiskTests
{
    // ── Label conversion ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("low",    "low")]
    [InlineData("medium", "elevated")]
    [InlineData("high",   "elevated")]
    public void CollapseLabel_MapsCorrectly(string input, string expected)
    {
        var result = TemporalElevatedRiskModelTrainingService.CollapseLabel(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CollapseLabel_EmptyString_ReturnsElevated()
    {
        // Any non-"low" value collapses to "elevated" — unknown labels are treated as elevated.
        var result = TemporalElevatedRiskModelTrainingService.CollapseLabel("");
        Assert.Equal("elevated", result);
    }

    [Fact]
    public void CollapseLabel_UnknownValue_ReturnsElevated()
    {
        var result = TemporalElevatedRiskModelTrainingService.CollapseLabel("unknown_state");
        Assert.Equal("elevated", result);
    }

    [Fact]
    public void CollapseLabel_LowPreservesCase()
    {
        Assert.Equal("low", TemporalElevatedRiskModelTrainingService.CollapseLabel("low"));
    }

    // ── Label distribution after collapse ────────────────────────────────────

    [Fact]
    public void LabelCollapse_MediumAndHighCollapsedToElevated()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, "low"),
            MakeRow(1, "low"),
            MakeRow(1, "medium"),
            MakeRow(1, "high"),
            MakeRow(1, "medium"),
        };

        var collapsed = rows.Select(r => TemporalElevatedRiskModelTrainingService.CollapseLabel(r.TargetNearTermRisk)).ToList();
        var dist = collapsed.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(2, dist["low"]);
        Assert.Equal(3, dist["elevated"]);
        Assert.False(dist.ContainsKey("medium"));
        Assert.False(dist.ContainsKey("high"));
    }

    [Fact]
    public void LabelCollapse_AllLow_ProducesOnlyLow()
    {
        var labels = new[] { "low", "low", "low" }
            .Select(TemporalElevatedRiskModelTrainingService.CollapseLabel)
            .Distinct()
            .ToList();

        Assert.Single(labels);
        Assert.Equal("low", labels[0]);
    }

    [Fact]
    public void LabelCollapse_AllHighOrMedium_ProducesOnlyElevated()
    {
        var labels = new[] { "medium", "high", "high", "medium" }
            .Select(TemporalElevatedRiskModelTrainingService.CollapseLabel)
            .Distinct()
            .ToList();

        Assert.Single(labels);
        Assert.Equal("elevated", labels[0]);
    }

    // ── Binary metrics helper (pure logic) ───────────────────────────────────

    [Fact]
    public void BinaryMetrics_PerfectPrecisionAndRecall()
    {
        // All elevated correctly predicted, no FP/FN.
        var (tp, fp, fn, tn) = ComputeConfusionMatrix(
            actuals:    ["elevated", "elevated", "low", "low"],
            predicted:  ["elevated", "elevated", "low", "low"]);

        Assert.Equal(2, tp);
        Assert.Equal(0, fp);
        Assert.Equal(0, fn);
        Assert.Equal(2, tn);

        double precision = Precision(tp, fp);
        double recall    = Recall(tp, fn);
        double f1        = F1(precision, recall);

        Assert.Equal(1.0, precision, precision: 6);
        Assert.Equal(1.0, recall,    precision: 6);
        Assert.Equal(1.0, f1,        precision: 6);
    }

    [Fact]
    public void BinaryMetrics_AllFalsePositives_PrecisionIsZero()
    {
        var (tp, fp, fn, tn) = ComputeConfusionMatrix(
            actuals:   ["low", "low"],
            predicted: ["elevated", "elevated"]);

        Assert.Equal(0, tp);
        Assert.Equal(2, fp);
        Assert.Equal(0, fn);
        Assert.Equal(0, tn);

        double precision = Precision(tp, fp);
        Assert.Equal(0.0, precision, precision: 6);
    }

    [Fact]
    public void BinaryMetrics_AllFalseNegatives_RecallIsZero()
    {
        var (tp, fp, fn, tn) = ComputeConfusionMatrix(
            actuals:   ["elevated", "elevated"],
            predicted: ["low", "low"]);

        Assert.Equal(0, tp);
        Assert.Equal(0, fp);
        Assert.Equal(2, fn);
        Assert.Equal(0, tn);

        double recall = Recall(tp, fn);
        Assert.Equal(0.0, recall, precision: 6);
    }

    [Fact]
    public void BinaryMetrics_MixedCase_F1Correct()
    {
        // 2 TP, 1 FP, 1 FN → precision=2/3, recall=2/3, F1=2/3.
        var (tp, fp, fn, _) = ComputeConfusionMatrix(
            actuals:   ["elevated", "elevated", "low",      "elevated"],
            predicted: ["elevated", "elevated", "elevated", "low"]);

        Assert.Equal(2, tp);
        Assert.Equal(1, fp);
        Assert.Equal(1, fn);

        double precision = Precision(tp, fp);
        double recall    = Recall(tp, fn);
        double f1        = F1(precision, recall);

        Assert.Equal(2.0 / 3.0, precision, precision: 6);
        Assert.Equal(2.0 / 3.0, recall,    precision: 6);
        Assert.Equal(2.0 / 3.0, f1,        precision: 6);
    }

    [Fact]
    public void BinaryMetrics_Accuracy_CorrectCount()
    {
        var (tp, fp, fn, tn) = ComputeConfusionMatrix(
            actuals:   ["elevated", "low", "elevated", "low"],
            predicted: ["elevated", "low", "low",      "elevated"]);

        int total = tp + fp + fn + tn;
        double accuracy = (double)(tp + tn) / total;

        Assert.Equal(4, total);
        Assert.Equal(0.5, accuracy, precision: 6);
    }

    // ── Model data classes ────────────────────────────────────────────────────

    [Fact]
    public void TemporalElevatedRiskModelOutput_EmptyScore_ConfidenceIsZero()
    {
        var output = new TemporalElevatedRiskModelOutput { Score = [] };
        Assert.Equal(0f, output.Confidence);
    }

    [Fact]
    public void TemporalElevatedRiskModelOutput_NonEmptyScore_ConfidenceIsMax()
    {
        var output = new TemporalElevatedRiskModelOutput
        {
            Score = [0.3f, 0.7f],
            PredictedLabel = "elevated",
        };
        Assert.Equal(0.7f, output.Confidence, precision: 6);
    }

    [Fact]
    public void TemporalElevatedRiskTrainingReport_Warning_IsNotEmpty()
    {
        var report = new TemporalElevatedRiskTrainingReport();
        Assert.False(string.IsNullOrWhiteSpace(report.Warning));
    }

    [Fact]
    public void TemporalElevatedRiskTrainingReport_DefaultHorizons_IsEmptyList()
    {
        var report = new TemporalElevatedRiskTrainingReport();
        Assert.Empty(report.Horizons);
    }

    [Fact]
    public void TemporalElevatedHorizonTrainingSummary_DefaultMetrics_AreNull()
    {
        var summary = new TemporalElevatedHorizonTrainingSummary();
        Assert.Null(summary.Accuracy);
        Assert.Null(summary.ElevatedPrecision);
        Assert.Null(summary.ElevatedRecall);
        Assert.Null(summary.ElevatedF1);
        Assert.Null(summary.FalsePositives);
        Assert.Null(summary.FalseNegatives);
    }

    // ── TemporalElevatedRiskPredictionService: missing-model behaviour ─────────

    [Fact]
    public void ElevatedRiskPredictionService_AllPathsNull_AllHorizonsReturnNull()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            h1ModelPath: null,
            h2ModelPath: null,
            h3ModelPath: null);

        var result = svc.Predict(MakeEmbedding(25));

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void ElevatedRiskPredictionService_PathsMissingOnDisk_AllHorizonsReturnNull()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            h1ModelPath: "/nonexistent/elevated_h1.zip",
            h2ModelPath: "/nonexistent/elevated_h2.zip",
            h3ModelPath: "/nonexistent/elevated_h3.zip");

        var result = svc.Predict(MakeEmbedding(25));

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void ElevatedRiskPredictionService_WrongEmbeddingLength_ReturnsEmptyResult()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null);

        var result = svc.Predict(MakeEmbedding(10));

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    // ── Shadow inference isolation ────────────────────────────────────────────

    [Fact]
    public void ElevatedRiskPredictionService_Predict_DoesNotMutateEmbedding()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null);

        var embedding = MakeEmbedding(25);
        var originalWindow  = embedding.WindowIndex;
        var originalSession = embedding.SessionId;
        var originalLen     = embedding.Values.Length;

        _ = svc.Predict(embedding);

        Assert.Equal(originalWindow,  embedding.WindowIndex);
        Assert.Equal(originalSession, embedding.SessionId);
        Assert.Equal(originalLen,     embedding.Values.Length);
    }

    [Fact]
    public void ElevatedRiskPredictionResult_IsNewInstance_EachCall()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null);

        var embedding = MakeEmbedding(25);
        var r1 = svc.Predict(embedding);
        var r2 = svc.Predict(embedding);

        Assert.NotSame(r1, r2);
    }

    // ── Adaptation isolation (no pipeline state shared) ───────────────────────

    [Fact]
    public void ElevatedRiskPredictionResult_HorizonsAreIndependentlyNullable()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null);

        var result = svc.Predict(MakeEmbedding(25));

        // All null when no models — each horizon is independently null.
        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void ModelVersion_IsExpectedConstant()
    {
        Assert.Equal("temporal-elevated-risk-v1", TemporalElevatedRiskPredictionService.ModelVersion);
    }

    // ── Dashboard DTO ─────────────────────────────────────────────────────────

    [Fact]
    public void TemporalElevatedRiskPredictionTimelineDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new TemporalElevatedRiskPredictionTimelineDto();
        Assert.Equal(0, dto.Horizon);
        Assert.Equal("", dto.PredictedElevatedRisk);
        Assert.Equal(0.0, dto.Confidence);
        Assert.Equal("", dto.ModelVersion);
    }

    [Fact]
    public void WindowTimelineDto_ElevatedRiskPredictions_DefaultsToEmptyList()
    {
        var dto = new WindowTimelineDto();
        Assert.Empty(dto.ElevatedRiskPredictions);
    }

    // ── Two-class training requirement ────────────────────────────────────────

    [Fact]
    public void FilteredRows_TwoDistinctCollapsedLabels_WhenBothPresent()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, "low"),
            MakeRow(1, "medium"),
            MakeRow(1, "high"),
        };

        var collapsed = rows
            .Where(r => r.EmbeddingValues.Length == 25 && !string.IsNullOrEmpty(r.TargetNearTermRisk))
            .Select(r => TemporalElevatedRiskModelTrainingService.CollapseLabel(r.TargetNearTermRisk))
            .Distinct()
            .ToList();

        Assert.Equal(2, collapsed.Count);
        Assert.Contains("low", collapsed);
        Assert.Contains("elevated", collapsed);
    }

    [Fact]
    public void FilteredRows_OnlyLow_SingleCollapsedLabel()
    {
        var rows = new List<TemporalRiskTrainingRow>
        {
            MakeRow(1, "low"),
            MakeRow(1, "low"),
        };

        var collapsed = rows
            .Select(r => TemporalElevatedRiskModelTrainingService.CollapseLabel(r.TargetNearTermRisk))
            .Distinct()
            .ToList();

        Assert.Single(collapsed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TemporalRiskTrainingRow MakeRow(int horizon, string label, int vectorLength = 25) =>
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

    private static (int tp, int fp, int fn, int tn) ComputeConfusionMatrix(
        string[] actuals, string[] predicted)
    {
        int tp = 0, fp = 0, fn = 0, tn = 0;
        for (int i = 0; i < actuals.Length; i++)
        {
            bool a = actuals[i] == "elevated";
            bool p = predicted[i] == "elevated";
            if (a && p)       tp++;
            else if (!a && p) fp++;
            else if (a && !p) fn++;
            else              tn++;
        }
        return (tp, fp, fn, tn);
    }

    private static double Precision(int tp, int fp) =>
        tp + fp > 0 ? (double)tp / (tp + fp) : 0;

    private static double Recall(int tp, int fn) =>
        tp + fn > 0 ? (double)tp / (tp + fn) : 0;

    private static double F1(double precision, double recall) =>
        precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;
}
