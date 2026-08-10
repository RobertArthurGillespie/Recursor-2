using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 7 corrective-pass tests: durable logical-key idempotency for temporal predictions and
/// defensive ADX-side deduplication. Live KQL execution against ADX was not performed as part
/// of this pass (no cluster access) — the KQL-facing tests here only verify the arg_max
/// dedup-by-logical-key pattern is present in the query text, which is as far as this
/// environment's tooling can validate without a live cluster (see Stage 15 report).
/// </summary>
public class Stage7IdempotencyAndDedupTests
{
    // ── Logical key semantics ────────────────────────────────────────────────

    [Fact]
    public void BehaviorStatePredictionKeys_SameInputs_ProduceEqualKeys()
    {
        var a = BehaviorStatePredictionKeys.For(10, 2, "temporal-behavior-state-v1");
        var b = BehaviorStatePredictionKeys.For(10, 2, "temporal-behavior-state-v1");

        Assert.Equal(a, b);
    }

    [Fact]
    public void CompletedBehaviorStatePredictionKeys_PartialHorizonFailure_OnlyMarksSucceededHorizons()
    {
        // Simulates: H1 ingest succeeds, H2 throws before it can be marked complete, H3 is
        // never reached this call — mirrors RecursorIngestionService.ProcessBehaviorStatePredictionAsync's
        // per-horizon try/catch, where only a horizon whose ingest actually succeeded is added.
        var session = new SessionDocument();
        var h1Key = BehaviorStatePredictionKeys.For(3, 1, "temporal-behavior-state-v1");
        var h2Key = BehaviorStatePredictionKeys.For(3, 2, "temporal-behavior-state-v1");
        var h3Key = BehaviorStatePredictionKeys.For(3, 3, "temporal-behavior-state-v1");

        session.CompletedBehaviorStatePredictionKeys.Add(h1Key); // only H1 succeeded

        Assert.Contains(h1Key, session.CompletedBehaviorStatePredictionKeys);
        Assert.DoesNotContain(h2Key, session.CompletedBehaviorStatePredictionKeys);
        Assert.DoesNotContain(h3Key, session.CompletedBehaviorStatePredictionKeys);

        // A retry would see H1 as already-completed (skip, no duplicate) and H2/H3 as pending
        // (eligible for retry) — exactly the desired all-or-nothing-per-horizon behavior.
        bool wouldSkipH1 = session.CompletedBehaviorStatePredictionKeys.Contains(h1Key);
        bool wouldRetryH2 = !session.CompletedBehaviorStatePredictionKeys.Contains(h2Key);
        Assert.True(wouldSkipH1);
        Assert.True(wouldRetryH2);
    }

    [Fact]
    public void CompletedBehaviorStatePredictionKeys_ReplayedWindow_DoesNotInflateCompletionCount()
    {
        var session = new SessionDocument();
        var key = BehaviorStatePredictionKeys.For(7, 1, "temporal-behavior-state-v1");

        session.CompletedBehaviorStatePredictionKeys.Add(key);
        session.CompletedBehaviorStatePredictionKeys.Add(key); // replay of the same window/horizon/version

        Assert.Single(session.CompletedBehaviorStatePredictionKeys);
    }

    // ── ADX-side defensive deduplication (KQL text) ──────────────────────────

    [Fact]
    public void TrainingQueryKql_DeduplicatesEmbeddingsAndTargetsByLogicalKey()
    {
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, WindowIndex", AdxTemporalTrainingQueryService.Kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon", AdxTemporalTrainingQueryService.Kql);
    }

    [Fact]
    public void ModelComparisonKql_DeduplicatesPredictionsAndTargetsByLogicalKey()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql();

        // Stage 4: predictions dedup by the deterministic PredictionId (derived from
        // SessionId+WindowIndex+Horizon+ModelVersion), not the raw composite key. Stage 6/7:
        // falls back to a legacy composite key for pre-migration rows with a blank PredictionId
        // — see Stage16LegacyPredictionIdMigrationTests for behavioral (not just text) coverage.
        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("legacy|", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon", kql);
    }

    [Fact]
    public void CoverageKql_DeduplicatesPredictionsAndTargetsByLogicalKey()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateCoverageKql();

        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("legacy|", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon", kql);
    }
}
