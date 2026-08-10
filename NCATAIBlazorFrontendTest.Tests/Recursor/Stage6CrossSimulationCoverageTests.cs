using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 6 corrective-pass tests: SplitSessionsForTraining must explicitly report train/
/// validation coverage by class and by simulation, and compute an explicit
/// CrossSimulationValidationStatus rather than letting a caller only infer coverage gaps from
/// the raw distributions. A simulation (or class) present in the trainable data but missing
/// from one side of the split must never be silently interpreted as validated.
///
/// Session-no-leakage and split-determinism are already covered end-to-end by
/// Stage9GroupedStratifiedSplitTests.cs (same underlying method) — this file focuses on the
/// coverage/status fields those tests don't exercise, with one leakage/determinism assertion
/// folded in per scenario as a cheap extra check rather than a full duplicate suite.
/// </summary>
public class Stage6CrossSimulationCoverageTests
{
    private static (TemporalRiskTrainingRow Row, string Label) Row(string sessionId, string simId, string label) =>
        (new TemporalRiskTrainingRow { SessionId = sessionId, SimId = simId, EmbeddingValues = new float[25] }, label);

    // Two sessions per (label, sim) combination for a given sim/label pair — a "multi-session"
    // stratum that SplitSessionsForTraining always splits (never routed through the singleton
    // pool), so it deterministically lands on both sides regardless of trainFraction.
    private static IEnumerable<(TemporalRiskTrainingRow, string)> TwoSessionStratum(
        string simId, string label, string sessionPrefix) =>
        new[]
        {
            Row($"{sessionPrefix}-1", simId, label),
            Row($"{sessionPrefix}-2", simId, label),
        };

    [Fact]
    public void BothSimulations_RepresentedInTrainAndValidation_StatusIsPassed()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "sim-a-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "sim-a-struggling"));
        trainable.AddRange(TwoSessionStratum("medical-supply-stocking", "stable", "medical-stable"));
        trainable.AddRange(TwoSessionStratum("medical-supply-stocking", "struggling", "medical-struggling"));

        var split = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 1);

        Assert.Equal(CrossSimulationValidationStatus.Passed, split.CrossSimulationStatus);
        Assert.Empty(split.SimulationsAbsentFromTraining);
        Assert.Empty(split.SimulationsAbsentFromValidation);
        Assert.Empty(split.ClassesAbsentFromTraining);
        Assert.Empty(split.ClassesAbsentFromValidation);

        // No-leakage sanity check (fully covered by Stage9GroupedStratifiedSplitTests.cs).
        Assert.Empty(split.TrainSessions.Intersect(split.ValidationSessions));
    }

    [Fact]
    public void MedicalSimulation_PresentOnlyInTraining_StatusIsInsufficientCoverage()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "sim-a-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "sim-a-struggling"));
        // A single medical-supply-stocking session forms a size-1 (singleton) stratum. With the
        // default 0.8 train fraction, the singleton pool's trainCount = Round(1 * 0.8) = 1, so
        // this session is placed entirely in train — reproduced deterministically via seed: 1.
        trainable.Add(Row("medical-1", "medical-supply-stocking", "stable"));

        var split = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 1);

        Assert.Contains("medical-supply-stocking", split.TrainSimDistribution.Keys);
        Assert.DoesNotContain("medical-supply-stocking", split.ValidationSimDistribution.Keys);
        Assert.Equal(CrossSimulationValidationStatus.InsufficientCoverage, split.CrossSimulationStatus);
        Assert.Contains("medical-supply-stocking", split.SimulationsAbsentFromValidation);
        Assert.Empty(split.SimulationsAbsentFromTraining);
        Assert.Contains("medical-supply-stocking", split.CrossSimulationStatusReason);
    }

    [Fact]
    public void MedicalSimulation_PresentOnlyInValidation_StatusIsInsufficientCoverage()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "sim-a-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "sim-a-struggling"));
        trainable.Add(Row("medical-1", "medical-supply-stocking", "stable"));

        // A very low train fraction pushes the singleton pool's trainCount = Round(1 * 0.1) = 0,
        // sending the sole medical session to validation instead, while the two-session strata
        // above are unaffected (Clamp keeps them at exactly 1/1 regardless of trainFraction).
        var split = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.1, seed: 1);

        Assert.DoesNotContain("medical-supply-stocking", split.TrainSimDistribution.Keys);
        Assert.Contains("medical-supply-stocking", split.ValidationSimDistribution.Keys);
        Assert.Equal(CrossSimulationValidationStatus.InsufficientCoverage, split.CrossSimulationStatus);
        Assert.Contains("medical-supply-stocking", split.SimulationsAbsentFromTraining);
        Assert.Empty(split.SimulationsAbsentFromValidation);
    }

    [Fact]
    public void SingleSimulation_ReportsNotApplicable_NotInsufficientCoverage()
    {
        // Cross-simulation evaluation genuinely does not apply here — must not be conflated with
        // "insufficient coverage", which specifically means multiple sims exist but are unevenly
        // represented.
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "only-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "only-struggling"));

        var split = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 1);

        Assert.Equal(CrossSimulationValidationStatus.NotApplicable, split.CrossSimulationStatus);
    }

    [Fact]
    public void RareClass_MissingFromValidation_IsReportedIndependentlyOfSimulationCoverage()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "common-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "common-struggling"));
        // "conflicted" is a rare, singleton-only class for this sim — with trainFraction 0.8 it
        // is placed entirely in train (same singleton-pool arithmetic as the medical-sim test).
        trainable.Add(Row("rare-conflicted", "sim-training-v1", "conflicted"));

        var split = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 1);

        Assert.Contains("conflicted", split.ClassesAbsentFromValidation);
        Assert.Empty(split.ClassesAbsentFromTraining);
        // Training is still possible (>= 2 classes trainable) — this must not be conflated with
        // a training-blocking condition.
        Assert.Null(split.ValidationError);
        // Only one simulation is present, so simulation coverage is a separate, unaffected axis.
        Assert.Equal(CrossSimulationValidationStatus.NotApplicable, split.CrossSimulationStatus);
    }

    [Fact]
    public void ComputeCoverage_IsDeterministic_SameInputsProduceIdenticalCoverageResults()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "stable", "sim-a-stable"));
        trainable.AddRange(TwoSessionStratum("sim-training-v1", "struggling", "sim-a-struggling"));
        trainable.Add(Row("medical-1", "medical-supply-stocking", "stable"));

        var first = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 7);
        var second = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, trainFraction: 0.8, seed: 7);

        Assert.Equal(first.CrossSimulationStatus, second.CrossSimulationStatus);
        Assert.Equal(first.CrossSimulationStatusReason, second.CrossSimulationStatusReason);
        Assert.Equal(first.SimulationsAbsentFromTraining, second.SimulationsAbsentFromTraining);
        Assert.Equal(first.SimulationsAbsentFromValidation, second.SimulationsAbsentFromValidation);
        Assert.Equal(first.ClassesAbsentFromTraining, second.ClassesAbsentFromTraining);
        Assert.Equal(first.ClassesAbsentFromValidation, second.ClassesAbsentFromValidation);
        Assert.Equal(first.TrainSessions, second.TrainSessions);
        Assert.Equal(first.ValidationSessions, second.ValidationSessions);
    }
}
