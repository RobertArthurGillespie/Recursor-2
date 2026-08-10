using System.Reflection;
using System.Text.Json;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10E tests: shadow-only H1/H2/H3 behavior-state temporal predictor.
/// All tests are pure unit tests. No ADX, no real model files, no DI container.
/// </summary>
public class Phase10EBehaviorStateTemporalPredictorTests
{
    // ── ADX row mapping / serialization ─────────────────────────────────────────

    private static TemporalEmbeddingVector MakeEmbedding() => new()
    {
        SessionId = "sess-1",
        UserId = "user-1",
        SimId = "medical-supply-stocking",
        ScenarioId = "scenario-1",
        WindowIndex = 7,
        SequenceWindowCount = 5,
        Values = new double[25],
        FeatureNamesJson = "[]",
        EmbeddingVersion = "v1.0",
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static TemporalBehaviorStateHorizonPrediction MakePrediction() => new()
    {
        PredictedBehaviorState = "recovering",
        Confidence = 0.72f,
        ClassProbabilities = new Dictionary<string, float>
        {
            ["struggling"] = 0.10f,
            ["recovering"] = 0.72f,
            ["stable"] = 0.10f,
            ["mastering"] = 0.05f,
            ["conflicted"] = 0.03f,
        },
        ModelVersion = "temporal-behavior-state-v1",
    };

    [Fact]
    public void MapTemporalBehaviorStatePrediction_CopiesAllFieldsFromEmbeddingAndPrediction()
    {
        var embedding = MakeEmbedding();
        var prediction = MakePrediction();
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, prediction, horizon: 2, createdAt);

        Assert.Equal(embedding.SessionId, row.SessionId);
        Assert.Equal(embedding.UserId, row.UserId);
        Assert.Equal(embedding.SimId, row.SimId);
        Assert.Equal(embedding.ScenarioId, row.ScenarioId);
        Assert.Equal(embedding.WindowIndex, row.WindowIndex);
        Assert.Equal(2, row.Horizon);
        Assert.Equal(prediction.PredictedBehaviorState, row.PredictedBehaviorState);
        Assert.Equal(prediction.Confidence, row.Confidence);
        Assert.Equal(prediction.ModelVersion, row.ModelVersion);
        Assert.Equal(createdAt, row.CreatedAtUtc);
    }

    [Fact]
    public void MapTemporalBehaviorStatePrediction_ClassProbabilities_RoundTripsAsJson()
    {
        var row = AdxRowMapper.MapTemporalBehaviorStatePrediction(MakeEmbedding(), MakePrediction(), 1, DateTime.UtcNow);

        var decoded = JsonSerializer.Deserialize<Dictionary<string, float>>(row.ClassProbabilities);

        Assert.NotNull(decoded);
        Assert.Equal(5, decoded!.Count);
        Assert.Equal(0.72f, decoded["recovering"]);
        Assert.Equal(0.10f, decoded["struggling"]);
    }

    [Fact]
    public void TemporalBehaviorStatePredictionRow_DefaultConstruction_HasNoNullFields()
    {
        // Column order in AdxIngestionService.BuildTemporalBehaviorStatePredictionsTable is:
        // SessionId, UserId, SimId, ScenarioId, WindowIndex, Horizon, PredictedBehaviorState,
        // Confidence, ClassProbabilities, ModelVersion, CreatedAtUtc. A default-constructed row
        // must not throw when every string property is read (DataTable.Rows.Add would NRE on null).
        var row = new TemporalBehaviorStatePredictionRow();

        Assert.NotNull(row.SessionId);
        Assert.NotNull(row.UserId);
        Assert.NotNull(row.SimId);
        Assert.NotNull(row.ScenarioId);
        Assert.NotNull(row.PredictedBehaviorState);
        Assert.NotNull(row.ClassProbabilities);
        Assert.NotNull(row.ModelVersion);
    }

    // ── Ingestion interface no-op safety ────────────────────────────────────────

    private sealed class NoOpAdxIngestion : IAdxIngestionService
    {
        public bool BehaviorStatePredictionIngested { get; private set; }

