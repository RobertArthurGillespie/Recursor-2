using Microsoft.AspNetCore.Mvc;
using NCATAIBlazorFrontendTest.Server.Controllers;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 7 corrective-pass tests: session list/detail must distinguish a genuinely empty ADX
/// result from a query that failed outright — the exact bug this stage fixes is that
/// GetRecentSessionsAsync/GetSessionTimelineAsync used to swallow every ADX exception into an
/// empty list / null, making "No sessions found" and "Session not found" indistinguishable from
/// an ADX outage. RecursorDashboardController must only ever return 404 when the query
/// succeeded and genuinely found nothing — any other Status is returned as 200 with the typed
/// Status/ErrorMessage in the body, matching the existing Stage-11 convention for the
/// behavior-state comparison/coverage endpoints.
/// </summary>
public class Stage7DashboardTypedErrorHandlingTests
{
    // Minimal stub — only GetSessionTimelineAsync is exercised by these tests.
    private class StubDashboardQueryService : IAdxDashboardQueryService
    {
        public DashboardSingleQueryResult<SessionTimelineDto> TimelineResult { get; set; } = new();

        public Task<DashboardQueryResult<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20)
            => Task.FromResult(new DashboardQueryResult<DashboardSessionSummaryDto>());

        public Task<DashboardSingleQueryResult<SessionTimelineDto>> GetSessionTimelineAsync(
            string sessionId, string? riskModelVersion = null, string? behaviorStateModelVersion = null)
            => Task.FromResult(TimelineResult);

        public Task<DashboardQueryResult<string>> GetAvailableBehaviorStateModelVersionsAsync(string sessionId)
            => Task.FromResult(new DashboardQueryResult<string>());

        public Task<DashboardQueryResult<ElevatedRiskModelComparisonRow>> GetElevatedRiskModelComparisonAsync()
            => Task.FromResult(new DashboardQueryResult<ElevatedRiskModelComparisonRow>());

