using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 9 corrective-pass test: the real, fully-wired Recursor adaptation pipeline
/// (RecursorIngestionService.ProcessBatchAsync — not a reflection-based structural proxy) must
/// produce byte-for-byte identical adaptation output whether or not Phase 10E shadow
/// behavior-state prediction (Recursor:Policies:EnableTemporalBehaviorStatePrediction) is
/// enabled. This is deliberately NOT the reflection-only isolation test already present in
/// Phase10EBehaviorStateTemporalPredictorTests.cs (whose own doc-comment explains why a literal
/// pipeline run was previously judged impractical, given RecursorIngestionService's ~23
/// constructor dependencies) — this test builds the real service graph and exercises it twice
/// with identical inputs, using a deterministic fake ITemporalBehaviorStatePredictionService
/// that actually returns non-null predictions in the "enabled" run, so the comparison proves
/// something even when the shadow predictor has real opinions to persist.
///
/// Every dependency here is either the real production class (used with benign/no-model
/// inputs, matching how Program.cs wires them when no ML model file is configured) or a tiny
/// local no-op/spy fake — no shared test harness for RecursorIngestionService exists elsewhere
/// in this repo (grep for "new RecursorIngestionService(" outside Program.cs returns nothing).
/// </summary>
public class Stage9ShadowOnlyAdaptationRegressionTests
{
    private const string SessionId = "sess-shadow-regression";
    private const string UserId = "user-shadow-regression";
    private const string SimId = "medical-supply-stocking";
    private const string ScenarioId = "basic-supply-room";
    private static readonly DateTime FixedNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── Fakes/spies — the two things this test actually needs to observe/control ──────────

    private sealed class FakeTemporalBehaviorStatePredictionService : ITemporalBehaviorStatePredictionService
    {
        public int CallCount { get; private set; }

        public TemporalBehaviorStatePredictionResult Predict(TemporalEmbeddingVector embedding)
        {
            CallCount++;
            // Deliberately a strong, opinionated prediction (not a null/no-op) — proves the
            // adaptation pipeline is unaffected even when the shadow predictor has something
            // to say and something is actually persisted, not merely that a null no-op is inert.
            var prediction = new TemporalBehaviorStateHorizonPrediction
            {
                PredictedBehaviorState = "conflicted",
                Confidence = 0.91f,
                ClassProbabilities = new Dictionary<string, float> { ["conflicted"] = 0.91f },
                ModelVersion = "shadow-regression-test-v1",
            };
            return new TemporalBehaviorStatePredictionResult
            {
                Horizon1 = prediction,
                Horizon2 = prediction,
                Horizon3 = prediction,
            };
        }
    }

