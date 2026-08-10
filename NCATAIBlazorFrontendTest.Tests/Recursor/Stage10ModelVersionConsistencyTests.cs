using Microsoft.Extensions.Configuration;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 10 corrective-pass tests: ModelVersionConsistencyValidator must catch the exact class
/// of bug found in this pass — Recursor:Models:TemporalElevatedRiskModelVersion configured as
/// "temporal-elevated-risk-v1" while TemporalElevatedRiskH1..3ModelPath all pointed at
/// "_v2.zip" files. ResolveVersionedModelPath silently discards a non-matching explicit path;
/// this validator is what makes that silent discard visible.
/// </summary>
public class Stage10ModelVersionConsistencyTests
{
    [Fact]
    public void HistoricalV1V2Mismatch_IsDetected()
    {
        // Reproduces the exact appsettings.json values found before this corrective pass.
        var problem = ModelVersionConsistencyValidator.CheckExplicitPathMatchesVersion(
            "TemporalElevatedRisk",
            horizon: 1,
            configuredVersion: "temporal-elevated-risk-v1",
            expectedSuffix: "v1",
            explicitConfigPath: "Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip");

        Assert.NotNull(problem);
        Assert.Contains("temporal-elevated-risk-v1", problem);
        Assert.Contains("temporal_elevated_risk_h1_v2.zip", problem);
    }

    [Fact]
    public void CheckAllHorizons_HistoricalConfig_ReportsAllThreeMismatches()
    {
        var problems = ModelVersionConsistencyValidator.CheckAllHorizons(
            "TemporalElevatedRisk",
            configuredVersion: "temporal-elevated-risk-v1",
            expectedSuffix: "v1",
            explicitH1: "Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip",
            explicitH2: "Recursor/TrainingModels/temporal_elevated_risk_h2_v2.zip",
            explicitH3: "Recursor/TrainingModels/temporal_elevated_risk_h3_v2.zip");

        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void CorrectedConfig_VersionMatchesPaths_NoMismatchReported()
    {
        // The fix applied to appsettings.json in this pass: bump the version to v2 to match the
        // already-configured _v2.zip paths.
        var problems = ModelVersionConsistencyValidator.CheckAllHorizons(
            "TemporalElevatedRisk",
            configuredVersion: "temporal-elevated-risk-v2",
            expectedSuffix: "v2",
            explicitH1: "Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip",
            explicitH2: "Recursor/TrainingModels/temporal_elevated_risk_h2_v2.zip",
            explicitH3: "Recursor/TrainingModels/temporal_elevated_risk_h3_v2.zip");

        Assert.Empty(problems);
    }

    [Fact]
    public void NoExplicitPathConfigured_NoMismatchReported()
    {
        // Nothing to compare against — auto-generation is used, which is always self-consistent.
        var problem = ModelVersionConsistencyValidator.CheckExplicitPathMatchesVersion(
            "TemporalBehaviorState", 1, "temporal-behavior-state-v1", "v1", explicitConfigPath: null);

        Assert.Null(problem);
    }

    // ── Real appsettings.json resolution (Stage 5 corrective pass) ─────────────
    //
    // The previous version of this test (renamed away below) hardcoded the exact strings it
    // claimed to be checking, so it could never fail no matter what appsettings.json actually
    // contained — including the historical bug where TemporalBehaviorStateH1..3ModelPath were
    // populated with legacy flat paths that silently overrode the canonical immutable
    // versions/{family}/{version}/ layout ModelVersionPublisher actually writes to. These tests
    // load the real Server/appsettings.json from disk and resolve it through the same production
    // code path (ResolveVersionedModelPath / ModelVersionPublisher.GetVersionDirectory) instead
    // of duplicating expected values as literals.

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Server", "appsettings.json")))
            dir = dir.Parent;

        Assert.NotNull(dir); // repo root (containing Server/appsettings.json) must be found by walking up from the test binary
        return dir!.FullName;
    }

