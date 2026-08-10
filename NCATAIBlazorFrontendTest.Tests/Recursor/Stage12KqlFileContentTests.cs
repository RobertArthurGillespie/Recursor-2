using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 12 corrective-pass tests: sanity-checks the standalone Phase 10E KQL scripts under
/// docs/kql/ for the specific defects this pass fixed. This is the extent to which KQL
/// correctness can be validated without a live ADX cluster (no cluster access was available
/// during this pass — see the final report's "live validation pending" section); it does not
/// execute the KQL, only verifies the expected patterns are present in the checked-in text.
/// </summary>
public class Stage12KqlFileContentTests
{
    private static string ReadDocsKqlFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "kql")))
            dir = dir.Parent;

        Assert.NotNull(dir); // repo root (containing docs/kql) must be found by walking up from the test binary
        var path = Path.Combine(dir!.FullName, "docs", "kql", fileName);
        Assert.True(File.Exists(path), $"Expected KQL file not found: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void SetupScript_UsesIdempotentTableCreation()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-predictions-setup.kql");

        Assert.Contains(".create-merge table TemporalBehaviorStatePredictions", kql);
        Assert.DoesNotContain(".create table TemporalBehaviorStatePredictions", kql);
    }

    [Fact]
    public void SetupScript_UsesIdempotentMappingCreation()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-predictions-setup.kql");

        Assert.Contains(".create-or-alter table TemporalBehaviorStatePredictions ingestion csv mapping", kql);
    }

    [Fact]
    public void SetupScript_MappingUsesCorrectKustoSchema()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-predictions-setup.kql");

        Assert.Contains("\"column\":\"SessionId\"", kql);
        Assert.Contains("\"Properties\":{\"Ordinal\":\"0\"}", kql);
        Assert.DoesNotContain("\"Name\":\"SessionId\"", kql);
    }

    [Fact]
    public void SetupScript_VerificationQueryDeduplicatesDefensively()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-predictions-setup.kql");

        // Stage 4: dedup by the deterministic PredictionId column, not the raw composite key.
        // Stage 6/9: falls back to a legacy composite key for pre-migration rows whose
        // PredictionId is blank, so those rows don't all collapse into one logical prediction.
        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("legacy|", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
    }

    [Fact]
    public void SetupScript_TableAndMappingIncludePredictionId()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-predictions-setup.kql");

        Assert.Contains("PredictionId: string", kql);
        Assert.Contains("\"column\":\"PredictionId\",\"datatype\":\"string\",\"Properties\":{\"Ordinal\":\"11\"}", kql);
    }

    [Fact]
    public void AuditScript_UsesSharedNormalizationFunction()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-target-audit.kql");

        Assert.Contains("let NormalizeBehaviorState = (raw: string)", kql);
        Assert.Contains("\"missing\"", kql);
        Assert.Contains("\"unknown\"", kql);
        Assert.Contains("\"invalid\"", kql);
    }

    [Fact]
    public void AuditScript_DeduplicatesBeforeComputingDistributions()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-target-audit.kql");

        Assert.Contains("let DedupedTargets", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon", kql);
        // Section 1 is the one deliberate exception — it must query the RAW table to surface
        // duplicates, not the deduplicated view.
        Assert.Contains("Duplicate target rows (BEFORE dedup)", kql);
    }

    [Fact]
    public void EvaluationScript_UsesSharedNormalizationFunction()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-prediction-evaluation.kql");

        Assert.Contains("let NormalizeBehaviorState = (raw: string)", kql);
    }

    [Fact]
    public void EvaluationScript_DeduplicatesPredictionsAndTargets()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-prediction-evaluation.kql");

        Assert.Contains("let predictions = TemporalBehaviorStatePredictions", kql);
        // Stage 6/9: falls back to a legacy composite key for pre-migration rows whose
        // PredictionId is blank, so those rows don't all collapse into one logical prediction.
        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("legacy|", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
        Assert.Contains("let allTargets = TemporalPredictionTargets", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by SessionId, UserId, SourceWindowIndex, Horizon", kql);
    }

    [Fact]
    public void EvaluationScript_ForcesAllFiveCanonicalClassesInPerClassMetrics()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-prediction-evaluation.kql");

        Assert.Contains("classUniverse", kql);
        Assert.Contains("mv-expand ClassLabel to typeof(string)", kql);
    }

    [Fact]
    public void EvaluationScript_SegmentsBySimIdAndScenarioIdAndModelVersion()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-prediction-evaluation.kql");

        Assert.Contains("by SimId, Horizon, ModelVersion", kql);
        Assert.Contains("by SimId, ScenarioId, Horizon, ModelVersion", kql);
        Assert.Contains("by ModelVersion", kql);
    }

    [Fact]
    public void EvaluationScript_NoLongerUsesAdHocUnknownStringCheck()
    {
        var kql = ReadDocsKqlFile("phase10e-temporal-behavior-state-prediction-evaluation.kql");

        // The pre-Stage-12 pattern this file used to have on every section.
        Assert.DoesNotContain("isnotempty(TargetBehaviorState) and TargetBehaviorState != \"unknown\"", kql);
    }
}