        public Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows) => Task.CompletedTask;
        public Task IngestFeatureWindowAsync(FeatureWindowRow row) => Task.CompletedTask;
        public Task IngestBehaviorProfileAsync(BehaviorProfileRow row) => Task.CompletedTask;
        public Task IngestHypothesisSetAsync(HypothesisSetRow row) => Task.CompletedTask;
        public Task IngestAdaptationDecisionAsync(AdaptationDecisionRow row) => Task.CompletedTask;
        public Task IngestBehaviorStateTrainingRowAsync(BehaviorStateTrainingRow row) => Task.CompletedTask;
        public Task IngestUserBehaviorProfileAsync(UserBehaviorProfileRow row) => Task.CompletedTask;
        public Task IngestUserBehaviorProfileUpdateAsync(UserBehaviorProfileUpdateRow row) => Task.CompletedTask;
        public Task IngestAdaptationEffectivenessAsync(AdaptationEffectivenessRow row) => Task.CompletedTask;
        public Task IngestTemporalEmbeddingAsync(TemporalEmbeddingRow row) => Task.CompletedTask;
        public Task IngestTemporalPredictionTargetAsync(TemporalPredictionTargetRow row) => Task.CompletedTask;
        public Task IngestTemporalRiskPredictionAsync(TemporalRiskPredictionRow row) => Task.CompletedTask;
        public Task IngestTemporalElevatedRiskPredictionAsync(TemporalElevatedRiskPredictionRow row) => Task.CompletedTask;