        public Task<DashboardQueryResult<BehaviorStateModelComparisonRow>> GetBehaviorStateModelComparisonAsync(
            string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
            => Task.FromResult(new DashboardQueryResult<BehaviorStateModelComparisonRow>());

        public Task<DashboardQueryResult<BehaviorStateCoverageRow>> GetBehaviorStateCoverageAsync(
            string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
            => Task.FromResult(new DashboardQueryResult<BehaviorStateCoverageRow>());

        public Task<DashboardQueryResult<BehaviorStateConfusionMatrixCell>> GetBehaviorStateConfusionMatrixAsync(
            string? simId = null, string? scenarioId = null, string? modelVersion = null, int? horizon = null)
            => Task.FromResult(new DashboardQueryResult<BehaviorStateConfusionMatrixCell>());
    }

    [Fact]
    public async Task GetSessionTimeline_QuerySucceededButSessionMissing_Returns404()
    {
        var stub = new StubDashboardQueryService
        {
            TimelineResult = new DashboardSingleQueryResult<SessionTimelineDto> { Status = DashboardQueryStatus.Success, Value = null },
        };
        var controller = new RecursorDashboardController(stub);

        var result = await controller.GetSessionTimeline("sess-missing");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetSessionTimeline_QuerySucceededWithData_Returns200WithTimeline()
    {
        var timeline = new SessionTimelineDto { SessionId = "sess-1", TotalWindows = 5 };
        var stub = new StubDashboardQueryService
        {
            TimelineResult = new DashboardSingleQueryResult<SessionTimelineDto> { Status = DashboardQueryStatus.Success, Value = timeline },
        };
        var controller = new RecursorDashboardController(stub);

        var result = await controller.GetSessionTimeline("sess-1");

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<DashboardSingleQueryResult<SessionTimelineDto>>(ok.Value);
        Assert.Equal(DashboardQueryStatus.Success, body.Status);
        Assert.Equal("sess-1", body.Value!.SessionId);
    }

    [Theory]
    [InlineData(DashboardQueryStatus.ServiceUnavailable)]
    [InlineData(DashboardQueryStatus.AuthorizationFailed)]
    [InlineData(DashboardQueryStatus.SchemaMissing)]
    [InlineData(DashboardQueryStatus.QueryFailed)]
    public async Task GetSessionTimeline_QueryFailed_ReturnsControlledDiagnosticState_Not404(DashboardQueryStatus status)
    {
        // This is the exact bug this stage fixes: previously any failure inside
        // GetSessionTimelineAsync's underlying queries was swallowed into an empty rows list,
        // which GetSessionTimelineAsync then reinterpreted as "no such session" -> 404. A real
        // ADX outage must never be indistinguishable from "session not found".
        var stub = new StubDashboardQueryService
        {
            TimelineResult = new DashboardSingleQueryResult<SessionTimelineDto>
            {
                Status = status,
                ErrorMessage = "simulated failure",
            },
        };
        var controller = new RecursorDashboardController(stub);

        var result = await controller.GetSessionTimeline("sess-1");

        Assert.IsNotType<NotFoundObjectResult>(result);
        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<DashboardSingleQueryResult<SessionTimelineDto>>(ok.Value);
        Assert.Equal(status, body.Status);
        Assert.Null(body.Value);
        Assert.Equal("simulated failure", body.ErrorMessage);
    }
}

/// <summary>
/// Stage 7 corrective-pass tests: the behavior-state confusion matrix must reuse the same
/// normalization/dedup/join contract as the existing model-comparison/coverage KQL, and must
/// always emit all 5x5 canonical-class cells so a cell with zero count is never silently absent
/// (matching the Stage 8 "never let a class vanish" rule already applied to per-class metrics).
/// </summary>
public class Stage7ConfusionMatrixKqlTests
{
    [Fact]
    public void BuildBehaviorStateConfusionMatrixKql_DedupsPredictionsByLogicalPredictionId()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateConfusionMatrixKql();

        // Stage 6/7: falls back to a legacy composite key for blank/pre-migration PredictionId.
        Assert.Contains("isempty(PredictionId)", kql);
        Assert.Contains("arg_max(CreatedAtUtc, *) by LogicalPredictionId", kql);
    }

    [Fact]
    public void BuildBehaviorStateConfusionMatrixKql_UsesSharedNormalizationBlock()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateConfusionMatrixKql();

        Assert.Contains("let NormalizeBehaviorState = (raw: string)", kql);
        Assert.Contains("\"steady\"", kql);
    }

    [Fact]
    public void BuildBehaviorStateConfusionMatrixKql_CrossJoinsClassUniverseForAllCells()
    {
        var kql = AdxDashboardQueryService.BuildBehaviorStateConfusionMatrixKql();

        // The cross-join of ActualClass x PredictedClass over the canonical class universe is
        // what guarantees all 25 cells are always emitted, even with zero count.
        Assert.Contains("let classUniverse", kql);
        Assert.Contains("let cellUniverse", kql);
        Assert.Contains("mv-expand ClassLabel to typeof(string)", kql);
        Assert.Contains("Count = coalesce(Count, 0)", kql);
    }

    [Fact]
    public void BuildBehaviorStateConfusionMatrixKql_AppliesSimScenarioVersionHorizonFilters()
    {
        var filtered = AdxDashboardQueryService.BuildBehaviorStateConfusionMatrixKql(
            simId: "medical-supply-stocking", scenarioId: "scenario-1", modelVersion: "temporal-behavior-state-v2", horizon: 2);
        var unfiltered = AdxDashboardQueryService.BuildBehaviorStateConfusionMatrixKql();

        Assert.Contains("SimId == 'medical-supply-stocking'", filtered);
        Assert.Contains("ScenarioId == 'scenario-1'", filtered);
        Assert.Contains("ModelVersion == 'temporal-behavior-state-v2'", filtered);
        Assert.Contains("Horizon == 2", filtered);
        Assert.NotEqual(filtered, unfiltered);
    }
}
