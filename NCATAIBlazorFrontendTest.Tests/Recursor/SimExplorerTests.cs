using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 16A tests: Sim Explorer DTOs, KQL builders, and graceful ADX fallback.
/// All tests are pure unit tests — no ADX, no real data, no DI container required.
/// </summary>
public class SimExplorerTests
{
    // ── DTO construction ──────────────────────────────────────────────────────

    [Fact]
    public void SimExplorerSimSummaryDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SimExplorerSimSummaryDto();
        Assert.Equal("", dto.SimId);
        Assert.Equal(0, dto.UserCount);
        Assert.Equal(0, dto.SessionCount);
        Assert.Equal(0, dto.WindowCount);
        Assert.Equal(0, dto.RawEventCount);
        Assert.Equal(0, dto.AdaptationCount);
        Assert.Equal(0, dto.ScenarioCount);
    }

    [Fact]
    public void SimExplorerUserSummaryDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SimExplorerUserSummaryDto();
        Assert.Equal("", dto.SimId);
        Assert.Equal("", dto.UserId);
        Assert.Equal(0, dto.SessionCount);
        Assert.Null(dto.AvgConfusionScore);
        Assert.Null(dto.AvgGoalUnderstanding);
        Assert.Null(dto.AvgHintDependenceScore);
    }

    [Fact]
    public void SimExplorerSessionSummaryDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SimExplorerSessionSummaryDto();
        Assert.Equal("", dto.SessionId);
        Assert.Equal("", dto.SimId);
        Assert.Equal("", dto.UserId);
        Assert.Equal("", dto.ScenarioId);
        Assert.Equal(0, dto.WindowCount);
        Assert.Equal(0, dto.RawEventCount);
        Assert.Equal(0, dto.AdaptationCount);
        Assert.Equal(0, dto.SafetyErrorCount);
        Assert.Equal(0, dto.ErrorCount);
        Assert.Equal(0, dto.SuccessCount);
        Assert.Null(dto.AvgConfusionScore);
    }

    [Fact]
    public void SimExplorerSessionDetailDto_DefaultConstruction_HasEmptyLists()
    {
        var dto = new SimExplorerSessionDetailDto();
        Assert.Equal("", dto.SessionId);
        Assert.Empty(dto.RawEvents);
        Assert.Empty(dto.EventCountsByCategory);
        Assert.Empty(dto.EventCountsByEventType);
        Assert.Null(dto.Timeline);
        Assert.False(dto.RawEventsTruncated);
    }

    [Fact]
    public void SimExplorerRawEventDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SimExplorerRawEventDto();
        Assert.Equal("", dto.EventId);
        Assert.Equal("", dto.EventType);
        Assert.Equal("", dto.Category);
        Assert.Equal("", dto.MetricsJson);
        Assert.Equal("", dto.ContextJson);
        Assert.Equal("", dto.PayloadJson);
    }

    [Fact]
    public void SimExplorerEventCountDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SimExplorerEventCountDto();
        Assert.Equal("", dto.Label);
        Assert.Equal(0, dto.Count);
    }

    // ── KQL content guards ────────────────────────────────────────────────────

    [Fact]
    public void SimsKql_ContainsBehaviorStateTrainingRows()
    {
        var kql = AdxSimExplorerQueryService.BuildSimsKql();
        Assert.Contains("BehaviorStateTrainingRows", kql);
    }

    [Fact]
    public void SimsKql_ContainsRawEvents()
    {
        var kql = AdxSimExplorerQueryService.BuildSimsKql();
        Assert.Contains("RawEvents", kql);
    }

    [Fact]
    public void SimsKql_ContainsAdaptationDecisions()
    {
        var kql = AdxSimExplorerQueryService.BuildSimsKql();
        Assert.Contains("AdaptationDecisions_v2", kql);
    }

    [Fact]
    public void SimsKql_DoesNotContain0L()
    {
        var kql = AdxSimExplorerQueryService.BuildSimsKql();
        Assert.DoesNotContain("0L", kql);
    }

    [Fact]
    public void UsersKql_FiltersSimId()
    {
        var kql = AdxSimExplorerQueryService.BuildUsersForSimKql("sim-abc");
        Assert.Contains("SimId == 'sim-abc'", kql);
    }

    [Fact]
    public void UsersKql_DoesNotContain0L()
    {
        var kql = AdxSimExplorerQueryService.BuildUsersForSimKql("sim-abc");
        Assert.DoesNotContain("0L", kql);
    }

    [Fact]
    public void SessionsKql_FiltersSimIdAndUserId()
    {
        var kql = AdxSimExplorerQueryService.BuildSessionsForSimUserKql("sim-x", "user-y");
        Assert.Contains("SimId == 'sim-x'", kql);
        Assert.Contains("UserId == 'user-y'", kql);
    }

    [Fact]
    public void SessionsKql_DoesNotContain0L()
    {
        var kql = AdxSimExplorerQueryService.BuildSessionsForSimUserKql("sim-x", "user-y");
        Assert.DoesNotContain("0L", kql);
    }

    [Fact]
    public void SessionsKql_ContainsSafetyErrorCountif()
    {
        var kql = AdxSimExplorerQueryService.BuildSessionsForSimUserKql("sim-x", "user-y");
        Assert.Contains("safety_error", kql);
    }

    [Fact]
    public void RawEventsKql_FiltersSessionId()
    {
        var kql = AdxSimExplorerQueryService.BuildRawEventsKql("sess-123");
        Assert.Contains("SessionId == 'sess-123'", kql);
    }

    [Fact]
    public void RawEventsKql_ContainsTake2000()
    {
        var kql = AdxSimExplorerQueryService.BuildRawEventsKql("sess-123");
        Assert.Contains("take 2000", kql);
    }

    [Fact]
    public void RawEventsKql_OrdersBySequenceNumber()
    {
        var kql = AdxSimExplorerQueryService.BuildRawEventsKql("sess-123");
        Assert.Contains("SequenceNumber asc", kql);
    }

    // ── Single-quote sanitization ─────────────────────────────────────────────
    // Sanitization is applied in the service methods before calling the static builders.
    // The builders themselves receive already-sanitized strings.

    [Fact]
    public void BuildUsersKql_EmbedsSanitizedSimIdLiterally()
    {
        // If a clean simId is passed the builder embeds it verbatim.
        var kql = AdxSimExplorerQueryService.BuildUsersForSimKql("sim-abc-123");
        Assert.Contains("'sim-abc-123'", kql);
    }

    [Fact]
    public void BuildSessionsKql_EmbedsBothParamsLiterally()
    {
        var kql = AdxSimExplorerQueryService.BuildSessionsForSimUserKql("simX", "userY");
        Assert.Contains("'simX'", kql);
        Assert.Contains("'userY'", kql);
    }

    // ── Graceful ADX fallback when no query provider is configured ────────────

    [Fact]
    public async Task GetSimsAsync_NoAdx_ReturnsEmpty()
    {
        var svc = BuildServiceWithoutAdx();
        var result = await svc.GetSimsAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsersForSimAsync_NoAdx_ReturnsEmpty()
    {
        var svc = BuildServiceWithoutAdx();
        var result = await svc.GetUsersForSimAsync("sim-abc");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionsForSimUserAsync_NoAdx_ReturnsEmpty()
    {
        var svc = BuildServiceWithoutAdx();
        var result = await svc.GetSessionsForSimUserAsync("sim-abc", "user-xyz");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionDetailAsync_NoAdx_ReturnsNull()
    {
        var svc = BuildServiceWithoutAdx();
        var result = await svc.GetSessionDetailAsync("sim-abc", "user-xyz", "sess-001");
        Assert.Null(result);
    }

    // ── DTO list deserialization with empty lists ─────────────────────────────

    [Fact]
    public void SimExplorerSessionDetailDto_WithEmptyRawEvents_SerializesAndDeserializes()
    {
        var dto = new SimExplorerSessionDetailDto
        {
            SessionId  = "s1",
            SimId      = "sim1",
            UserId     = "u1",
            ScenarioId = "sc1",
        };
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var back = System.Text.Json.JsonSerializer.Deserialize<SimExplorerSessionDetailDto>(json);
        Assert.NotNull(back);
        Assert.Empty(back!.RawEvents);
        Assert.Empty(back.EventCountsByCategory);
        Assert.Empty(back.EventCountsByEventType);
        Assert.False(back.RawEventsTruncated);
    }

    [Fact]
    public void SimExplorerRawEventDto_DynamicJsonFields_DefaultToEmptyString()
    {
        var dto = new SimExplorerRawEventDto();
        Assert.Equal("", dto.MetricsJson);
        Assert.Equal("", dto.ContextJson);
        Assert.Equal("", dto.PayloadJson);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdxSimExplorerQueryService BuildServiceWithoutAdx()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Adx:Database"] = "TestDb"
            })
            .Build();

        // Stub dashboard query service — ADX not available so it returns null/empty.
        var dashboardQuery = new NullDashboardQueryService();

        return new AdxSimExplorerQueryService(
            services,
            config,
            NullLogger<AdxSimExplorerQueryService>.Instance,
            dashboardQuery);
    }

    // Minimal stub that satisfies IAdxDashboardQueryService without ADX.
    private class NullDashboardQueryService : IAdxDashboardQueryService
    {
        public Task<DashboardQueryResult<DashboardSessionSummaryDto>> GetRecentSessionsAsync(int count = 20)
            => Task.FromResult(new DashboardQueryResult<DashboardSessionSummaryDto>());

        public Task<DashboardSingleQueryResult<SessionTimelineDto>> GetSessionTimelineAsync(
            string sessionId, string? riskModelVersion = null, string? behaviorStateModelVersion = null)
            => Task.FromResult(new DashboardSingleQueryResult<SessionTimelineDto>());

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
}