        public Task IngestTemporalBehaviorStatePredictionAsync(TemporalBehaviorStatePredictionRow row)
        {
            BehaviorStatePredictionIngested = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NoOpAdxIngestion_IngestTemporalBehaviorStatePrediction_CompletesWithoutThrowing()
    {
        var ingestion = new NoOpAdxIngestion();
        var row = AdxRowMapper.MapTemporalBehaviorStatePrediction(MakeEmbedding(), MakePrediction(), 1, DateTime.UtcNow);

        await ingestion.IngestTemporalBehaviorStatePredictionAsync(row);

        Assert.True(ingestion.BehaviorStatePredictionIngested);
    }

    // ── Row exclusion / label-policy application ────────────────────────────────

    private static TemporalRiskTrainingRow MakeTrainingRow(
        string sessionId = "sess-1", string simId = "medical-supply-stocking",
        string targetBehaviorState = "stable", int embeddingLength = 25) => new()
    {
        SessionId = sessionId,
        UserId = "user-1",
        SimId = simId,
        ScenarioId = "scenario-1",
        SourceWindowIndex = 1,
        TargetWindowIndex = 2,
        Horizon = 1,
        EmbeddingValues = new float[embeddingLength],
        TargetNearTermRisk = "low",
        TargetBehaviorState = targetBehaviorState,
    };

    [Theory]
    [InlineData("struggling")]
    [InlineData("recovering")]
    [InlineData("stable")]
    [InlineData("mastering")]
    [InlineData("conflicted")]
    public void ClassifyRow_CanonicalLabel_IsTrainable(string label)
    {
        var result = TemporalBehaviorStateModelTrainingService.ClassifyRow(MakeTrainingRow(targetBehaviorState: label));

        Assert.True(result.Trainable);
        Assert.Equal(label, result.CanonicalLabel);
        Assert.Null(result.ExclusionReason);
    }

    [Fact]
    public void ClassifyRow_BlankLabel_ExcludedAsMissingTarget()
    {
        var result = TemporalBehaviorStateModelTrainingService.ClassifyRow(MakeTrainingRow(targetBehaviorState: ""));

        Assert.False(result.Trainable);
        Assert.Null(result.CanonicalLabel);
        Assert.Equal("missing-target-label", result.ExclusionReason);
    }

    [Fact]
    public void ClassifyRow_UnknownLabel_ExcludedAsNoSignalNotFabricatedStable()
    {
        var result = TemporalBehaviorStateModelTrainingService.ClassifyRow(MakeTrainingRow(targetBehaviorState: "unknown"));

        Assert.False(result.Trainable);
        Assert.Null(result.CanonicalLabel);
        Assert.Equal("unknown-no-signal", result.ExclusionReason);
    }

    [Fact]
    public void ClassifyRow_InvalidLiteral_ExcludedAsInvalidLabel()
    {
        var result = TemporalBehaviorStateModelTrainingService.ClassifyRow(MakeTrainingRow(targetBehaviorState: "garbled"));

        Assert.False(result.Trainable);
        Assert.Equal("invalid-label-literal", result.ExclusionReason);
    }

    [Fact]
    public void ClassifyRow_WrongEmbeddingLength_ExcludedBeforeLabelCheck()
    {
        var result = TemporalBehaviorStateModelTrainingService.ClassifyRow(
            MakeTrainingRow(targetBehaviorState: "stable", embeddingLength: 10));

        Assert.False(result.Trainable);
        Assert.Equal("missing-or-invalid-embedding", result.ExclusionReason);
    }

    // ── Session-level train/validation split ─────────────────────────────────────

    [Fact]
    public void SplitSessionsForTraining_NoSessionAppearsInBothSets()
    {
        var sessions = Enumerable.Range(1, 20).Select(i => $"sess-{i}").ToList();

        var (train, validation) = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(
            sessions, trainFraction: 0.8, seed: 42);

        Assert.Empty(train.Intersect(validation));
        Assert.Equal(sessions.Count, train.Count + validation.Count);
        Assert.NotEmpty(validation);
    }

    [Fact]
    public void SplitSessionsForTraining_TwoSessions_AlwaysHoldsOneOutForValidation()
    {
        var (train, validation) = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(
            ["sess-a", "sess-b"], trainFraction: 0.8, seed: 42);

        Assert.Single(train);
        Assert.Single(validation);
    }

    [Fact]
    public void SplitSessionsForTraining_IsDeterministicForSameSeed()
    {
        var sessions = Enumerable.Range(1, 15).Select(i => $"sess-{i}").ToList();

        var (train1, validation1) = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(sessions, 0.8, 42);
        var (train2, validation2) = TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining(sessions, 0.8, 42);

        Assert.Equal(train1, train2);
        Assert.Equal(validation1, validation2);
    }

    // ── Multiclass metrics computation ───────────────────────────────────────────

    [Fact]
    public void ComputeMetricsSlice_PerfectPredictions_YieldsAccuracyOne()
    {
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("stable", "stable"),
            ("struggling", "struggling"),
            ("mastering", "mastering"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice(
            "all", pairs, new[] { "stable", "struggling", "mastering" });

        Assert.Equal(1.0, slice.MicroAccuracy);
        // Stage 8: macro F1 is always averaged over the full canonical 5-class set, not just the
        // 3 classes this slice happens to contain — "recovering" and "conflicted" never appear
        // (zero support, zero predictions) and correctly contribute F1=0 each, so a slice that is
        // "perfect" on only 3-of-5 classes must NOT report macro F1 = 1.0.
        Assert.Equal(5, slice.PerClass.Count);
        Assert.Equal((1.0 + 1.0 + 1.0 + 0.0 + 0.0) / 5.0, slice.MacroF1, precision: 5);
        foreach (var seenClass in new[] { "stable", "struggling", "mastering" })
        {
            var m = slice.PerClass.Single(c => c.ClassLabel == seenClass);
            Assert.Equal(1.0, m.Precision);
            Assert.Equal(1.0, m.Recall);
            Assert.Equal(1.0, m.F1);
        }
        foreach (var unseenClass in new[] { "recovering", "conflicted" })
        {
            var m = slice.PerClass.Single(c => c.ClassLabel == unseenClass);
            Assert.Equal(0, m.SupportCount);
            Assert.Equal(0, m.PredictedCount);
            Assert.Equal(0.0, m.Precision);
            Assert.Equal(0.0, m.Recall);
            Assert.Equal(0.0, m.F1);
        }
    }

    [Fact]
    public void ComputeMetricsSlice_MajorityBaseline_ReflectsTrainingDistributionNotSliceDistribution()
    {
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("stable", "stable"),
            ("struggling", "stable"),
        };
        // Training set is overwhelmingly "struggling" even though this validation slice is not.
        var trainingLabels = Enumerable.Repeat("struggling", 9).Concat(Enumerable.Repeat("stable", 1));

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice("all", pairs, trainingLabels);

        Assert.Equal("struggling", slice.MajorityClass);
        // Only 1 of 2 validation rows has Actual == "struggling".
        Assert.Equal(0.5, slice.MajorityBaselineAccuracy);
    }

    [Fact]
    public void ComputeMetricsSlice_ConfusionMatrix_CountsEachActualPredictedPair()
    {
        var pairs = new List<(string Actual, string Predicted)>
        {
            ("stable", "stable"),
            ("stable", "struggling"),
            ("struggling", "struggling"),
        };

        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice(
            "all", pairs, new[] { "stable", "struggling" });

        Assert.Equal(1, slice.ConfusionMatrix["stable"]["stable"]);
        Assert.Equal(1, slice.ConfusionMatrix["stable"]["struggling"]);
        Assert.Equal(1, slice.ConfusionMatrix["struggling"]["struggling"]);
        Assert.Equal(0, slice.ConfusionMatrix["struggling"]["stable"]);
    }

    [Fact]
    public void ComputeMetricsSlice_EmptyPairs_ReturnsZeroedSliceWithAllFiveCanonicalClasses()
    {
        var slice = TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice(
            "all", new List<(string Actual, string Predicted)>(), []);

        Assert.Equal(0, slice.RowCount);
        Assert.Equal(0.0, slice.MicroAccuracy);
        Assert.Equal(0.0, slice.MacroF1);
        // Stage 8: even a completely empty evaluation slice must still explicitly report all
        // five canonical classes (each with zero support/predicted/metrics), never an empty list.
        Assert.Equal(5, slice.PerClass.Count);
        Assert.All(slice.PerClass, c =>
        {
            Assert.Equal(0, c.SupportCount);
            Assert.Equal(0, c.PredictedCount);
            Assert.Equal(0.0, c.Precision);
            Assert.Equal(0.0, c.Recall);
            Assert.Equal(0.0, c.F1);
        });
    }

    // ── Versioned artifact naming ──────────────────────────────────────────────

    [Fact]
    public void ExtractVersionSuffix_MatchesElevatedRiskConvention()
    {
        Assert.Equal("v2", TemporalBehaviorStateModelTrainingService.ExtractVersionSuffix("temporal-behavior-state-v2"));
        Assert.Equal("v1", TemporalBehaviorStateModelTrainingService.ExtractVersionSuffix(""));
    }

    [Fact]
    public void ResolveVersionedModelPath_GeneratesExpectedFileName()
    {
        // Stage 3 (corrective pass): with no explicit override, resolution must match the
        // immutable directory layout ModelVersionPublisher actually writes to
        // (versions/{family}/{version}/h{n}.zip), not a flat legacy filename.
        var path = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(
            2, "temporal-behavior-state-v3", @"C:\app", null);

        var expected = Path.Combine(
            ModelVersionPublisher.GetVersionDirectory(@"C:\app", TemporalBehaviorStateModelTrainingService.ModelFamily, "temporal-behavior-state-v3"),
            "h2.zip");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void ResolveVersionedModelPath_PreservesMatchingExplicitConfigPath()
    {
        var explicitPath = "Recursor/TrainingModels/temporal_behavior_state_h1_v1.zip";

        var path = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-behavior-state-v1", @"C:\app", explicitPath);

        Assert.Equal(Path.Combine(@"C:\app", explicitPath), path);
    }

    // ── Runtime prediction service: graceful missing-model handling ─────────────

    [Fact]
    public void PredictionService_NoModelsConfigured_ReturnsAllNullHorizonsWithoutThrowing()
    {
        var service = new TemporalBehaviorStatePredictionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalBehaviorStatePredictionService>.Instance,
            h1ModelPath: null, h2ModelPath: null, h3ModelPath: null);

        var result = service.Predict(MakeEmbedding());

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void PredictionService_ModelPathDoesNotExist_ReturnsNullHorizonWithoutThrowing()
    {
        var service = new TemporalBehaviorStatePredictionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalBehaviorStatePredictionService>.Instance,
            h1ModelPath: @"C:\nonexistent\model.zip", h2ModelPath: null, h3ModelPath: null);

        var result = service.Predict(MakeEmbedding());

        Assert.Null(result.Horizon1);
    }

    [Fact]
    public void PredictionService_WrongEmbeddingLength_ReturnsEmptyResultWithoutThrowing()
    {
        var service = new TemporalBehaviorStatePredictionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalBehaviorStatePredictionService>.Instance,
            h1ModelPath: null, h2ModelPath: null, h3ModelPath: null);

        var badEmbedding = MakeEmbedding();
        badEmbedding.Values = new double[10];

        var result = service.Predict(badEmbedding);

        Assert.Null(result.Horizon1);
        Assert.Null(result.Horizon2);
        Assert.Null(result.Horizon3);
    }