    private static IConfigurationRoot LoadServerAppSettings(out string serverContentRoot)
    {
        var repoRoot = FindRepoRoot();
        serverContentRoot = Path.Combine(repoRoot, "Server");
        return new ConfigurationBuilder()
            .SetBasePath(serverContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
    }

    [Fact]
    public void CurrentAppSettingsJsonConfig_BehaviorStateHasNoDefaultExplicitHorizonPaths()
    {
        // Stage 4: normal/default configuration must be version-driven only — no populated
        // TemporalBehaviorStateH1/H2/H3ModelPath, which would silently bypass the canonical
        // versions/temporal-behavior-state/{version}/ directory ModelVersionPublisher writes to.
        var config = LoadServerAppSettings(out _);

        Assert.False(string.IsNullOrWhiteSpace(config["Recursor:Models:TemporalBehaviorStateModelVersion"]));
        Assert.True(string.IsNullOrWhiteSpace(config["Recursor:Models:TemporalBehaviorStateH1ModelPath"]));
        Assert.True(string.IsNullOrWhiteSpace(config["Recursor:Models:TemporalBehaviorStateH2ModelPath"]));
        Assert.True(string.IsNullOrWhiteSpace(config["Recursor:Models:TemporalBehaviorStateH3ModelPath"]));
    }

    [Fact]
    public void CurrentAppSettingsJsonConfig_BehaviorStateResolvesIntoImmutableVersionDirectory()
    {
        var config = LoadServerAppSettings(out var serverContentRoot);
        var version = config["Recursor:Models:TemporalBehaviorStateModelVersion"];
        Assert.False(string.IsNullOrWhiteSpace(version));

        var resolvedH1 = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(
            1, version!, serverContentRoot, config["Recursor:Models:TemporalBehaviorStateH1ModelPath"]);
        var resolvedH2 = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(
            2, version!, serverContentRoot, config["Recursor:Models:TemporalBehaviorStateH2ModelPath"]);
        var resolvedH3 = TemporalBehaviorStateModelTrainingService.ResolveVersionedModelPath(
            3, version!, serverContentRoot, config["Recursor:Models:TemporalBehaviorStateH3ModelPath"]);

        var expectedVersionDirectory = ModelVersionPublisher.GetVersionDirectory(
            serverContentRoot, TemporalBehaviorStateModelTrainingService.ModelFamily, version!);

        Assert.Equal(Path.Combine(expectedVersionDirectory, "h1.zip"), resolvedH1);
        Assert.Equal(Path.Combine(expectedVersionDirectory, "h2.zip"), resolvedH2);
        Assert.Equal(Path.Combine(expectedVersionDirectory, "h3.zip"), resolvedH3);
    }

    [Fact]
    public void CurrentAppSettingsJsonConfig_ElevatedRiskDocumentedLegacyOverrideRemainsConsistent()
    {
        // Documented decision (Stage 4 caution): elevated-risk v2 models are already deployed
        // via flat explicit H1..H3 paths. Migrating an already-working predictor to the
        // immutable version-directory layout purely for stylistic consistency is explicitly out
        // of scope for this pass, so its explicit overrides are deliberately left populated.
        // This guards against silently reintroducing the version/path drift this pass fixed for
        // TemporalElevatedRisk (configured version disagreeing with the _v{n}.zip suffix).
        var config = LoadServerAppSettings(out _);

        var version = config["Recursor:Models:TemporalElevatedRiskModelVersion"];
        var h1 = config["Recursor:Models:TemporalElevatedRiskH1ModelPath"];
        var h2 = config["Recursor:Models:TemporalElevatedRiskH2ModelPath"];
        var h3 = config["Recursor:Models:TemporalElevatedRiskH3ModelPath"];

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.False(string.IsNullOrWhiteSpace(h1));
        Assert.False(string.IsNullOrWhiteSpace(h2));
        Assert.False(string.IsNullOrWhiteSpace(h3));

        var expectedSuffix = TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix(version!);
        var problems = ModelVersionConsistencyValidator.CheckAllHorizons(
            "TemporalElevatedRisk", version!, expectedSuffix, h1, h2, h3);
        Assert.Empty(problems);
    }
}
