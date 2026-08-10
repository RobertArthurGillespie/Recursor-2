using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10D-4 tests: elevated-risk model versioning.
/// Covers version defaults, configured versions, path generation, sanitization,
/// comparison KQL structure, and confirmation that no adaptation behavior changes.
///
/// All tests are pure unit tests. No ADX, no real model files, no DI container.
/// </summary>
public class Phase10D4ElevatedRiskVersioningTests
{
    // ── Default model version ─────────────────────────────────────────────────

    [Fact]
    public void ModelVersion_Const_IsV1()
    {
        Assert.Equal("temporal-elevated-risk-v1", TemporalElevatedRiskPredictionService.ModelVersion);
    }

    [Fact]
    public void PredictionService_NoVersionArg_UsesDefaultConst()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null);

        Assert.Equal(TemporalElevatedRiskPredictionService.ModelVersion, svc.LoadedModelVersion);
    }

    [Fact]
    public void PredictionService_EmptyVersionArg_FallsBackToDefault()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null,
            modelVersion: "");

        Assert.Equal(TemporalElevatedRiskPredictionService.ModelVersion, svc.LoadedModelVersion);
    }

    [Fact]
    public void PredictionService_WhitespaceVersionArg_FallsBackToDefault()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null,
            modelVersion: "   ");

        Assert.Equal(TemporalElevatedRiskPredictionService.ModelVersion, svc.LoadedModelVersion);
    }

    // ── Configured model version stamped onto predictions ────────────────────

    [Fact]
    public void PredictionService_ConfiguredVersion_StoredAsLoadedModelVersion()
    {
        var svc = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null,
            modelVersion: "temporal-elevated-risk-v2");

        Assert.Equal("temporal-elevated-risk-v2", svc.LoadedModelVersion);
    }

    [Fact]
    public void PredictionService_V1AndV2_HaveDifferentLoadedVersions()
    {
        var svcV1 = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null,
            modelVersion: "temporal-elevated-risk-v1");

        var svcV2 = new TemporalElevatedRiskPredictionService(
            NullLogger<TemporalElevatedRiskPredictionService>.Instance,
            null, null, null,
            modelVersion: "temporal-elevated-risk-v2");

        Assert.NotEqual(svcV1.LoadedModelVersion, svcV2.LoadedModelVersion);
    }

    // ── Training report includes version ─────────────────────────────────────

    [Fact]
    public void TrainingReport_DefaultConstruction_ModelVersionIsEmpty()
    {
        var report = new TemporalElevatedRiskTrainingReport();
        Assert.Equal("", report.ModelVersion);
    }

    [Fact]
    public void TrainingReport_DefaultConstruction_GeneratedAtUtcIsSet()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var report = new TemporalElevatedRiskTrainingReport();
        Assert.True(report.GeneratedAtUtc >= before);
    }

    [Fact]
    public void TrainingReport_DefaultConstruction_WarningIsNotEmpty()
    {
        var report = new TemporalElevatedRiskTrainingReport();
        Assert.False(string.IsNullOrWhiteSpace(report.Warning));
    }

    // ── SanitizeModelVersion ──────────────────────────────────────────────────

    [Theory]
    [InlineData("temporal-elevated-risk-v1", "temporal-elevated-risk-v1")]
    [InlineData("temporal-elevated-risk-v2", "temporal-elevated-risk-v2")]
    [InlineData("my_model.v2",               "my_model.v2")]
    public void SanitizeModelVersion_AllowedChars_PassThrough(string input, string expected)
    {
        Assert.Equal(expected, TemporalElevatedRiskModelTrainingService.SanitizeModelVersion(input));
    }

    [Theory]
    [InlineData("../etc/passwd",         "..etcpasswd")]   // slashes stripped; dots remain (allowed)
    [InlineData("unsafe!@#$%^&*()",      "unsafe")]
    [InlineData("C:\\Windows\\System32", "CWindowsSystem32")]
    [InlineData("foo bar",               "foobar")]
    public void SanitizeModelVersion_UnsafeChars_Stripped(string input, string expected)
    {
        Assert.Equal(expected, TemporalElevatedRiskModelTrainingService.SanitizeModelVersion(input));
    }

    [Fact]
    public void SanitizeModelVersion_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", TemporalElevatedRiskModelTrainingService.SanitizeModelVersion(""));
    }

    // ── ExtractVersionSuffix ──────────────────────────────────────────────────

    [Theory]
    [InlineData("temporal-elevated-risk-v1", "v1")]
    [InlineData("temporal-elevated-risk-v2", "v2")]
    [InlineData("temporal-elevated-risk-v10", "v10")]
    public void ExtractVersionSuffix_StandardVersionString_ExtractsSuffix(string input, string expected)
    {
        Assert.Equal(expected, TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix(input));
    }

    [Fact]
    public void ExtractVersionSuffix_NoDash_ReturnsSanitizedFull()
    {
        // No dash → falls back to sanitized full string
        Assert.Equal("mymodel", TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix("mymodel"));
    }

    [Fact]
    public void ExtractVersionSuffix_EmptyInput_ReturnsV1Fallback()
    {
        Assert.Equal("v1", TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix(""));
    }

    // ── Versioned paths do not collide ───────────────────────────────────────

    [Fact]
    public void ExtractVersionSuffix_V1AndV2_ProduceDifferentSuffixes()
    {
        var suffixV1 = TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix("temporal-elevated-risk-v1");
        var suffixV2 = TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix("temporal-elevated-risk-v2");
        Assert.NotEqual(suffixV1, suffixV2);
    }

    [Fact]
    public void ExtractVersionSuffix_V1AndV2_ProduceExpectedFilenames()
    {
        var suffixV1 = TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix("temporal-elevated-risk-v1");
        var suffixV2 = TemporalElevatedRiskModelTrainingService.ExtractVersionSuffix("temporal-elevated-risk-v2");

        var fileV1 = $"temporal_elevated_risk_h1_{suffixV1}.zip";
        var fileV2 = $"temporal_elevated_risk_h1_{suffixV2}.zip";

        Assert.Equal("temporal_elevated_risk_h1_v1.zip", fileV1);
        Assert.Equal("temporal_elevated_risk_h1_v2.zip", fileV2);
        Assert.NotEqual(fileV1, fileV2);
    }

    // ── ResolveVersionedModelPath (static) ───────────────────────────────────

    [Fact]
    public void ResolveVersionedModelPath_V1_WithMatchingConfigPath_UsesConfigPath()
    {
        var configPath = "Recursor/TrainingModels/temporal_elevated_risk_h1_v1.zip";
        var result = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-elevated-risk-v1", "C:/app", configPath);

        Assert.EndsWith("temporal_elevated_risk_h1_v1.zip", result.Replace('\\', '/'));
        Assert.Contains("Recursor/TrainingModels", result.Replace('\\', '/'));
    }

    [Fact]
    public void ResolveVersionedModelPath_V2ConfiguredWithV1ConfigPath_HonorsConfigPathVerbatim()
    {
        // Stage 3 (corrective pass): an explicit config path is always honored verbatim, even if
        // its filename doesn't look like it matches the configured version — silently discarding
        // a configured override in favor of an auto-generated path was itself a bug. Version
        // consistency for an explicit override is now checked separately (and only against a
        // manifest.json, if present) by ModelVersionManifestValidator.ValidateExplicitOverridePath.
        var configPath = "Recursor/TrainingModels/temporal_elevated_risk_h1_v1.zip";
        var result = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-elevated-risk-v2", "C:/app", configPath);

        Assert.EndsWith("temporal_elevated_risk_h1_v1.zip", result.Replace('\\', '/'));
    }

    [Fact]
    public void ResolveVersionedModelPath_V2_WithNoConfigPath_ResolvesToVersionDirectory()
    {
        // Stage 3 (corrective pass): with no explicit override, resolution must match the
        // immutable directory layout ModelVersionPublisher actually writes to.
        var result = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            2, "temporal-elevated-risk-v2", "C:/app", null);

        var expected = Path.Combine(
            ModelVersionPublisher.GetVersionDirectory("C:/app", TemporalElevatedRiskModelTrainingService.ModelFamily, "temporal-elevated-risk-v2"),
            "h2.zip");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveVersionedModelPath_V1AndV2_ProduceDifferentPaths()
    {
        var pathV1 = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-elevated-risk-v1", "C:/app", null);
        var pathV2 = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-elevated-risk-v2", "C:/app", null);

        Assert.NotEqual(pathV1, pathV2);
    }

    [Fact]
    public void ResolveVersionedModelPath_V2_ExplicitMatchingConfigPath_UsesExplicitPath()
    {
        // Explicit config path for v2 matches the expected filename — use it.
        var configPath = "Custom/Models/temporal_elevated_risk_h1_v2.zip";
        var result = TemporalElevatedRiskModelTrainingService.ResolveVersionedModelPath(
            1, "temporal-elevated-risk-v2", "C:/app", configPath);

        Assert.EndsWith("temporal_elevated_risk_h1_v2.zip", result.Replace('\\', '/'));
        Assert.Contains("Custom/Models", result.Replace('\\', '/'));
    }

    // ── Model comparison KQL ──────────────────────────────────────────────────

    [Fact]
    public void ModelComparisonKql_GroupsByModelVersionAndHorizon()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("by ModelVersion, Horizon", kql);
    }

    [Fact]
    public void ModelComparisonKql_ComputesPrecision()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("ElevatedPrecision", kql);
    }

    [Fact]
    public void ModelComparisonKql_ComputesRecall()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("ElevatedRecall", kql);
    }

    [Fact]
    public void ModelComparisonKql_ComputesF1()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("ElevatedF1", kql);
    }

    [Fact]
    public void ModelComparisonKql_DoesNotContainInvalidLiteral_0L()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.DoesNotContain("0L", kql);
    }

    [Fact]
    public void ModelComparisonKql_JoinsToTemporalPredictionTargets()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("TemporalPredictionTargets", kql);
    }

    [Fact]
    public void ModelComparisonKql_CollapsesMediumHighToElevated()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        // The KQL derives TargetElevatedRisk using iff(TargetNearTermRisk == "low", ...)
        Assert.Contains("TargetNearTermRisk", kql);
        Assert.Contains("elevated", kql);
    }

    [Fact]
    public void ModelComparisonKql_ProjectsAllExpectedColumns()
    {
        var kql = AdxDashboardQueryService.BuildElevatedRiskModelComparisonKql();
        Assert.Contains("| project ModelVersion, Horizon, Total, Correct, Accuracy", kql);
        Assert.Contains("ElevatedPrecision, ElevatedRecall, ElevatedF1", kql);
    }

    // ── ElevatedRiskModelComparisonRow DTO ────────────────────────────────────

    [Fact]
    public void ElevatedRiskModelComparisonRow_DefaultConstruction_HasSensibleDefaults()
    {
        var row = new ElevatedRiskModelComparisonRow();
        Assert.Equal("", row.ModelVersion);
        Assert.Equal(0, row.Horizon);
        Assert.Equal(0, row.Total);
        Assert.Equal(0.0, row.Accuracy);
        Assert.Null(row.ElevatedPrecision);
        Assert.Null(row.ElevatedRecall);
        Assert.Null(row.ElevatedF1);
    }

    // ── Adaptation behavior unchanged ─────────────────────────────────────────

    [Fact]
    public void PredictionService_LoadedModelVersion_IsReadOnly()
    {
        // LoadedModelVersion is a get-only property — cannot be mutated externally.
        var prop = typeof(TemporalElevatedRiskPredictionService).GetProperty("LoadedModelVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void PredictionService_Predict_ResultHasNoAdaptationFields()
    {
        // TemporalElevatedRiskPredictionResult must not expose any adaptation-related fields.
        var type = typeof(TemporalElevatedRiskPredictionResult);
        var propNames = type.GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("ParameterChanges", propNames);
        Assert.DoesNotContain("AdaptationDecision", propNames);
        Assert.DoesNotContain("InterventionFamily", propNames);
    }

    [Fact]
    public void TrainingService_Interface_ExposesOnlyTrainAsync()
    {
        // The ITemporalElevatedRiskModelTrainingService interface must not expose any
        // methods that could influence adaptation decisions at runtime.
        var methods = typeof(ITemporalElevatedRiskModelTrainingService)
            .GetMethods()
            .Select(m => m.Name)
            .ToList();

        Assert.Contains("TrainAsync", methods);
        Assert.DoesNotContain("Predict", methods);
        Assert.DoesNotContain("Adapt", methods);
        Assert.DoesNotContain("Suppress", methods);
    }
}
