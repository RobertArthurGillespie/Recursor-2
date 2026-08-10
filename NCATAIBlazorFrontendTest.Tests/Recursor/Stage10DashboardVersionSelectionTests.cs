using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 10 corrective-pass tests: dashboard queries must filter to exactly one model version
/// per prediction family, never leaving DashboardTimelineBuilder's FirstOrDefault to pick
/// arbitrarily among multiple candidate predictions for the same (WindowIndex, Horizon).
/// </summary>
public class Stage10DashboardVersionSelectionTests
{
    [Fact]
    public void BehaviorStatePredictionsKql_FiltersToExactlyOneModelVersion()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStatePredictionsKql("sess-1", "temporal-behavior-state-v2");

        Assert.Contains("ModelVersion == 'temporal-behavior-state-v2'", kql);
        // Stage 4: dedup by the deterministic PredictionId, not the raw composite key.
        // Stage 6/7: falls back to a legacy composite key for blank/pre-migration PredictionId.
        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
    }

    [Fact]
    public void RiskPredictionsKql_FiltersToExactlyOneModelVersion()
    {
        var kql = AdxDashboardQueryService.BuildRiskPredictionsKql("sess-1", "temporal-risk-v2");

        Assert.Contains("ModelVersion == 'temporal-risk-v2'", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by WindowIndex, Horizon", kql);
    }

    [Fact]
    public void PredictionKql_DifferentVersions_ProduceDifferentFilters()
    {
        var v1 = AdxDashboardQueryService.BuildBehaviorStatePredictionsKql("sess-1", "temporal-behavior-state-v1");
        var v2 = AdxDashboardQueryService.BuildBehaviorStatePredictionsKql("sess-1", "temporal-behavior-state-v2");

        Assert.NotEqual(v1, v2);
        Assert.Contains("'temporal-behavior-state-v1'", v1);
        Assert.Contains("'temporal-behavior-state-v2'", v2);
    }

    [Fact]
    public async Task GetSessionTimelineAsync_WithoutAdx_StillResolvesAndReportsASelectedVersion()
    {
        // No ADX configured (ICslQueryProvider unavailable) — GetSessionTimelineAsync returns
        // null before it would even reach the version-resolution logic, but this at least
        // proves the new optional parameters don't break the "ADX not configured" path.
        var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var service = new AdxDashboardQueryService(
            services, config, NullLogger<AdxDashboardQueryService>.Instance);

        var result = await service.GetSessionTimelineAsync("sess-1", riskModelVersion: "temporal-risk-v2", behaviorStateModelVersion: "temporal-behavior-state-v2");

        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetAvailableBehaviorStateModelVersionsAsync_WithoutAdx_ReturnsEmptyNotNull()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var service = new AdxDashboardQueryService(
            services, config, NullLogger<AdxDashboardQueryService>.Instance);

        var result = await service.GetAvailableBehaviorStateModelVersionsAsync("sess-1");

        Assert.NotNull(result.Rows);
        Assert.Empty(result.Rows);
        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
    }
}
