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
/// Regression tests for the feature-window accumulation bug: a feature window must be built
/// from every event received since the previous window, not merely the batch that happened to
/// cross a trigger. See FeatureExtractionService.TryExtractWindow and
/// RecursorIngestionService.ProcessBatchAsync (steps 6-8) for the fix.
/// </summary>
public class FeatureWindowAccumulationTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── Test doubles for direct FeatureExtractionService-level tests ──────────────────────
    // A recording adapter lets these tests assert on the exact event set handed to feature
    // extraction (count, sequence numbers, identity) independent of any specific sim's
    // behavioral scoring formulas.

    private sealed class RecordingAdapter : ISimEventInterpretationAdapter
    {
        public List<List<RawEventRecord>> ReceivedEventSets { get; } = new();

        public bool AppliesTo(string simId) => true;

        public bool ShouldForceWindow(RawEventBatch batch) =>
            batch.Events.Any(e => e.EventType == "force_trigger");

        public BehavioralFeatureSet ExtractFeatures(SessionDocument session, List<RawEventRecord> events)
        {
            ReceivedEventSets.Add(new List<RawEventRecord>(events));
            return new BehavioralFeatureSet();
        }
    }

    private sealed class SingleAdapterFactory : ISimEventInterpretationAdapterFactory
    {
        private readonly ISimEventInterpretationAdapter _adapter;
        public SingleAdapterFactory(ISimEventInterpretationAdapter adapter) => _adapter = adapter;
        public ISimEventInterpretationAdapter GetAdapter(string simId) => _adapter;
    }

    private static SessionDocument NewSession(string simId = "generic-sim") => new()
    {
        SessionId = "sess-accum",
        UserId = "user-accum",
        SimId = simId,
        ScenarioId = "scenario-accum",
        Status = "active",
    };

    private static RawEventBatch MakeBatch(int count, ref int seq, string eventType = "action")
    {
        var batch = new RawEventBatch { SessionId = "sess-accum", UserId = "user-accum" };
        for (int i = 0; i < count; i++)
        {
            batch.Events.Add(new RawEventRecord
            {
                EventId = $"e-{seq}",
                SequenceNumber = seq,
                TimestampUtc = BaseTime.AddSeconds(seq),
                EventType = eventType,
                Category = "task_action",
                Actor = "user",
                Target = "obj",
            });
            seq++;
        }
        return batch;
    }

    // Mirrors RecursorIngestionService.ProcessBatchAsync steps 6-8 (append batch to the
    // pending buffer, attempt extraction, reset buffer+counter only when a window is
    // produced) without constructing the full ~20-dependency ingestion service. Kept in sync
    // with production by design: this is exactly what step 6 + the post-ingest reset in step 8
    // do, minus the actual ADX call.
    private static FeatureWindowDocument? SimulateBatch(
        FeatureExtractionService featureService, SessionDocument session, RawEventBatch batch)
    {
        session.EventCount += batch.Events.Count;
        session.EventsSinceLastWindow += batch.Events.Count;
        session.PendingFeatureWindowEvents.AddRange(batch.Events);
        session.BatchCount += 1;

        var window = featureService.TryExtractWindow(session, batch);
        if (window is not null)
        {
            session.EventsSinceLastWindow = 0;
            session.PendingFeatureWindowEvents = new List<RawEventRecord>();
        }
        return window;
    }

    // ── Edge case 1: normal accumulation ───────────────────────────────────────────────────

    [Fact]
    public void NormalAccumulation_ThreeBatches_OnlyThirdProducesWindow_ContainingAll55Events()
    {
        var adapter = new RecordingAdapter();
        var featureService = new FeatureExtractionService(new SingleAdapterFactory(adapter));
        var session = NewSession();
        int seq = 0;

        Assert.Null(SimulateBatch(featureService, session, MakeBatch(20, ref seq)));
        Assert.Null(SimulateBatch(featureService, session, MakeBatch(20, ref seq)));
        var window = SimulateBatch(featureService, session, MakeBatch(15, ref seq));

        Assert.NotNull(window);
        Assert.Equal("accumulation", window!.WindowType);
        Assert.Equal(55, Assert.Single(adapter.ReceivedEventSets).Count);
        Assert.Equal(0, window.WindowStartSequence);
        Assert.Equal(54, window.WindowEndSequence);
        Assert.Equal(BaseTime, window.WindowStartUtc);
        Assert.Equal(BaseTime.AddSeconds(54), window.WindowEndUtc);

        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
    }

    // ── Edge case 2: threshold-crossing batch ──────────────────────────────────────────────

    [Fact]
    public void ThresholdCrossingBatch_48Buffered_Plus7_ProducesWindowWithAll55Events()
    {
        var adapter = new RecordingAdapter();
        var featureService = new FeatureExtractionService(new SingleAdapterFactory(adapter));
        var session = NewSession();
        int seq = 0;

        Assert.Null(SimulateBatch(featureService, session, MakeBatch(48, ref seq)));
        var window = SimulateBatch(featureService, session, MakeBatch(7, ref seq));

        Assert.NotNull(window);
        Assert.Equal(55, Assert.Single(adapter.ReceivedEventSets).Count);
        Assert.Equal(0, window!.WindowStartSequence);
        Assert.Equal(54, window.WindowEndSequence);
    }

    // ── Edge case 3: forced window before threshold ────────────────────────────────────────

    [Fact]
    public void ForcedWindow_BeforeThreshold_ContainsAllBufferedEventsIncludingTriggeringBatch()
    {
        var adapter = new RecordingAdapter();
        var featureService = new FeatureExtractionService(new SingleAdapterFactory(adapter));
        var session = NewSession();
        int seq = 0;

        Assert.Null(SimulateBatch(featureService, session, MakeBatch(30, ref seq)));

        var batch = MakeBatch(5, ref seq); // 5 ordinary events + 1 force-trigger below = 6 total
        batch.Events.Add(new RawEventRecord
        {
            EventId = $"e-{seq}",
            SequenceNumber = seq,
            TimestampUtc = BaseTime.AddSeconds(seq),
            EventType = "force_trigger",
            Category = "system",
            Actor = "user",
            Target = "obj",
        });
        seq++;

        var window = SimulateBatch(featureService, session, batch);

        Assert.NotNull(window);
        Assert.Equal("safety-trigger", window!.WindowType);
        Assert.Equal(36, Assert.Single(adapter.ReceivedEventSets).Count);
        Assert.Equal(0, window.WindowStartSequence);
        Assert.Equal(35, window.WindowEndSequence);
        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
    }

    // ── Edge case 4: stage completion before threshold ─────────────────────────────────────

    [Fact]
    public void StageCompletionTrigger_BeforeThreshold_ContainsAllBufferedEvents()
    {
        var adapter = new RecordingAdapter();
        var featureService = new FeatureExtractionService(new SingleAdapterFactory(adapter));
        var session = NewSession();
        int seq = 0;

        Assert.Null(SimulateBatch(featureService, session, MakeBatch(25, ref seq)));

        var batch = MakeBatch(4, ref seq);
        batch.Events.Add(new RawEventRecord
        {
            EventId = $"e-{seq}",
            SequenceNumber = seq,
            TimestampUtc = BaseTime.AddSeconds(seq),
            EventType = "stage_complete",
            Category = "system",
            Actor = "user",
            Target = "obj",
        });
        seq++;

        var window = SimulateBatch(featureService, session, batch);

        Assert.NotNull(window);
        Assert.Equal("stage-completion", window!.WindowType);
        Assert.Equal(30, Assert.Single(adapter.ReceivedEventSets).Count); // 25 + 4 + 1
        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
    }

    // ── Edge case 5: second window after reset ─────────────────────────────────────────────

    [Fact]
    public void SecondWindowAfterReset_DoesNotContainEventsFromPriorWindow()
    {
        var adapter = new RecordingAdapter();
        var featureService = new FeatureExtractionService(new SingleAdapterFactory(adapter));
        var session = NewSession();
        int seq = 0;

        Assert.NotNull(SimulateBatch(featureService, session, MakeBatch(50, ref seq))); // seq 0-49
        Assert.Single(adapter.ReceivedEventSets);

        Assert.Null(SimulateBatch(featureService, session, MakeBatch(10, ref seq))); // seq 50-59
        Assert.Single(adapter.ReceivedEventSets); // still just the first window

        var window2 = SimulateBatch(featureService, session, MakeBatch(41, ref seq)); // seq 60-100

        Assert.NotNull(window2);
        Assert.Equal(2, adapter.ReceivedEventSets.Count);
        var secondWindowEvents = adapter.ReceivedEventSets[1];
        Assert.Equal(51, secondWindowEvents.Count);
        Assert.All(secondWindowEvents, e => Assert.True(e.SequenceNumber >= 50));
        Assert.Equal(50, window2!.WindowStartSequence);
        Assert.Equal(100, window2.WindowEndSequence);
    }

    // ── Edge case 6 / core regression: alternating success/error batches (medical-supply) ──
    // Builds the exact scenario from the bug report: successes and errors are deliberately
    // split across separate transmission batches (alternating mostly-success / mostly-error)
    // so that if extraction still scored only the threshold-crossing batch, the aggregate
    // dimensions below would be very different (batch D alone is 2 successes / 10 errors).
    //
    // Totals across all 4 batches: 20 SupplyStockedCorrectly + 6 DamagedSupplyRejectedCorrectly
    // (26 successes) + 26 WrongBinError (26 errors), 52 events, no safety errors, no explicit
    // hint/feedback events — which by the adapter's documented formulas works out to exactly
    // the values below.

    private static RawEventRecord MedicalEvent(string eventType, ref int seq)
    {
        var evt = new RawEventRecord
        {
            EventId = $"e-{seq}",
            SequenceNumber = seq,
            TimestampUtc = BaseTime.AddSeconds(seq),
            EventType = eventType,
            Category = eventType == MedicalSupplyEventTypes.WrongBinError
                ? RecursorEventCategories.Error
                : RecursorEventCategories.Success,
            Actor = "user",
            Target = "bin-a",
        };
        seq++;
        return evt;
    }

    private static RawEventBatch MedicalBatch(int stockedCorrectly, int correctRejections, int wrongBinError, ref int seq)
    {
        var batch = new RawEventBatch
        {
            SessionId = "sess-med-accum",
            UserId = "user-med-accum",
            SimId = "medical-supply-stocking",
        };
        for (int i = 0; i < stockedCorrectly; i++)
            batch.Events.Add(MedicalEvent(MedicalSupplyEventTypes.SupplyStockedCorrectly, ref seq));
        for (int i = 0; i < correctRejections; i++)
            batch.Events.Add(MedicalEvent(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly, ref seq));
        for (int i = 0; i < wrongBinError; i++)
            batch.Events.Add(MedicalEvent(MedicalSupplyEventTypes.WrongBinError, ref seq));
        return batch;
    }

    private static FeatureExtractionService MakeMedicalFeatureService() =>
        new(new SimEventInterpretationAdapterFactory(
            new MedicalSupplyEventInterpretationAdapter(), new DefaultSimEventInterpretationAdapter()));

    private static SessionDocument NewMedicalSession() => new()
    {
        SessionId = "sess-med-accum",
        UserId = "user-med-accum",
        SimId = "medical-supply-stocking",
        ScenarioId = "basic-supply-room",
        Status = "active",
    };

    [Fact]
    public void MedicalSupply_AlternatingBatches_FeaturesReflectCombinedEventSet_NotFinalBatchAlone()
    {
        var featureService = MakeMedicalFeatureService();
        var session = NewMedicalSession();
        int seq = 0;

        Assert.Null(SimulateBatch(featureService, session, MedicalBatch(10, 3, 2, ref seq)));  // A: mostly success (13 ok / 2 err)
        Assert.Null(SimulateBatch(featureService, session, MedicalBatch(2, 0, 12, ref seq)));   // B: mostly error   (2 ok / 12 err)
        Assert.Null(SimulateBatch(featureService, session, MedicalBatch(6, 3, 2, ref seq)));    // C: mostly success (9 ok / 2 err)
        var window = SimulateBatch(featureService, session, MedicalBatch(2, 0, 10, ref seq));   // D: mostly error, crosses threshold (52 total)

        Assert.NotNull(window);
        Assert.Equal("accumulation", window!.WindowType);
        Assert.Equal(52, window.WindowEndSequence - window.WindowStartSequence + 1);

        // If extraction were still scoring only batch D (2 successes / 10 errors), these
        // would be far from the 50/50-aggregate values below — SafetyCompliance in
        // particular would fall back to its neutral 0.75 default (batch D alone has zero
        // supply-quality decisions).
        var features = window.Features;
        Assert.Equal(1.0, features.SafetyCompliance, 3);
        Assert.Equal(1.0, features.AttentionDetection, 3);
        Assert.Equal(0.5, features.GoalUnderstanding, 3);
        Assert.Equal(0.5, features.ProcedureSequencing, 3);
        Assert.Equal(0.4, features.SelfCorrection, 3);

        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
    }

    // ── Edge cases 7 & 8: through the real RecursorIngestionService pipeline ──────────────
    // These need the real ingestion service (not the SimulateBatch harness above) because raw
    // event ingestion and buffer-clear-on-success are both orchestrated in
    // RecursorIngestionService.ProcessBatchAsync, not in FeatureExtractionService.

    private sealed class NoOpUserRelativeSignalService : IUserRelativeSignalService
    {
        public Task<UserRelativeSignals?> GetUserRelativeSignalsAsync(string userId) =>
            Task.FromResult<UserRelativeSignals?>(null);
    }

    private sealed class NoOpTemporalBehaviorStatePredictionService : ITemporalBehaviorStatePredictionService
    {
        public TemporalBehaviorStatePredictionResult Predict(TemporalEmbeddingVector embedding) => new();
    }

    // No-op ADX ingestion that records raw-event batch sizes and feature-window rows, and can
    // be told to throw on the next feature-window ingest to exercise failure-safety semantics.
    private sealed class SpyAdxIngestionService : IAdxIngestionService
    {
        public List<int> RawEventBatchSizes { get; } = new();
        public List<FeatureWindowRow> FeatureWindowRows { get; } = new();
        public int FeatureWindowIngestAttempts { get; private set; }
        public bool ThrowOnNextFeatureWindowIngest { get; set; }

        public Task IngestRawEventsAsync(IEnumerable<RawEventRow> rows)
        {
            RawEventBatchSizes.Add(rows.Count());
            return Task.CompletedTask;
        }

        public Task IngestFeatureWindowAsync(FeatureWindowRow row)
        {
            FeatureWindowIngestAttempts++;
            if (ThrowOnNextFeatureWindowIngest)
            {
                ThrowOnNextFeatureWindowIngest = false;
                throw new InvalidOperationException("Simulated ADX outage during feature-window ingest.");
            }
            FeatureWindowRows.Add(row);
            return Task.CompletedTask;
        }

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
        public Task IngestTemporalBehaviorStatePredictionAsync(TemporalBehaviorStatePredictionRow row) => Task.CompletedTask;
    }

    // Mirrors Stage9ShadowOnlyAdaptationRegressionTests.BuildService: the real, fully-wired
    // pipeline with every optional dependency at its real default (all Phase 8/9/10
    // personalization and shadow-prediction flags off), so nothing besides the accumulation
    // fix under test can influence these results.
    private static RecursorIngestionService BuildIngestionService(IAdxIngestionService adxIngestion)
    {
        var policies = Options.Create(new RecursorPoliciesOptions());
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
            new NoOpTemporalBehaviorStatePredictionService(),
            policies,
            NullLogger<RecursorIngestionService>.Instance);
    }

    private static SessionDocument NewIngestionSession() => new()
    {
        SessionId = "sess-med-ingestion",
        UserId = "user-med-ingestion",
        SimId = "medical-supply-stocking",
        ScenarioId = "basic-supply-room",
        Status = "active",
        CurrentDifficultyProfile = new Dictionary<string, string>(),
    };

    [Fact]
    public async Task ProcessBatchAsync_MultipleBatches_IngestsRawEventsExactlyOncePerBatch()
    {
        var spy = new SpyAdxIngestionService();
        var service = BuildIngestionService(spy);
        var session = NewIngestionSession();
        int seq = 0;

        await service.ProcessBatchAsync(session, MedicalBatch(10, 3, 2, ref seq)); // A: 15
        await service.ProcessBatchAsync(session, MedicalBatch(2, 0, 12, ref seq)); // B: 14
        await service.ProcessBatchAsync(session, MedicalBatch(6, 3, 2, ref seq));  // C: 11
        await service.ProcessBatchAsync(session, MedicalBatch(2, 0, 10, ref seq)); // D: 12, crosses threshold

        Assert.Equal(new[] { 15, 14, 11, 12 }, spy.RawEventBatchSizes);
        Assert.Equal(52, spy.RawEventBatchSizes.Sum());
        Assert.Single(spy.FeatureWindowRows);
    }

    [Fact]
    public async Task ProcessBatchAsync_FeatureWindowIngestThrows_BufferAndCounterPreserved_RetryIncludesFailedEvents()
    {
        var spy = new SpyAdxIngestionService();
        var service = BuildIngestionService(spy);
        var session = NewIngestionSession();
        int seq = 0;

        await service.ProcessBatchAsync(session, MedicalBatch(10, 3, 2, ref seq)); // A: 15
        await service.ProcessBatchAsync(session, MedicalBatch(2, 0, 12, ref seq)); // B: 14
        await service.ProcessBatchAsync(session, MedicalBatch(6, 3, 2, ref seq));  // C: 11 (cumulative 40)

        spy.ThrowOnNextFeatureWindowIngest = true;
        var batchD = MedicalBatch(2, 0, 10, ref seq); // D: 12, cumulative 52 -> triggers, then ingest throws
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessBatchAsync(session, batchD));

        // The failed attempt must not have cleared the buffer or reset the counter.
        Assert.Equal(52, session.PendingFeatureWindowEvents.Count);
        Assert.Equal(52, session.EventsSinceLastWindow);
        Assert.Empty(spy.FeatureWindowRows);
        Assert.Equal(1, spy.FeatureWindowIngestAttempts);

        // Raw-event ingestion for batch D must still have gone through exactly once, even
        // though the downstream feature-window ingest failed.
        Assert.Equal(new[] { 15, 14, 11, 12 }, spy.RawEventBatchSizes);

        // The next batch must succeed and its window must include every event since the
        // first failed attempt (52 buffered + 1 new = 53), not just the new event.
        var batchE = MedicalBatch(1, 0, 0, ref seq);
        await service.ProcessBatchAsync(session, batchE);

        Assert.Single(spy.FeatureWindowRows);
        Assert.Equal(0, spy.FeatureWindowRows[0].WindowStartSequence);
        Assert.Equal(52, spy.FeatureWindowRows[0].WindowEndSequence);
        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
    }

    // ── Session cleanup ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EndSessionAsync_ClearsPendingFeatureWindowBuffer()
    {
        var spy = new SpyAdxIngestionService();
        var ingestionService = BuildIngestionService(spy);
        var sessionRepository = new SessionRepository();
        var simCatalog = new SimCatalogRepository();
        var sessionService = new RecursorSessionService(
            sessionRepository, simCatalog, ingestionService, NullLogger<RecursorSessionService>.Instance);

        var session = NewIngestionSession();
        sessionRepository.Add(session);
        int seq = 0;

        // Below the accumulation threshold — events remain buffered, not yet flushed.
        await ingestionService.ProcessBatchAsync(session, MedicalBatch(10, 3, 2, ref seq));
        Assert.NotEmpty(session.PendingFeatureWindowEvents);
        Assert.NotEqual(0, session.EventsSinceLastWindow);

        await sessionService.EndSessionAsync(session.SessionId);

        Assert.Empty(session.PendingFeatureWindowEvents);
        Assert.Equal(0, session.EventsSinceLastWindow);
        Assert.Equal("ended", session.Status);
    }
}
