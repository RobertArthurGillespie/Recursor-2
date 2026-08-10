using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 4 corrective-pass tests: deterministic PredictionId is the durable logical identity
/// for a shadow behavior-state prediction (SessionId + WindowIndex + Horizon + ModelVersion),
/// designed to survive what the in-memory idempotency guard (Stage 7,
/// CompletedBehaviorStatePredictionKeys) cannot — a process restart, replay across multiple App
/// Service instances, or re-ingestion after a retry. ADX itself is append-only, so physical
/// duplicate rows are tolerated (Option C from the Stage 4 brief); every read/evaluation path
/// must collapse them to one logical prediction by grouping on PredictionId.
/// </summary>
public class Stage4PredictionIdTests
{
    // ── Deterministic generation ─────────────────────────────────────────────

    [Fact]
    public void ComputePredictionId_SameInputs_ProduceTheSameId()
    {
        var a = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 2, "temporal-behavior-state-v1");
        var b = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 2, "temporal-behavior-state-v1");

        Assert.Equal(a, b);
        Assert.False(string.IsNullOrWhiteSpace(a));
    }

    [Fact]
    public void ComputePredictionId_DifferentHorizon_ProducesDifferentId()
    {
        var h1 = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 1, "temporal-behavior-state-v1");
        var h2 = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 2, "temporal-behavior-state-v1");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputePredictionId_DifferentModelVersion_ProducesDifferentId()
    {
        // Two different loaded model versions predicting the same session/window/horizon must be
        // tracked as two distinct logical predictions, not overwrite/collide with each other.
        var v1 = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 1, "temporal-behavior-state-v1");
        var v2 = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 1, "temporal-behavior-state-v2");

        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void ComputePredictionId_DifferentSessionOrWindow_ProducesDifferentId()
    {
        var baseline = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 1, "v1");
        var differentSession = BehaviorStatePredictionKeys.ComputePredictionId("sess-2", 5, 1, "v1");
        var differentWindow = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 6, 1, "v1");

        Assert.NotEqual(baseline, differentSession);
        Assert.NotEqual(baseline, differentWindow);
        Assert.NotEqual(differentSession, differentWindow);
    }

    // ── Restart / replay / cross-instance determinism ────────────────────────

    [Fact]
    public void ComputePredictionId_RecomputedAfterSimulatedRestart_IsIdentical()
    {
        // The function is pure/stateless — nothing about its output depends on process memory,
        // so "computed before a restart" and "computed after a restart" are indistinguishable
        // calls with the same inputs. This is what makes it safe to recompute on every retry
        // instead of depending on the in-memory CompletedBehaviorStatePredictionKeys set, which
        // is explicitly NOT durable across a restart.
        var beforeRestart = BehaviorStatePredictionKeys.ComputePredictionId("sess-durable", 3, 1, "temporal-behavior-state-v1");

        // Nothing is retained between these two calls other than the literal input values —
        // simulating a fresh process picking the same logical window back up after a restart.
        var afterRestart = BehaviorStatePredictionKeys.ComputePredictionId("sess-durable", 3, 1, "temporal-behavior-state-v1");

        Assert.Equal(beforeRestart, afterRestart);
    }

    [Fact]
    public void ComputePredictionId_ConcurrentComputation_AlwaysAgrees()
    {
        // Multiple App Service instances (or concurrent requests within one instance) computing
        // the ID for the same logical prediction must never disagree — there is no shared state
        // to race on since the function is pure.
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        System.Threading.Tasks.Parallel.For(0, 50, _ =>
            ids.Add(BehaviorStatePredictionKeys.ComputePredictionId("sess-concurrent", 9, 3, "temporal-behavior-state-v1")));

        Assert.Single(ids.Distinct());
    }

    // ── Row mapper wiring ─────────────────────────────────────────────────────

    [Fact]
    public void RowMapper_StampsPredictionId_MatchingTheStandaloneComputation()
    {
        var embedding = new NCATAIBlazorFrontendTest.Server.Recursor.Models.TemporalEmbeddingVector
        {
            SessionId = "sess-map", UserId = "user-map", SimId = "sim-1", ScenarioId = "scenario-1",
            WindowIndex = 4, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "stable", Confidence = 0.8f, ModelVersion = "temporal-behavior-state-v1",
            ClassProbabilities = new Dictionary<string, float> { ["stable"] = 0.8f },
        };

        var row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, prediction, horizon: 2, DateTime.UtcNow);

        var expected = BehaviorStatePredictionKeys.ComputePredictionId("sess-map", 4, 2, "temporal-behavior-state-v1");
        Assert.Equal(expected, row.PredictionId);
    }

    [Fact]
    public void RowMapper_RetryOfSameHorizon_ProducesTheSamePredictionId()
    {
        // Simulates: H1 ingest is attempted, the ADX call throws (row never lands), and a retry
        // re-maps the same inputs. The retried row must carry the SAME PredictionId as the first
        // attempt so downstream arg_max-by-PredictionId collapses both physical attempts (if the
        // first one actually did land despite the client-side exception) into one logical row.
        var embedding = new NCATAIBlazorFrontendTest.Server.Recursor.Models.TemporalEmbeddingVector
        {
            SessionId = "sess-retry", UserId = "user-retry", SimId = "sim-1", ScenarioId = "scenario-1",
            WindowIndex = 7, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "struggling", Confidence = 0.6f, ModelVersion = "temporal-behavior-state-v1",
            ClassProbabilities = new Dictionary<string, float> { ["struggling"] = 0.6f },
        };

        var firstAttempt = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, prediction, horizon: 1, DateTime.UtcNow);
        var retryAttempt = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, prediction, horizon: 1, DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(firstAttempt.PredictionId, retryAttempt.PredictionId);
        // CreatedAtUtc legitimately differs between attempts — arg_max(CreatedAtUtc, *) is what
        // picks the winner among physical duplicates sharing this PredictionId.
        Assert.NotEqual(firstAttempt.CreatedAtUtc, retryAttempt.CreatedAtUtc);
    }

    [Fact]
    public void RowMapper_H1SucceedsH2Fails_ProducesIndependentPredictionIdsPerHorizon()
    {
        var embedding = new NCATAIBlazorFrontendTest.Server.Recursor.Models.TemporalEmbeddingVector
        {
            SessionId = "sess-partial", UserId = "user-partial", SimId = "sim-1", ScenarioId = "scenario-1",
            WindowIndex = 2, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var h1Prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "mastering", Confidence = 0.7f, ModelVersion = "temporal-behavior-state-v1",
        };
        var h2Prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "recovering", Confidence = 0.5f, ModelVersion = "temporal-behavior-state-v1",
        };

        var h1Row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, h1Prediction, horizon: 1, DateTime.UtcNow);
        var h2Row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, h2Prediction, horizon: 2, DateTime.UtcNow);

        // H1's PredictionId is durable/known even though H2 never landed (simulating H2's ADX
        // call throwing) — a later retry of H2 alone recomputes the same H2 PredictionId without
        // touching or duplicating H1's.
        Assert.NotEqual(h1Row.PredictionId, h2Row.PredictionId);
        var h2Retry = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, h2Prediction, horizon: 2, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(h2Row.PredictionId, h2Retry.PredictionId);
    }

    // ── Physical-duplicate collapse (simulates arg_max(CreatedAtUtc, *) by PredictionId) ─────

    [Fact]
    public void DuplicatePhysicalRows_CollapseToOneLogicalPredictionByPredictionId()
    {
        // Three physical rows land in ADX for the exact same logical prediction (e.g. a
        // restart-triggered replay produced two extra copies) plus one row for a different
        // horizon. Grouping by PredictionId and keeping the latest CreatedAtUtc — exactly what
        // every KQL query in this pass now does via `summarize arg_max(CreatedAtUtc, *) by
        // PredictionId` — must collapse the three duplicates into one.
        var embedding = new NCATAIBlazorFrontendTest.Server.Recursor.Models.TemporalEmbeddingVector
        {
            SessionId = "sess-dup", UserId = "user-dup", SimId = "sim-1", ScenarioId = "scenario-1",
            WindowIndex = 1, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var predictionH1 = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "stable", Confidence = 0.9f, ModelVersion = "temporal-behavior-state-v1",
        };
        var predictionH2 = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "conflicted", Confidence = 0.4f, ModelVersion = "temporal-behavior-state-v1",
        };

        var now = DateTime.UtcNow;
        var rows = new List<TemporalBehaviorStatePredictionRow>
        {
            AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, predictionH1, horizon: 1, now),
            AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, predictionH1, horizon: 1, now.AddSeconds(1)), // replay duplicate
            AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, predictionH1, horizon: 1, now.AddSeconds(2)), // replay duplicate
            AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, predictionH2, horizon: 2, now),
        };

        var collapsed = rows
            .GroupBy(r => r.PredictionId)
            .Select(g => g.OrderByDescending(r => r.CreatedAtUtc).First())
            .ToList();

        Assert.Equal(2, collapsed.Count); // one per horizon, not four
        var h1Winner = collapsed.Single(r => r.Horizon == 1);
        Assert.Equal(now.AddSeconds(2), h1Winner.CreatedAtUtc); // latest physical copy wins
    }

    // ── Multiple model versions for the same window ──────────────────────────

    [Fact]
    public void TwoModelVersions_SameSessionWindowHorizon_ProduceDistinctPredictionsThatDoNotCollapse()
    {
        var embedding = new NCATAIBlazorFrontendTest.Server.Recursor.Models.TemporalEmbeddingVector
        {
            SessionId = "sess-multiversion", UserId = "user-1", SimId = "sim-1", ScenarioId = "scenario-1",
            WindowIndex = 5, Values = new double[25], FeatureNamesJson = "[]", EmbeddingVersion = "v1.0",
        };
        var v1Prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "stable", Confidence = 0.9f, ModelVersion = "temporal-behavior-state-v1",
        };
        var v2Prediction = new TemporalBehaviorStateHorizonPrediction
        {
            PredictedBehaviorState = "mastering", Confidence = 0.85f, ModelVersion = "temporal-behavior-state-v2",
        };

        var v1Row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, v1Prediction, horizon: 1, DateTime.UtcNow);
        var v2Row = AdxRowMapper.MapTemporalBehaviorStatePrediction(embedding, v2Prediction, horizon: 1, DateTime.UtcNow);

        var collapsed = new[] { v1Row, v2Row }
            .GroupBy(r => r.PredictionId)
            .ToList();

        Assert.Equal(2, collapsed.Count); // both versions' predictions survive independently
    }
}
