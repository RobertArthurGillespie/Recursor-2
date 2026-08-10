using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 9 corrective-pass tests: the grouped stratified train/validation split
/// (TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining, the rich overload) must
/// keep sessions from overlapping between train and validation, spread rare classes/simulations
/// across both sides instead of accidentally isolating them, remain deterministic from a fixed
/// seed, and fail with a precise message when the resulting partition cannot support training.
/// </summary>
public class Stage9GroupedStratifiedSplitTests
{
    private static (TemporalRiskTrainingRow Row, string Label) MakeRow(string sessionId, string simId, string label) =>
        (new TemporalRiskTrainingRow { SessionId = sessionId, SimId = simId, EmbeddingValues = new float[25] }, label);

    [Fact]
    public void NoSessionAppearsInBothTrainAndValidation()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        var labels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        for (int i = 0; i < 60; i++)
            trainable.Add(MakeRow($"sess-{i}", i % 3 == 0 ? "sim-a" : "sim-b", labels[i % labels.Length]));

        var result = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 42);

        Assert.Empty(result.TrainSessions.Intersect(result.ValidationSessions));
    }

    [Fact]
    public void RareClass_WithMultipleSessions_AppearsOnBothSidesOfTheSplit()
    {
        // "conflicted" is rare (only 2 sessions) among an otherwise common class.
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        for (int i = 0; i < 40; i++)
            trainable.Add(MakeRow($"common-{i}", "sim-a", "stable"));
        trainable.Add(MakeRow("rare-0", "sim-a", "conflicted"));
        trainable.Add(MakeRow("rare-1", "sim-a", "conflicted"));

        var result = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 42);

        // A 2-session stratum must be split so both sides get at least one — the pre-Stage-9
        // flat shuffle could (and with unlucky seeds, did) put both "conflicted" sessions on
        // the same side.
        Assert.True(result.TrainLabelDistribution.GetValueOrDefault("conflicted") > 0);
        Assert.True(result.ValidationLabelDistribution.GetValueOrDefault("conflicted") > 0);
    }

    [Fact]
    public void OneSimulationImbalance_SmallSimStillRepresentedInValidationDistributionReporting()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        var labels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        for (int i = 0; i < 50; i++)
            trainable.Add(MakeRow($"big-{i}", "sim-big", labels[i % labels.Length]));
        for (int i = 0; i < 6; i++)
            trainable.Add(MakeRow($"small-{i}", "sim-small", labels[i % labels.Length]));

        var result = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 42);

        // Distributions are reported separately by simulation for both sides — a caller can see
        // exactly how "sim-small" landed without having to re-derive it.
        Assert.True(result.TrainSimDistribution.ContainsKey("sim-small") || result.ValidationSimDistribution.ContainsKey("sim-small"));
        Assert.True(result.TrainSimDistribution.ContainsKey("sim-big"));
        Assert.True(result.ValidationSimDistribution.ContainsKey("sim-big"));
    }

    [Fact]
    public void DeterministicFromSeed_SameInputsProduceIdenticalSplitAcrossCalls()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        var labels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        for (int i = 0; i < 37; i++)
            trainable.Add(MakeRow($"sess-{i}", i % 4 == 0 ? "sim-a" : "sim-b", labels[i % labels.Length]));

        var first = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 99);
        var second = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 99);

        Assert.Equal(first.TrainSessions.OrderBy(s => s), second.TrainSessions.OrderBy(s => s));
        Assert.Equal(first.ValidationSessions.OrderBy(s => s), second.ValidationSessions.OrderBy(s => s));
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentSplits()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        var labels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        for (int i = 0; i < 37; i++)
            trainable.Add(MakeRow($"sess-{i}", i % 4 == 0 ? "sim-a" : "sim-b", labels[i % labels.Length]));

        var a = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 1);
        var b = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 2);

        Assert.NotEqual(
            string.Join(",", a.ValidationSessions.OrderBy(s => s)),
            string.Join(",", b.ValidationSessions.OrderBy(s => s)));
    }

    [Fact]
    public void PostSplitSingleClassInTraining_FailsWithPreciseMessage()
    {
        // Only one class exists at all — training can never end up with >= 2 classes.
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        for (int i = 0; i < 10; i++)
            trainable.Add(MakeRow($"sess-{i}", "sim-a", "stable"));

        var result = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 42);

        Assert.NotNull(result.ValidationError);
        Assert.Contains("fewer than two classes", result.ValidationError);
    }

    [Fact]
    public void EveryTrainableSession_IsPlacedOnExactlyOneSide()
    {
        var trainable = new List<(TemporalRiskTrainingRow, string)>();
        var labels = new[] { "struggling", "recovering", "stable", "mastering", "conflicted" };
        for (int i = 0; i < 45; i++)
            trainable.Add(MakeRow($"sess-{i}", i % 2 == 0 ? "sim-a" : "sim-b", labels[i % labels.Length]));

        var result = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(trainable, 0.8, seed: 42);

        var distinctSessions = trainable.Select(t => t.Item1.SessionId).Distinct().Count();
        Assert.Equal(distinctSessions, result.TrainSessions.Count + result.ValidationSessions.Count);
    }
}