    // No-op ADX ingestion that also counts behavior-state-prediction ingests, so the test can
    // prove the flag actually changed what got persisted (not just that it was called).
    private sealed class SpyAdxIngestion : IAdxIngestionService
    {
        public int TemporalBehaviorStatePredictionIngestCount { get; private set; }

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
            TemporalBehaviorStatePredictionIngestCount++;
            return Task.CompletedTask;
        }
    }

    // Never invoked with default policy flags (personalization rules are all off by default),
    // but RecursorIngestionService still needs a concrete instance to construct.
    private sealed class NoOpUserRelativeSignalService : IUserRelativeSignalService
    {
        public Task<UserRelativeSignals?> GetUserRelativeSignalsAsync(string userId) =>
            Task.FromResult<UserRelativeSignals?>(null);
    }

    // ── Real pipeline construction ──────────────────────────────────────────────────────

    private static RecursorIngestionService BuildService(
        bool enableTemporalBehaviorStatePrediction,
        ITemporalBehaviorStatePredictionService temporalBehaviorStatePrediction,
        IAdxIngestionService adxIngestion)
    {
        // Same options object shape used for both AdaptationPolicyService and
        // RecursorIngestionService (mirrors Program.cs binding both from the same
        // "Recursor:Policies" config section) — every flag except the one under test uses its
        // real default, so this is the ONLY difference between the two pipeline runs.
        var policies = Options.Create(new RecursorPoliciesOptions
        {
            EnableTemporalBehaviorStatePrediction = enableTemporalBehaviorStatePrediction,
        });

        // No Azure OpenAI config — GenerateExplanationAsync catches this internally and
        // returns null (verified in Stage 2 rework of this class), never a network call.
        var emptyConfig = new ConfigurationBuilder().Build();

        var adapterFactory = new SimEventInterpretationAdapterFactory(
            new MedicalSupplyEventInterpretationAdapter(), new DefaultSimEventInterpretationAdapter());
        var profileRepository = new InMemoryUserProfileRepository();

        return new RecursorIngestionService(
            adxIngestion,
            new FeatureExtractionService(adapterFactory),
            new BehaviorInterpreter(new BehaviorScoringService()),
            new AdaptationPolicyService(policies, NullLogger<AdaptationPolicyService>.Instance),
            new AzureOpenAiExplanationService(NullLogger<AzureOpenAiExplanationService>.Instance, emptyConfig),
            new SessionRepository(),
            new SimCatalogRepository(),
            new TrajectoryAnalysisService(),
            new BehaviorStateFeatureVectorBuilder(),
            new ShadowBehaviorStatePredictionService(),
            new NoOpUserRelativeSignalService(),
            new InMemoryUserThresholdRepository(),
            profileRepository,
            new UserProfileUpdateService(
                profileRepository, adxIngestion,
                new UserThresholdDerivationService(NullLogger<UserThresholdDerivationService>.Instance),
                NullLogger<UserProfileUpdateService>.Instance),
            new MultiSignalGuardrailService(NullLogger<MultiSignalGuardrailService>.Instance),
            new Phase8AGuardrailModifierService(NullLogger<Phase8AGuardrailModifierService>.Instance),
            new AdaptationEffectivenessService(),
            new PolicyReliabilityWeightingService(
                Options.Create(new RecursorPolicyReliabilityOptions()),
                NullLogger<PolicyReliabilityWeightingService>.Instance),
            new SequenceFeatureExtractor(),
            new TemporalEmbeddingService(),
            new TemporalRiskPredictionService(NullLogger<TemporalRiskPredictionService>.Instance, null, null, null),
            new TemporalElevatedRiskPredictionService(NullLogger<TemporalElevatedRiskPredictionService>.Instance, null, null, null),
            temporalBehaviorStatePrediction,
            policies,
            NullLogger<RecursorIngestionService>.Instance);
    }

    private static SessionDocument NewSession() => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        SimId = SimId,
        ScenarioId = ScenarioId,
        Status = "active",
        CurrentDifficultyProfile = new Dictionary<string, string>(),
    };

    // ── Synthetic "struggling" medical-supply batch — drives a rich, real hypothesis set ──
    //
    // 6x DamagedSupplyStocked (safety error, zero correct rejections) -> SafetyCompliance=0.0,
    // contributes to AttentionDetection=0.0 too. 4x WrongBinError -> GoalUnderstanding=0.0,
    // ProcedureSequencing=0.0. Through BehaviorScoringService's formulas this drives
    // ConfusionScore/ImpulsivityScore/HintDependenceScore all well past their pattern
    // thresholds, so BehaviorInterpreter.BuildHypothesisSet fires the full compound set:
    // safety-risk, attention-deficit, goal-confusion, sequencing-difficulty, confusion_pattern,
    // impulsivity_pattern, hint_dependence_pattern, compound_confusion_hint_dependence, and
    // learner-overload (>=2 concurrent hypotheses) — the same realistic label set
    // Phase10S3MedicalSupplyAdaptationPolicyTests.cs hand-constructs for its
    // "StrugglingSession_WithCompoundHypothesisSet_ProducesAdaptation" test, except derived
    // here from real raw events through the real FeatureExtractionService/BehaviorInterpreter
    // chain, not hand-built. A trailing ScenarioCompleted event forces feature-window
    // extraction (MedicalSupplyEventInterpretationAdapter.ShouldForceWindow).
    private static RawEventBatch BuildStrugglingBatch()
    {
        var events = new List<RawEventRecord>();
        int seq = 0;

        for (int i = 0; i < 6; i++)
        {
            events.Add(new RawEventRecord
            {
                EventId = $"e-{seq}",
                SequenceNumber = seq,
                TimestampUtc = FixedNow,
                EventType = MedicalSupplyEventTypes.DamagedSupplyStocked,
                Category = RecursorEventCategories.SafetyError,
                Actor = "user",
                Target = "bin-sterile",
                Metrics = new EventMetrics
                {
                    AdditionalMetrics = new Dictionary<string, double>
                    {
                        [RecursorCommonMetricKeys.ErrorSeverity] = 4.0,
                        [RecursorCommonMetricKeys.SafetyCritical] = 1.0,
                        [RecursorCommonMetricKeys.IsCorrect] = 0.0,
                    },
                },
            });
            seq++;
        }

        for (int i = 0; i < 4; i++)
        {
            events.Add(new RawEventRecord
            {
                EventId = $"e-{seq}",
                SequenceNumber = seq,
                TimestampUtc = FixedNow,
                EventType = MedicalSupplyEventTypes.WrongBinError,
                Category = RecursorEventCategories.Error,
                Actor = "user",
                Target = "bin-wrong",
                Metrics = new EventMetrics
                {
                    AdditionalMetrics = new Dictionary<string, double>
                    {
                        [RecursorCommonMetricKeys.ErrorSeverity] = 2.0,
                        [RecursorCommonMetricKeys.IsCorrect] = 0.0,
                    },
                },
            });
            seq++;
        }

        events.Add(new RawEventRecord
        {
            EventId = $"e-{seq}",
            SequenceNumber = seq,
            TimestampUtc = FixedNow,
            EventType = MedicalSupplyEventTypes.ScenarioCompleted,
            Category = RecursorEventCategories.System,
            Actor = "user",
            Target = "scenario",
        });

        return new RawEventBatch
        {
            SchemaVersion = "1.0",
            BatchId = "batch-shadow-regression",
            SessionId = SessionId,
            UserId = UserId,
            SimId = SimId,
            ScenarioId = ScenarioId,
            ClientTimestampUtc = FixedNow,
            BatchSequence = 1,
            Events = events,
        };
    }

    // ── The regression test ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdaptationOutput_IsIdenticalWhetherPhase10EShadowPredictionIsEnabledOrNot()
    {
        var adxOff = new SpyAdxIngestion();
        var adxOn = new SpyAdxIngestion();
        var predictorOff = new FakeTemporalBehaviorStatePredictionService();
        var predictorOn = new FakeTemporalBehaviorStatePredictionService();

        var serviceOff = BuildService(enableTemporalBehaviorStatePrediction: false, predictorOff, adxOff);
        var serviceOn = BuildService(enableTemporalBehaviorStatePrediction: true, predictorOn, adxOn);

        // Same session (same SessionId/UserId/SimId/ScenarioId/initial state) and the same raw
        // event batch processed by each pipeline — the only difference between the two calls
        // is the EnableTemporalBehaviorStatePrediction flag baked into each service instance.
        var sessionOff = NewSession();
        var sessionOn = NewSession();

        var resultOff = await serviceOff.ProcessBatchAsync(sessionOff, BuildStrugglingBatch());
        var resultOn = await serviceOn.ProcessBatchAsync(sessionOn, BuildStrugglingBatch());

        // ── Prove the two runs actually took different code paths (not a vacuous comparison) ──
        Assert.Equal(0, predictorOff.CallCount);
        Assert.Equal(1, predictorOn.CallCount);
        Assert.Equal(0, adxOff.TemporalBehaviorStatePredictionIngestCount);
        Assert.True(adxOn.TemporalBehaviorStatePredictionIngestCount > 0);

        // ── Prove this is a real, non-trivial adaptation (not both sides silently no-op) ──
        Assert.True(resultOff.AdaptationProduced);
        Assert.True(resultOn.AdaptationProduced);
        Assert.NotEmpty(resultOff.ParameterChanges);

        // ── The actual adaptation output must be identical ──
        Assert.Equal(resultOff.AdaptationProduced, resultOn.AdaptationProduced);
        Assert.Equal(resultOff.ReasoningSummary, resultOn.ReasoningSummary);
        Assert.Equal(
            resultOff.HypothesisLabels.OrderBy(l => l, StringComparer.Ordinal),
            resultOn.HypothesisLabels.OrderBy(l => l, StringComparer.Ordinal));

        // Parameter changes: intervention families are not directly exposed on IngestionResult
        // (they're audit-only, persisted alongside the adaptation document, not part of the
        // pipeline's public return contract) — ParameterChanges is the actual Unity-facing
        // payload, so it is compared exhaustively, per parameter/operation/value.
        var changesOff = resultOff.ParameterChanges.OrderBy(c => c.Parameter, StringComparer.Ordinal).ToList();
        var changesOn = resultOn.ParameterChanges.OrderBy(c => c.Parameter, StringComparer.Ordinal).ToList();
        Assert.Equal(changesOff.Count, changesOn.Count);
        for (int i = 0; i < changesOff.Count; i++)
        {
            Assert.Equal(changesOff[i].Parameter, changesOn[i].Parameter);
            Assert.Equal(changesOff[i].Operation, changesOn[i].Operation);
            Assert.Equal(changesOff[i].Value?.ToString(), changesOn[i].Value?.ToString());
        }

        // Sanity: this batch is specifically engineered to produce hint-mode/difficulty/
        // time-pressure support changes — confirms the fixture is exercising a real adaptation,
        // not an edge case with zero parameter changes.
        Assert.Contains(changesOff, c => c.Parameter == "hintMode");
        Assert.Contains(changesOff, c => c.Parameter == "difficulty");
        Assert.Contains(changesOff, c => c.Parameter == "timePressure");

        // Unity-facing adaptive state — hintMode/difficulty/timePressure/errorTolerance as
        // actually applied to session state (what the sim would be told on its next request).
        Assert.Equal(sessionOff.CurrentDifficultyProfile, sessionOn.CurrentDifficultyProfile);

        // Guardrail/trajectory counters the policy layer reads on the NEXT window — must also
        // agree, or a later window's adaptation could silently diverge even though this one
        // didn't.
        Assert.Equal(sessionOff.ConsecutiveStableMasteryWindows, sessionOn.ConsecutiveStableMasteryWindows);
        Assert.Equal(sessionOff.ConsecutiveRelapseWindows, sessionOn.ConsecutiveRelapseWindows);
        Assert.Equal(sessionOff.ConsecutiveSupportFadeEligibleWindows, sessionOn.ConsecutiveSupportFadeEligibleWindows);

        // Explanation is deterministically null in both runs (no Azure OpenAI config in this
        // test) — asserting equality here only strengthens the no-divergence claim.
        Assert.Equal(resultOff.Explanation, resultOn.Explanation);

        // AdaptationId is a fresh Guid per adaptation by design — deliberately excluded from
        // the equality assertions above; only presence is checked.
        Assert.False(string.IsNullOrEmpty(resultOff.AdaptationId));
        Assert.False(string.IsNullOrEmpty(resultOn.AdaptationId));
    }

    [Fact]
    public async Task ShadowPredictionDisabled_NeverInvokesOrPersistsPhase10EPrediction()
    {
        var adx = new SpyAdxIngestion();
        var predictor = new FakeTemporalBehaviorStatePredictionService();
        var service = BuildService(enableTemporalBehaviorStatePrediction: false, predictor, adx);

        var result = await service.ProcessBatchAsync(NewSession(), BuildStrugglingBatch());

        Assert.True(result.AdaptationProduced);
        Assert.Equal(0, predictor.CallCount);
        Assert.Equal(0, adx.TemporalBehaviorStatePredictionIngestCount);
    }
}
