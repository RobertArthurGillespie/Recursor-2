using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 6 corrective-pass tests: BehaviorStateLabelPolicy must be the single, consistently
/// applied source of truth for missing / unknown / invalid / valid / legacy-alias
/// classification across every consumer — training exclusion (ClassifyRow) and dashboard
/// correctness (ComputeBehaviorStateCorrectness) in particular. Every consumer must classify
/// the same pathological input identically; a value trainable in one place must never be
/// silently treated as untrainable (or vice versa) in another.
/// </summary>
public class Stage6LabelPolicyConsistencyTests
{
    public static IEnumerable<object[]> PathologicalValues => new List<object[]>
    {
        new object[] { "stable",    BehaviorStateLabelStatus.Valid,    "stable" },
        new object[] { "Stable",    BehaviorStateLabelStatus.Valid,    "stable" },
        new object[] { " stable ",  BehaviorStateLabelStatus.Valid,    "stable" },
        new object[] { "steady",    BehaviorStateLabelStatus.Valid,    "stable" },
        new object[] { " STEADY ",  BehaviorStateLabelStatus.Valid,    "stable" },
        new object[] { "unknown",   BehaviorStateLabelStatus.NoSignal, null! },
        new object[] { " UNKNOWN ", BehaviorStateLabelStatus.NoSignal, null! },
        new object[] { "",          BehaviorStateLabelStatus.Missing,  null! },
        new object[] { null!,       BehaviorStateLabelStatus.Missing,  null! },
        new object[] { "mastered",  BehaviorStateLabelStatus.Invalid,  null! },
        new object[] { "confused",  BehaviorStateLabelStatus.Invalid,  null! },
    };