    [Fact]
    public void PredictionService_DefaultModelVersion_MatchesConstant()
    {
        var service = new TemporalBehaviorStatePredictionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalBehaviorStatePredictionService>.Instance,
            h1ModelPath: null, h2ModelPath: null, h3ModelPath: null);

        Assert.Equal("temporal-behavior-state-v1", service.LoadedModelVersion);
        Assert.Equal(TemporalBehaviorStatePredictionService.ModelVersion, service.LoadedModelVersion);
    }

    // ── Adaptation isolation ──────────────────────────────────────────────────

    [Fact]
    public void TemporalBehaviorStateHorizonPrediction_HasNoAdaptationRelatedMembers()
    {
        // Reflection guard: this shadow-only prediction type must never grow members that
        // look like they participate in adaptation, guardrail suppression, or policy
        // selection. If this test fails, someone wired the prediction into a live decision path.
        var type = typeof(TemporalBehaviorStateHorizonPrediction);
        var forbiddenSubstrings = new[]
        {
            "ParameterChange", "Adaptation", "Guardrail", "Suppress", "Policy", "InterventionFamil"
        };

        foreach (var member in type.GetMembers())
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.False(
                    member.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Member '{member.Name}' on {type.Name} contains forbidden substring '{forbidden}'.");
            }
        }
    }

    [Fact]
    public void ITemporalBehaviorStatePredictionService_HasNoAdaptationRelatedMembers()
    {
        var type = typeof(ITemporalBehaviorStatePredictionService);
        var forbiddenSubstrings = new[] { "Adapt", "Guardrail", "Suppress", "Policy" };

        foreach (var member in type.GetMembers())
        {
            foreach (var forbidden in forbiddenSubstrings)
            {
                Assert.False(
                    member.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Member '{member.Name}' on {type.Name} contains forbidden substring '{forbidden}'.");
            }
        }
    }

    // ── Structural proof that adaptation policy cannot consume Phase 10E predictions ─────
    //
    // RecursorIngestionService has ~20 constructor dependencies and is never directly
    // instantiated in this test project (no fake exists for the full dependency graph), so a
    // literal "run the pipeline twice, diff the AdaptationDecisionDocument" regression test is
    // impractical here — the same limitation applies to the Phase 10D-1 elevated-risk shadow
    // predictor, which has no such test either. Instead these tests prove the isolation
    // structurally: IAdaptationPolicyService.ApplyPolicy's entire parameter surface is
    // reflected and asserted to contain no Phase 10E type, meaning the policy layer has no
    // compile-time path to observe a behavior-state prediction at all.

    [Fact]
    public void ApplyPolicy_ParameterTypes_ContainNoPhase10ETemporalBehaviorStateType()
    {
        var method = typeof(IAdaptationPolicyService).GetMethod(nameof(IAdaptationPolicyService.ApplyPolicy))!;

        foreach (var parameter in method.GetParameters())
        {
            Assert.False(
                parameter.ParameterType.Namespace?.Contains("Recursor.ML") == true,
                $"ApplyPolicy parameter '{parameter.Name}' ({parameter.ParameterType}) lives in the ML namespace — " +
                "adaptation policy must not depend on any temporal prediction service type.");
            // Note: ApplyPolicy legitimately takes a pre-existing Phase 7A/7B `BehaviorStatePrediction?`
            // shadow parameter, so the check below is scoped to the Phase 10E "TemporalBehaviorState"
            // prefix specifically rather than the generic "BehaviorState" substring.
            Assert.DoesNotContain("TemporalBehaviorState", parameter.ParameterType.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdaptationDecisionDocument_And_ParameterChange_HaveNoBehaviorStatePredictionFields()
    {
        foreach (var type in new[] { typeof(AdaptationDecisionDocument), typeof(ParameterChange) })
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.False(
                    prop.PropertyType == typeof(TemporalBehaviorStatePredictionResult) ||
                    prop.PropertyType == typeof(TemporalBehaviorStateHorizonPrediction),
                    $"{type.Name}.{prop.Name} exposes a Phase 10E prediction type — this must remain shadow-only.");
            }
        }
    }

    [Fact]
    public void RecursorPoliciesOptions_TemporalBehaviorStatePrediction_DefaultsToDisabled()
    {
        var options = new RecursorPoliciesOptions();

        Assert.False(options.EnableTemporalBehaviorStatePrediction);
    }

    [Fact]
    public void SessionDocument_CompletedBehaviorStatePredictionKeys_DefaultsToEmpty()
    {
        var session = new SessionDocument();

        Assert.Empty(session.CompletedBehaviorStatePredictionKeys);
    }

    [Fact]
    public void BehaviorStatePredictionKeys_DistinguishesWindowHorizonAndModelVersion()
    {
        var k1 = BehaviorStatePredictionKeys.For(windowIndex: 5, horizon: 1, modelVersion: "temporal-behavior-state-v1");
        var k2 = BehaviorStatePredictionKeys.For(windowIndex: 5, horizon: 2, modelVersion: "temporal-behavior-state-v1");
        var k3 = BehaviorStatePredictionKeys.For(windowIndex: 6, horizon: 1, modelVersion: "temporal-behavior-state-v1");
        var k4 = BehaviorStatePredictionKeys.For(windowIndex: 5, horizon: 1, modelVersion: "temporal-behavior-state-v2");

        var keys = new[] { k1, k2, k3, k4 };
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void DuplicatePredictionGuard_SameLogicalKey_SkipsReprocessing()
    {
        // Mirrors the per-horizon guard in RecursorIngestionService.ProcessBehaviorStatePredictionAsync:
        // a horizon is only (re-)ingested when its logical key is not already recorded as completed.
        var session = new SessionDocument();
        var key = BehaviorStatePredictionKeys.For(5, 1, "temporal-behavior-state-v1");
        session.CompletedBehaviorStatePredictionKeys.Add(key);

        bool shouldIngestSameKey = !session.CompletedBehaviorStatePredictionKeys.Contains(key);
        bool shouldIngestDifferentHorizon =
            !session.CompletedBehaviorStatePredictionKeys.Contains(BehaviorStatePredictionKeys.For(5, 2, "temporal-behavior-state-v1"));
        bool shouldIngestDifferentVersion =
            !session.CompletedBehaviorStatePredictionKeys.Contains(BehaviorStatePredictionKeys.For(5, 1, "temporal-behavior-state-v2"));

        Assert.False(shouldIngestSameKey);
        Assert.True(shouldIngestDifferentHorizon);
        Assert.True(shouldIngestDifferentVersion);
    }

    // ── Dashboard: correctness computation ──────────────────────────────────────

    private static TemporalBehaviorStatePredictionTimelineDto MakePredictionDto(
        int horizon, string predictedState) => new()
    {
        Horizon = horizon,
        PredictedBehaviorState = predictedState,
        Confidence = 0.8,
        ModelVersion = "temporal-behavior-state-v1",
    };

    private static TemporalTargetTimelineDto MakeTargetDto(int horizon, string targetState) => new()
    {
        Horizon = horizon,
        TargetWindowIndex = 1,
        TargetBehaviorState = targetState,
    };

    [Fact]
    public void ComputeBehaviorStateCorrectness_MatchingPrediction_ReturnsTrue()
    {
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "stable") };
        var targets = new List<TemporalTargetTimelineDto> { MakeTargetDto(1, "stable") };

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 1);

        Assert.True(result);
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_MismatchedPrediction_ReturnsFalse()
    {
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "struggling") };
        var targets = new List<TemporalTargetTimelineDto> { MakeTargetDto(1, "stable") };

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 1);

        Assert.False(result);
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_NoTargetYet_ReturnsNullNotIncorrect()
    {
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "stable") };
        var targets = new List<TemporalTargetTimelineDto>();

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 1);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_UnknownTarget_ReturnsNullNotIncorrect()
    {
        // "unknown" is a legitimate no-signal outcome (BehaviorStateLabelPolicy), never a
        // wrong-prediction penalty against the model.
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "stable") };
        var targets = new List<TemporalTargetTimelineDto> { MakeTargetDto(1, "unknown") };

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 1);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_BlankTarget_ReturnsNullNotIncorrect()
    {
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "stable") };
        var targets = new List<TemporalTargetTimelineDto> { MakeTargetDto(1, "") };

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 1);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_WrongHorizon_ReturnsNull()
    {
        var preds = new List<TemporalBehaviorStatePredictionTimelineDto> { MakePredictionDto(1, "stable") };
        var targets = new List<TemporalTargetTimelineDto> { MakeTargetDto(1, "stable") };

        var result = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(preds, targets, 2);

        Assert.Null(result);
    }

    [Fact]
    public void Build_BehaviorStatePredictionsGroupedByWindowAndCorrectnessComputed()
    {
        var rows = new List<DashboardTrainingRowResult>
        {
            new("s1", "u1", "medical-supply-stocking", "scenario-1", 0, DateTime.UtcNow,
                0.4, 0.6, 0.3, "stable", "none", 3, 0.1, 0.1, 0.8, "low"),
        };
        var behaviorStatePreds = new List<DashboardBehaviorStatePredictionResult>
        {
            new(WindowIndex: 0, Horizon: 1, PredictedBehaviorState: "stable", Confidence: 0.9, ModelVersion: "v1"),
        };
        var targets = new List<DashboardTargetResult>
        {
            new(SourceWindowIndex: 0, TargetWindowIndex: 1, Horizon: 1,
                TargetNearTermRisk: "low", TargetBehaviorState: "stable",
                TargetConfusionScore: 0.3, TargetGoalUnderstanding: 0.6, TargetHintDependenceScore: 0.2),
        };

        var result = DashboardTimelineBuilder.Build("s1", rows, [], [], [], behaviorStatePreds, targets);

        Assert.Single(result.Windows[0].BehaviorStatePredictions);
        Assert.Equal("stable", result.Windows[0].BehaviorStatePredictions[0].PredictedBehaviorState);
        Assert.True(result.Windows[0].H1BehaviorStateCorrect);
        Assert.Equal(1.0, result.H1BehaviorStateAccuracy!.Value, precision: 5);
        Assert.Null(result.H2BehaviorStateAccuracy);
    }

    // ── Dashboard: model-comparison / coverage KQL structure ─────────────────────

    [Fact]
    public void BuildBehaviorStateModelComparisonKql_ReferencesCorrectTablesAndExcludesUnknownTargets()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql();

        Assert.Contains("TemporalBehaviorStatePredictions", kql);
        Assert.Contains("TemporalPredictionTargets", kql);
        Assert.Contains("\"unknown\"", kql);
        Assert.Contains("ClassLabel", kql);
    }

    [Fact]
    public void BuildBehaviorStateCoverageKql_DistinguishesPendingFromInvalidTarget()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateCoverageKql();

        Assert.Contains("\"pending\"", kql);
        Assert.Contains("\"invalid-target\"", kql);
        Assert.Contains("\"resolved\"", kql);
    }
}
