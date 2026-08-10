using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 8 corrective-pass tests: TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice
/// must always report the full canonical 5-class set (struggling/recovering/stable/mastering/
/// conflicted), compute macro F1 over that fixed set, and never drop a class merely because it
/// has zero support or was never predicted in a given slice. These are deliberately pathological
/// cases the pre-fix implementation (classes derived only from what appears in `pairs`) would
/// have gotten wrong — each test asserts the corrected behavior directly.
/// </summary>
public class Stage8MetricsCorrectionTests
{
    private static readonly string[] AllCanonicalClasses =
        { "struggling", "recovering", "stable", "mastering", "conflicted" };

    [Fact]
    public void OneSupportedClassNeverPredicted_HasPrecisionZero_RecallCalculatedNormally_F1Zero()
    {
        // "struggling" has support (appears as Actual) but the model never predicts it.
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("struggling", "stable"),
            ("struggling", "stable"),
            ("stable", "stable"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        var struggling = slice.PerClass.Single(c => c.ClassLabel == "struggling");
        Assert.Equal(2, struggling.SupportCount);
        Assert.Equal(0, struggling.PredictedCount);
        Assert.Equal(0.0, struggling.Precision);
        Assert.Equal(0.0, struggling.Recall); // 0 true positives / 2 support — calculated normally, not undefined
        Assert.Equal(0.0, struggling.F1);
    }

    [Fact]
    public void OneClassAbsentFromValidation_StillAppearsExplicitlyWithZeroSupport()
    {
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("stable", "stable"),
            ("mastering", "mastering"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        Assert.Equal(5, slice.PerClass.Count);
        var conflicted = slice.PerClass.Single(c => c.ClassLabel == "conflicted");
        Assert.Equal(0, conflicted.SupportCount);
        Assert.Equal(0, conflicted.PredictedCount);
        Assert.Equal(0.0, conflicted.F1);
    }

    [Fact]
    public void PredictionsCollapseToMajorityClass_MacroF1PenalizesIgnoredClasses()
    {
        // Model always predicts "stable" regardless of actual — a common degenerate outcome.
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("struggling", "stable"),
            ("recovering", "stable"),
            ("stable", "stable"),
            ("mastering", "stable"),
            ("conflicted", "stable"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        // "stable" gets perfect recall (all rows predicted stable) but poor precision (only 1/5
        // of its predictions were actually stable); every other class gets recall 0.
        var stable = slice.PerClass.Single(c => c.ClassLabel == "stable");
        Assert.Equal(1.0, stable.Recall);
        Assert.Equal(0.2, stable.Precision, precision: 5);

        foreach (var ignored in new[] { "struggling", "recovering", "mastering", "conflicted" })
        {
            var m = slice.PerClass.Single(c => c.ClassLabel == ignored);
            Assert.Equal(0.0, m.Recall);
            Assert.Equal(0.0, m.F1);
        }

        // Macro F1 must be dragged down by the four ignored classes, not flattered by treating
        // this as a 1-class problem.
        Assert.True(slice.MacroF1 < 0.2);
    }

    [Fact]
    public void AllPredictionsIncorrect_MicroAccuracyZero_MacroF1Zero()
    {
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("struggling", "stable"),
            ("stable", "struggling"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        Assert.Equal(0.0, slice.MicroAccuracy);
        Assert.Equal(0.0, slice.MacroF1);
        Assert.Equal(5, slice.PerClass.Count);
    }

    [Fact]
    public void PendingOrInvalidTargets_NeverReachMetricsSlice_ConfusionMatrixHasOnlyCanonicalKeys()
    {
        // ComputeMetricsSlice trusts its caller (ClassifyRow / BehaviorStateLabelPolicy) to have
        // already excluded missing/unknown/invalid rows — this asserts the confusion matrix
        // itself is keyed strictly by the 5 canonical classes, so an invalid literal could never
        // silently become a "legitimate" class in the output even if one slipped through.
        var pairs = new List<(string Actual, string Predicted)> { ("stable", "stable") };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        Assert.Equal(AllCanonicalClasses.OrderBy(c => c).ToArray(), slice.ConfusionMatrix.Keys.OrderBy(k => k).ToArray());
        foreach (var row in slice.ConfusionMatrix.Values)
            Assert.Equal(AllCanonicalClasses.OrderBy(c => c).ToArray(), row.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void UnexpectedNonCanonicalPairIsIgnored_NotCrashOrCorruptMatrix()
    {
        // Defensive: even if a non-canonical value somehow reached this method, it must not
        // throw (KeyNotFoundException) or fabricate a 6th class — it is simply excluded.
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("stable", "stable"),
            ("mastered", "stable"), // non-canonical literal — must not appear as a class
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, AllCanonicalClasses);

        Assert.Equal(5, slice.PerClass.Count);
        Assert.DoesNotContain("mastered", slice.PerClass.Select(c => c.ClassLabel));
    }
}