    [Theory]
    [MemberData(nameof(PathologicalValues))]
    public void Normalize_ClassifiesEachPathologicalValueAsDocumented(
        string? raw, BehaviorStateLabelStatus expectedStatus, string? expectedCanonical)
    {
        var result = BehaviorStateLabelPolicy.Normalize(raw);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCanonical, result.CanonicalLabel);
        Assert.Equal(expectedStatus == BehaviorStateLabelStatus.Valid, result.IsTrainable);
    }

    [Theory]
    [MemberData(nameof(PathologicalValues))]
    public void ClassifyRow_TrainerExclusion_MatchesPolicyTrainability(
        string? raw, BehaviorStateLabelStatus expectedStatus, string? _)
    {
        var row = new TemporalRiskTrainingRow
        {
            SessionId = "s1",
            EmbeddingValues = new float[25],
            TargetBehaviorState = raw ?? "",
        };

        var (trainable, canonicalLabel, exclusionReason) = TemporalBehaviorStateModelTrainingService.ClassifyRow(row);

        Assert.Equal(expectedStatus == BehaviorStateLabelStatus.Valid, trainable);
        if (trainable)
        {
            Assert.NotNull(canonicalLabel);
            Assert.Null(exclusionReason);
        }
        else
        {
            Assert.Null(canonicalLabel);
            Assert.NotNull(exclusionReason);
        }
    }

    // Dashboard correctness must agree with the policy: only a Valid target participates in
    // correctness at all (everything else is "pending", i.e. null — never "incorrect"), and a
    // legacy-aliased or differently-cased target must compare equal to a canonical prediction.
    [Theory]
    [MemberData(nameof(PathologicalValues))]
    public void ComputeBehaviorStateCorrectness_AgreesWithPolicyTrainability(
        string? raw, BehaviorStateLabelStatus expectedStatus, string? _)
    {
        var predictions = new List<TemporalBehaviorStatePredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedBehaviorState = "stable" },
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetBehaviorState = raw ?? "" },
        };

        var correct = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(predictions, targets, 1);

        if (expectedStatus != BehaviorStateLabelStatus.Valid)
        {
            Assert.Null(correct); // pending — never counted as incorrect
        }
        else
        {
            // Every Valid pathological value here normalizes to "stable", matching the prediction.
            Assert.True(correct);
        }
    }

    [Fact]
    public void ComputeBehaviorStateCorrectness_MismatchedCanonicalLabels_IsFalseNotNull()
    {
        var predictions = new List<TemporalBehaviorStatePredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedBehaviorState = "struggling" },
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetBehaviorState = "stable" },
        };

        var correct = DashboardTimelineBuilder.ComputeBehaviorStateCorrectness(predictions, targets, 1);

        Assert.False(correct);
    }

    // ── Stage 5: target persistence must round-trip through the policy ──────────
    // TemporalEmbeddingService.BuildPredictionTargets used to persist
    // MultiSignalGuardrailSummary.OverallBehaviorState verbatim (TargetBehaviorState = raw,
    // no normalization) — every consumer had to remember to re-normalize on read, which is
    // exactly the class of bug the Blazor dashboard bypass (also fixed this stage) fell into.
    // NormalizeTargetBehaviorState is what BuildPredictionTargets now calls before persisting;
    // this asserts round-trip consistency — whatever gets persisted must classify identically to
    // the raw input when read back through BehaviorStateLabelPolicy.Normalize, for every
    // pathological value, and a Valid input's canonical label is exactly what gets stored.
    [Theory]
    [MemberData(nameof(PathologicalValues))]
    public void NormalizeTargetBehaviorState_PersistedValue_RoundTripsToTheSameClassification(
        string? raw, BehaviorStateLabelStatus expectedStatus, string? expectedCanonical)
    {
        var persisted = TemporalEmbeddingService.NormalizeTargetBehaviorState(raw);

        // Never fabricates a canonical label for a row that lacks evidence.
        if (expectedStatus != BehaviorStateLabelStatus.Valid)
            Assert.NotEqual(BehaviorStateLabelPolicy.Stable, persisted);

        var reread = BehaviorStateLabelPolicy.Normalize(persisted);
        Assert.Equal(expectedStatus, reread.Status);
        Assert.Equal(expectedCanonical, reread.CanonicalLabel);

        if (expectedStatus == BehaviorStateLabelStatus.Valid)
            Assert.Equal(expectedCanonical, persisted); // canonical label is stored verbatim
    }

    [Fact]
    public void NormalizeTargetBehaviorState_NoSignal_PersistsTheCanonicalLowercaseSentinel()
    {
        // This is the specific fix for the dashboard bypass this stage also closes: every other
        // consumer (KQL, DashboardTimelineBuilder, the Blazor pages after this stage) checks for
        // the exact literal "unknown" — persisting any other casing/whitespace variant of the
        // guardrail's "unknown" output would silently break a consumer that does a raw compare.
        Assert.Equal("unknown", TemporalEmbeddingService.NormalizeTargetBehaviorState("UNKNOWN"));
        Assert.Equal("unknown", TemporalEmbeddingService.NormalizeTargetBehaviorState(" Unknown "));
    }

    // ── KQL structural mirror check ──────────────────────────────────────────
    // Live KQL execution against ADX was not performed as part of this pass (no cluster
    // access) — this test only verifies the shared normalization block is present verbatim in
    // both queries that must use it, so the two queries cannot drift apart from each other.
    [Fact]
    public void ModelComparisonAndCoverageKql_ShareTheSameNormalizationBlock()
    {
        var comparisonKql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql();
        var coverageKql = AdxDashboardQueryService.BuildBehaviorStateCoverageKql();

        const string marker = "let NormalizeBehaviorState = (raw: string)";
        Assert.Contains(marker, comparisonKql);
        Assert.Contains(marker, coverageKql);

        // Both must map the same legacy alias and distinguish missing/unknown/invalid.
        foreach (var kql in new[] { comparisonKql, coverageKql })
        {
            Assert.Contains("\"steady\"", kql);
            Assert.Contains("\"missing\"", kql);
            Assert.Contains("\"unknown\"", kql);
            Assert.Contains("\"invalid\"", kql);
        }
    }
}
