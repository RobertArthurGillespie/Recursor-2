using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 6/8/9 corrective-pass tests: legacy blank-<c>PredictionId</c> compatibility for
/// <c>TemporalBehaviorStatePredictions</c>. The KQL text tests elsewhere (Stage7IdempotencyAndDedupTests,
/// Stage10DashboardVersionSelectionTests, Stage7ConfusionMatrixKqlTests, Stage12KqlFileContentTests)
/// only prove the fallback fragment is present in the generated/checked-in KQL text — they cannot
/// execute it (no live ADX cluster in this environment). These tests instead exercise the actual
/// dedup *semantics* in-process: <see cref="BehaviorStatePredictionKeys.ComputeLogicalPredictionId"/>
/// is the exact C# mirror of the KQL `iif(isempty(PredictionId), strcat("legacy|", ...), PredictionId)`
/// fallback used by every query in <see cref="AdxDashboardQueryService"/>, and grouping-by-max-CreatedAtUtc
/// below simulates `summarize arg_max(CreatedAtUtc, *) by LogicalPredictionId` over an in-memory row set.
/// </summary>
public class Stage16LegacyPredictionIdMigrationTests
{
    private sealed record PhysicalRow(
        string PredictionId, string SessionId, int WindowIndex, int Horizon, string ModelVersion, DateTime CreatedAtUtc);

    // Simulates `| summarize arg_max(CreatedAtUtc, *) by LogicalPredictionId`.
    private static List<PhysicalRow> Dedup(IEnumerable<PhysicalRow> rows) =>
        rows
            .GroupBy(r => BehaviorStatePredictionKeys.ComputeLogicalPredictionId(
                r.PredictionId, r.SessionId, r.WindowIndex, r.Horizon, r.ModelVersion))
            .Select(g => g.OrderByDescending(r => r.CreatedAtUtc).First())
            .ToList();

    // ── ComputeLogicalPredictionId unit behavior ────────────────────────────

    [Fact]
    public void ComputeLogicalPredictionId_NonBlankId_ReturnsIdUnchanged()
    {
        var id = BehaviorStatePredictionKeys.ComputePredictionId("sess-1", 5, 1, "temporal-behavior-state-v1");

        var logical = BehaviorStatePredictionKeys.ComputeLogicalPredictionId(id, "sess-1", 5, 1, "temporal-behavior-state-v1");

        Assert.Equal(id, logical);
    }

    [Fact]
    public void ComputeLogicalPredictionId_BlankId_FallsBackToLegacyCompositeKey()
    {
        var logical = BehaviorStatePredictionKeys.ComputeLogicalPredictionId(
            "", "sess-1", 5, 1, "temporal-behavior-state-v1");

        Assert.Equal("legacy|sess-1|5|1|temporal-behavior-state-v1", logical);
    }

    [Fact]
    public void ComputeLogicalPredictionId_NullId_TreatedSameAsBlank()
    {
        var fromNull = BehaviorStatePredictionKeys.ComputeLogicalPredictionId(
            null, "sess-1", 5, 1, "temporal-behavior-state-v1");
        var fromEmpty = BehaviorStatePredictionKeys.ComputeLogicalPredictionId(
            "", "sess-1", 5, 1, "temporal-behavior-state-v1");

        Assert.Equal(fromEmpty, fromNull);
    }

    [Fact]
    public void ComputeLogicalPredictionId_BlankId_DifferentCompositeKeys_ProduceDifferentLogicalIds()
    {
        var a = BehaviorStatePredictionKeys.ComputeLogicalPredictionId("", "s1", 1, 1, "v1");
        var b = BehaviorStatePredictionKeys.ComputeLogicalPredictionId("", "s1", 2, 1, "v1");
        var c = BehaviorStatePredictionKeys.ComputeLogicalPredictionId("", "s2", 1, 1, "v1");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    // ── Dedup semantics over simulated physical rows ────────────────────────

    [Fact]
    public void NewRows_SamePredictionId_PhysicalRetriesCollapseToOneLogicalPrediction()
    {
        var predictionId = BehaviorStatePredictionKeys.ComputePredictionId("s1", 1, 1, "v1");
        var rows = new List<PhysicalRow>
        {
            new(predictionId, "s1", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new(predictionId, "s1", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc)), // retried ingest
        };

        var deduped = Dedup(rows);

        Assert.Single(deduped);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), deduped[0].CreatedAtUtc); // newest wins
    }

    [Fact]
    public void LegacyRows_BlankPredictionId_DistinctCompositeKeysRemainThreeDistinctPredictions()
    {
        // Exactly the scenario the corrective-pass spec calls out: three legacy rows with blank
        // PredictionId that must NOT collapse into one logical prediction just because their IDs
        // are all "".
        var rows = new List<PhysicalRow>
        {
            new("", "s1", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("", "s1", 2, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("", "s2", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        var deduped = Dedup(rows);

        Assert.Equal(3, deduped.Count);
    }

    [Fact]
    public void LegacyRows_DuplicatePhysicalCopies_CollapseToOneLogicalPrediction()
    {
        // Two physical copies of the exact same legacy (session, window, horizon, version) —
        // e.g. a pre-migration replayed/retried ingest — must still collapse to one row.
        var rows = new List<PhysicalRow>
        {
            new("", "s1", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("", "s1", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc)),
        };

        var deduped = Dedup(rows);

        Assert.Single(deduped);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc), deduped[0].CreatedAtUtc);
    }

    [Fact]
    public void MixedDataset_NewLegacyAndRetries_ProducesCorrectLogicalCount()
    {
        var newId1 = BehaviorStatePredictionKeys.ComputePredictionId("s1", 1, 1, "v2");
        var newId2 = BehaviorStatePredictionKeys.ComputePredictionId("s1", 2, 1, "v2");

        var rows = new List<PhysicalRow>
        {
            // New-format prediction, ingested once.
            new(newId1, "s1", 1, 1, "v2", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            // New-format prediction, physically retried (same PredictionId, two rows).
            new(newId2, "s1", 2, 1, "v2", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            new(newId2, "s1", 2, 1, "v2", new DateTime(2026, 2, 1, 0, 0, 1, DateTimeKind.Utc)),
            // Legacy rows: two distinct logical predictions, one of them duplicated physically.
            new("", "s2", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("", "s2", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)), // dup of the row above
            new("", "s3", 1, 1, "v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        var deduped = Dedup(rows);

        // 2 new-format logical predictions + 2 distinct legacy logical predictions = 4.
        Assert.Equal(4, deduped.Count);
    }

    // ── KQL fragment mirrors the C# semantics (documents the coupling; does not execute KQL) ──

    [Fact]
    public void SharedDedupKqlFragment_UsesTheDocumentedLegacyFallbackShape()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStatePredictionsKql("sess-1", "temporal-behavior-state-v1");

        Assert.Contains("iif(isempty(PredictionId)", kql);
        Assert.Contains("strcat(\"legacy|\", SessionId, \"|\", tostring(WindowIndex), \"|\", tostring(Horizon), \"|\", ModelVersion)", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
    }
}
