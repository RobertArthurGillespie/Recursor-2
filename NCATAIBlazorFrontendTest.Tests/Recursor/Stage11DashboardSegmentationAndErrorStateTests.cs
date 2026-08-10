using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 11 corrective-pass tests: the Phase 10E dashboard's model-comparison/coverage
/// endpoints must (a) support filtering the evaluated predictions by simulation/scenario/model
/// version/horizon, and (b) return a result that distinguishes a genuinely empty dataset from a
/// query that never ran — a bare empty list can never again be mistaken for "no data."
/// </summary>
public class Stage11DashboardSegmentationAndErrorStateTests
{
    // ── KQL filter construction ──────────────────────────────────────────────

    [Fact]
    public void ModelComparisonKql_NoFilters_HasNoWhereClauseOnPredictions()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql();

        // "All simulations" — the predictions source must not be filtered at all.
        Assert.DoesNotContain("where SimId", kql);
        Assert.DoesNotContain("where ScenarioId", kql);
    }

    [Fact]
    public void ModelComparisonKql_WithSimIdFilter_FiltersPredictionsBySimId()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql(simId: "medical-supply-stocking");

        Assert.Contains("SimId == 'medical-supply-stocking'", kql);
    }

    [Fact]
    public void ModelComparisonKql_WithAllFilters_CombinesThemWithAnd()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql(
            simId: "medical-supply-stocking", scenarioId: "scenario-1",
            modelVersion: "temporal-behavior-state-v2", horizon: 2);

        Assert.Contains("SimId == 'medical-supply-stocking' and ScenarioId == 'scenario-1' " +
                         "and ModelVersion == 'temporal-behavior-state-v2' and Horizon == 2", kql);
    }

    [Fact]
    public void CoverageKql_WithSimIdFilter_FiltersPredictionsBySimId()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateCoverageKql(simId: "sim-training-v1");

        Assert.Contains("SimId == 'sim-training-v1'", kql);
    }

    [Fact]
    public void FilterValues_AreSanitized_AgainstSingleQuoteInjection()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql(simId: "evil'; .drop table X;");

        Assert.DoesNotContain("';", kql);
    }

    // ── Query-result status distinguishes failure from empty ─────────────────

    [Fact]
    public async Task GetBehaviorStateModelComparisonAsync_NoAdxConfigured_ReturnsServiceUnavailable_NotEmptySuccess()
    {
        var service = BuildServiceWithoutAdx();

        var result = await service.GetBehaviorStateModelComparisonAsync();

        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
        Assert.Empty(result.Rows);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GetBehaviorStateCoverageAsync_NoAdxConfigured_ReturnsServiceUnavailable_NotEmptySuccess()
    {
        var service = BuildServiceWithoutAdx();

        var result = await service.GetBehaviorStateCoverageAsync();

        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void DashboardQueryResult_DefaultConstruction_IsSuccessWithEmptyRows()
    {
        // A caller that only checks "Rows.Count == 0" without looking at Status would still see
        // this as indistinguishable from a real empty dataset — which is correct, since a
        // freshly-constructed default result IS a genuine (unpopulated) success, never a
        // silently-swallowed failure. Failure paths must explicitly set a non-Success status.
        var result = new DashboardQueryResult<BehaviorStateCoverageRow>();

        Assert.Equal(DashboardQueryStatus.Success, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void DashboardQueryStatus_HasDistinctValuesForEachFailureMode()
    {
        var values = Enum.GetValues<DashboardQueryStatus>();
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Contains(DashboardQueryStatus.ServiceUnavailable, values);
        Assert.Contains(DashboardQueryStatus.AuthorizationFailed, values);
        Assert.Contains(DashboardQueryStatus.SchemaMissing, values);
        Assert.Contains(DashboardQueryStatus.QueryFailed, values);
    }

    private static AdxDashboardQueryService BuildServiceWithoutAdx()
    {
        var services = new ServiceCollection().BuildServiceProvider(); // no ICslQueryProvider registered
        var config = new ConfigurationBuilder().Build();
        return new AdxDashboardQueryService(services, config, NullLogger<AdxDashboardQueryService>.Instance);
    }
}
