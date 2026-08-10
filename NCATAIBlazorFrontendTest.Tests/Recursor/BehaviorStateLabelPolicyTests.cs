using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Tests for the centralized Phase 10E behavior-state label normalizer.
/// Covers canonical-label recognition, casing/whitespace normalization, legacy
/// alias mapping, and the Missing-vs-NoSignal-vs-Invalid distinction that keeps
/// training data honest (no fabricated labels).
/// </summary>
public class BehaviorStateLabelPolicyTests
{
    [Theory]
    [InlineData("struggling")]
    [InlineData("recovering")]
    [InlineData("stable")]
    [InlineData("mastering")]
    [InlineData("conflicted")]
    public void Normalize_CanonicalLabel_ReturnsValid(string label)
    {
        var result = BehaviorStateLabelPolicy.Normalize(label);

        Assert.Equal(BehaviorStateLabelStatus.Valid, result.Status);
        Assert.Equal(label, result.CanonicalLabel);
        Assert.True(result.IsTrainable);
    }

    [Theory]
    [InlineData("STRUGGLING", "struggling")]
    [InlineData("  stable  ", "stable")]
    [InlineData("Mastering", "mastering")]
    public void Normalize_CasingAndWhitespace_IsNormalized(string raw, string expected)
    {
        var result = BehaviorStateLabelPolicy.Normalize(raw);

        Assert.Equal(BehaviorStateLabelStatus.Valid, result.Status);
        Assert.Equal(expected, result.CanonicalLabel);
    }

    [Theory]
    [InlineData("steady")]
    [InlineData("STEADY")]
    public void Normalize_LegacyAlias_MapsToCanonical(string raw)
    {
        var result = BehaviorStateLabelPolicy.Normalize(raw);

        Assert.Equal(BehaviorStateLabelStatus.Valid, result.Status);
        Assert.Equal(BehaviorStateLabelPolicy.Stable, result.CanonicalLabel);
    }

    [Fact]
    public void Normalize_Null_IsMissingNotInvalid()
    {
        var result = BehaviorStateLabelPolicy.Normalize(null);

        Assert.Equal(BehaviorStateLabelStatus.Missing, result.Status);
        Assert.Null(result.CanonicalLabel);
        Assert.False(result.IsTrainable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankOrWhitespace_IsMissing(string raw)
    {
        var result = BehaviorStateLabelPolicy.Normalize(raw);

        Assert.Equal(BehaviorStateLabelStatus.Missing, result.Status);
        Assert.Null(result.CanonicalLabel);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    public void Normalize_UnknownLiteral_IsNoSignalNotMissingOrStable(string raw)
    {
        var result = BehaviorStateLabelPolicy.Normalize(raw);

        Assert.Equal(BehaviorStateLabelStatus.NoSignal, result.Status);
        Assert.Null(result.CanonicalLabel);
        Assert.NotEqual(BehaviorStateLabelPolicy.Stable, result.CanonicalLabel);
        Assert.False(result.IsTrainable);
    }

    [Fact]
    public void Normalize_UnrecognizedLiteral_IsInvalid()
    {
        var result = BehaviorStateLabelPolicy.Normalize("confused_and_hint_dependent");

        Assert.Equal(BehaviorStateLabelStatus.Invalid, result.Status);
        Assert.Null(result.CanonicalLabel);
        Assert.Contains("confused_and_hint_dependent", result.Reason);
    }

    [Fact]
    public void CanonicalLabels_ContainsExactlyFiveClasses()
    {
        Assert.Equal(5, BehaviorStateLabelPolicy.CanonicalLabels.Count);
        Assert.Contains("struggling", BehaviorStateLabelPolicy.CanonicalLabels);
        Assert.Contains("recovering", BehaviorStateLabelPolicy.CanonicalLabels);
        Assert.Contains("stable", BehaviorStateLabelPolicy.CanonicalLabels);
        Assert.Contains("mastering", BehaviorStateLabelPolicy.CanonicalLabels);
        Assert.Contains("conflicted", BehaviorStateLabelPolicy.CanonicalLabels);
    }

    [Fact]
    public void IsCanonical_RecognizesOnlyExactCanonicalLiterals()
    {
        Assert.True(BehaviorStateLabelPolicy.IsCanonical("stable"));
        Assert.False(BehaviorStateLabelPolicy.IsCanonical("Stable"));
        Assert.False(BehaviorStateLabelPolicy.IsCanonical("unknown"));
        Assert.False(BehaviorStateLabelPolicy.IsCanonical("steady"));
    }
}
