using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Controllers;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using System.Text.Json;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10S-4: Trend-aware medical-supply debrief tests.
///
/// All tests are pure unit tests — no ADX, no GPT, no DI container.
/// Tests cover:
///   1. CountEventsFromRecords counts event types correctly.
///   2. Previous-session lookup excludes the current session ID.
///   3. Trend deltas are computed in code (not deferred to GPT).
///   4. No previous session → overallTrend = "insufficient_data".
///   5. BuildPromptPayload does not include raw event arrays.
///   6. Controller returns structured TrendAwareDebriefResponse.
///   7. Missing currentSessionId or userId returns useful error.
/// </summary>
public class Phase10S4MedicalSupplyDebriefTests
{
    // ── 1. CountEventsFromRecords ─────────────────────────────────────────────

    [Fact]
    public void CountEventsFromRecords_WrongBinErrors_CountedCorrectly()
    {
        var events = new[]
        {
            Event(MedicalSupplyEventTypes.WrongBinError),
            Event(MedicalSupplyEventTypes.WrongBinError),
            Event(MedicalSupplyEventTypes.SupplyStockedCorrectly),
        };

        var counts = MedicalSupplyDebriefHelper.CountEventsFromRecords(events);

        Assert.Equal(2, counts.WrongBinErrors);
    }

    [Fact]
    public void CountEventsFromRecords_SafetyErrors_IncludesDamagedExpiredAndSkippedInspection()
    {
        var events = new[]
        {
            Event(MedicalSupplyEventTypes.DamagedSupplyStocked),
            Event(MedicalSupplyEventTypes.ExpiredSupplyStocked),
            Event(MedicalSupplyEventTypes.InspectionSkipped),
            Event(MedicalSupplyEventTypes.SupplyStockedCorrectly),
        };

        var counts = MedicalSupplyDebriefHelper.CountEventsFromRecords(events);

        Assert.Equal(3, counts.SafetyErrors);
        Assert.Equal(1, counts.DamagedSupplyStocked);
    }

    [Fact]
    public void CountEventsFromRecords_SuccessCount_IncludesCorrectStockingAndRejection()
    {
        var events = new[]
        {
            Event(MedicalSupplyEventTypes.SupplyStockedCorrectly),
            Event(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly),
            Event(MedicalSupplyEventTypes.ExpiredSupplyRejectedCorrectly),
            Event(MedicalSupplyEventTypes.WrongBinError),
        };

        var counts = MedicalSupplyDebriefHelper.CountEventsFromRecords(events);

        Assert.Equal(3, counts.SuccessCount);
    }

    [Fact]
    public void CountEventsFromRecords_UsableSupplyRejected_CountedSeparatelyFromSafetyErrors()
    {
        var events = new[]
        {
            Event(MedicalSupplyEventTypes.UsableSupplyRejected),
            Event(MedicalSupplyEventTypes.UsableSupplyRejected),
            Event(MedicalSupplyEventTypes.DamagedSupplyStocked),
        };

        var counts = MedicalSupplyDebriefHelper.CountEventsFromRecords(events);

        Assert.Equal(2, counts.UsableSupplyRejected);
        Assert.Equal(1, counts.SafetyErrors);     // only DamagedSupplyStocked qualifies as safety error
        Assert.Equal(0, counts.WrongBinErrors);
    }

    [Fact]
    public void CountEventsFromRecords_EmptyList_ReturnsAllZeros()
    {
        var counts = MedicalSupplyDebriefHelper.CountEventsFromRecords([]);

        Assert.Equal(0, counts.WrongBinErrors);
        Assert.Equal(0, counts.SafetyErrors);
        Assert.Equal(0, counts.SuccessCount);
    }

    // ── 2. Previous-session lookup excludes current session ───────────────────

    [Fact]
    public async Task GenerateTrendAwareDebrief_PreviousSessionLookup_PassesCurrentSessionIdAsExclusion()
    {
        const string currentId  = "session-current-001";
        const string previousId = "session-previous-001";

        var fakeAdx = new FakeAdxDebriefQueryService { PreviousSessionIdToReturn = previousId };
        var fakeRecursorQuery = new FakeAdxRecursorQueryService();
        var fakeGpt = new FakeDebriefService();

        var request = new TrendAwareDebriefRequest
        {
            UserId            = "user-001",
            SimId             = "medical-supply-stocking",
            ScenarioId        = "basic-supply-room",
            CurrentSessionId  = currentId,
            IncludePreviousSession = true,
        };

        // Call service directly so we can inspect what IDs were passed to the ADX query.
        var svc = new MedicalSupplyDebriefService(
            fakeAdx, fakeRecursorQuery, NullLogger<MedicalSupplyDebriefService>.Instance);

        await svc.GenerateTrendAwareDebriefAsync(request);

        // The lookup must exclude the current session, not any other session.
        Assert.Equal(currentId, fakeAdx.LastExcludedSessionId);
    }

    [Fact]
    public async Task GenerateTrendAwareDebrief_ExplicitPreviousSessionId_DoesNotQueryAdxForPreviousId()
    {
        const string currentId  = "session-current-002";
        const string previousId = "session-explicit-previous";

        var fakeAdx = new FakeAdxDebriefQueryService();
        var fakeRecursorQuery = new FakeAdxRecursorQueryService();

        var request = new TrendAwareDebriefRequest
        {
            UserId             = "user-002",
            SimId              = "medical-supply-stocking",
            ScenarioId         = "basic-supply-room",
            CurrentSessionId   = currentId,
            PreviousSessionId  = previousId,  // explicit
            IncludePreviousSession = true,
        };

        var svc = new MedicalSupplyDebriefService(
            fakeAdx, fakeRecursorQuery, NullLogger<MedicalSupplyDebriefService>.Instance);

        await svc.GenerateTrendAwareDebriefAsync(request);

        // ADX previous-session lookup should be skipped when explicit ID is provided.
        Assert.False(fakeAdx.GetPreviousSessionWasCalled);
    }

    // ── 3. Trend deltas computed in code ────────────────────────────────────

    [Fact]
    public void ComputeDeltas_WithPreviousData_ComputesCorrectErrorDeltas()
    {
        var current  = Session("s-cur", wrongBin: 3, safetyErrors: 2, success: 10);
        var previous = Session("s-prev", wrongBin: 5, safetyErrors: 4, success: 8);

        var deltas = MedicalSupplyDebriefHelper.ComputeDeltas(current, previous);

        Assert.True(deltas.HasPreviousData);
        Assert.Equal(-2, deltas.WrongBinErrorDelta);    // 3 - 5 = -2 (improvement)
        Assert.Equal(-2, deltas.SafetyErrorDelta);      // 2 - 4 = -2 (improvement)
        Assert.Equal(2,  deltas.SuccessDelta);          // 10 - 8 = +2 (improvement)
    }

    [Fact]
    public void ComputeDeltas_WithBehaviorSummaries_ComputesCorrectScoreDeltas()
    {
        var current = Session("s-cur");
        current.BehaviorSummary = new MedicalSupplyBehaviorSummary
        {
            AvgConfusion        = 0.30,
            AvgSafetyCompliance = 0.85,
            AvgGoalUnderstanding = 0.70,
            DominantState       = "learning",
            WindowCount         = 3,
        };

        var previous = Session("s-prev");
        previous.BehaviorSummary = new MedicalSupplyBehaviorSummary
        {
            AvgConfusion        = 0.50,
            AvgSafetyCompliance = 0.65,
            AvgGoalUnderstanding = 0.55,
            DominantState       = "struggling",
            WindowCount         = 2,
        };

        var deltas = MedicalSupplyDebriefHelper.ComputeDeltas(current, previous);

        Assert.True(deltas.HasPreviousData);
        Assert.True(deltas.AvgConfusionDelta < 0);           // confusion improved
        Assert.True(deltas.AvgSafetyComplianceDelta > 0);    // safety improved
        Assert.True(deltas.AvgGoalUnderstandingDelta > 0);   // goal improved
        Assert.Equal("struggling → learning", deltas.DominantStateChange);
    }

    [Fact]
    public void ComputeDeltas_WithNoPreviousSession_HasPreviousDataIsFalse()
    {
        var current = Session("s-cur", wrongBin: 1, safetyErrors: 1, success: 5);

        var deltas = MedicalSupplyDebriefHelper.ComputeDeltas(current, null);

        Assert.False(deltas.HasPreviousData);
        Assert.Null(deltas.WrongBinErrorDelta);
        Assert.Null(deltas.SafetyErrorDelta);
        Assert.Null(deltas.AvgConfusionDelta);
    }

    // ── 4. No previous session → overallTrend = "insufficient_data" ─────────

    [Fact]
    public void ComputeOverallTrend_NoPreviousData_ReturnsInsufficientData()
    {
        var deltas = new MedicalSupplyTrendDeltas { HasPreviousData = false };

        var trend = MedicalSupplyDebriefHelper.ComputeOverallTrend(deltas);

        Assert.Equal("insufficient_data", trend);
    }

    [Fact]
    public void ComputeOverallTrend_AllMetricsImproved_ReturnsImproved()
    {
        var deltas = new MedicalSupplyTrendDeltas
        {
            HasPreviousData           = true,
            SafetyErrorDelta          = -3,   // improvement
            SuccessDelta              = 5,    // improvement
            AvgConfusionDelta         = -0.15, // improvement
            AvgSafetyComplianceDelta  = 0.10,  // improvement
            AvgGoalUnderstandingDelta = 0.12,  // improvement
        };

        var trend = MedicalSupplyDebriefHelper.ComputeOverallTrend(deltas);

        Assert.Equal("improved", trend);
    }

    [Fact]
    public void ComputeOverallTrend_AllMetricsWorsened_ReturnsWorsened()
    {
        var deltas = new MedicalSupplyTrendDeltas
        {
            HasPreviousData           = true,
            SafetyErrorDelta          = 4,    // worsened
            SuccessDelta              = -3,   // worsened
            AvgConfusionDelta         = 0.20, // worsened
            AvgSafetyComplianceDelta  = -0.15, // worsened
            AvgGoalUnderstandingDelta = -0.12, // worsened
        };

        var trend = MedicalSupplyDebriefHelper.ComputeOverallTrend(deltas);

        Assert.Equal("worsened", trend);
    }

    [Fact]
    public void ComputeOverallTrend_MixedSignals_ReturnsMixed()
    {
        var deltas = new MedicalSupplyTrendDeltas
        {
            HasPreviousData           = true,
            SafetyErrorDelta          = -1,   // improved
            SuccessDelta              = -2,   // worsened
            AvgConfusionDelta         = 0.10, // worsened
            AvgSafetyComplianceDelta  = 0.08, // improved
            AvgGoalUnderstandingDelta = null, // no data
        };

        var trend = MedicalSupplyDebriefHelper.ComputeOverallTrend(deltas);

        Assert.Equal("mixed", trend);
    }

    // ── 5. BuildPromptPayload does not include raw event arrays ──────────────

    [Fact]
    public void BuildPromptPayload_DoesNotContainRawEventsArray()
    {
        var current = Session("s-cur", wrongBin: 2, safetyErrors: 1, success: 7);
        var deltas  = new MedicalSupplyTrendDeltas { HasPreviousData = false };

        var payload = MedicalSupplyDebriefHelper.BuildPromptPayload(current, null, deltas, "insufficient_data");
        var json    = JsonSerializer.Serialize(payload);

        // The payload must not include raw event arrays from ADX.
        Assert.DoesNotContain("\"events\"", json);
        Assert.DoesNotContain("\"rawEvents\"", json);
        Assert.DoesNotContain("EventId", json);
        Assert.DoesNotContain("SequenceNumber", json);
    }

    [Fact]
    public void BuildPromptPayload_ContainsComputedCountsNotNull()
    {
        var current = Session("s-cur", wrongBin: 3, safetyErrors: 2, success: 8);
        var deltas  = new MedicalSupplyTrendDeltas { HasPreviousData = false };

        var payload = MedicalSupplyDebriefHelper.BuildPromptPayload(current, null, deltas, "insufficient_data");
        var json    = JsonSerializer.Serialize(payload);

        // Computed counts must appear in the payload for GPT context.
        Assert.Contains("wrongBinErrors", json);
        Assert.Contains("safetyErrors", json);
        Assert.Contains("successCount", json);
    }

    [Fact]
    public void BuildPromptPayload_WithPreviousSession_IncludesPreviousSessionBlock()
    {
        var current  = Session("s-cur",  wrongBin: 2, safetyErrors: 1, success: 7);
        var previous = Session("s-prev", wrongBin: 4, safetyErrors: 3, success: 5);
        var deltas   = MedicalSupplyDebriefHelper.ComputeDeltas(current, previous);

        var payload = MedicalSupplyDebriefHelper.BuildPromptPayload(current, previous, deltas, "improved");
        var json    = JsonSerializer.Serialize(payload);

        Assert.Contains("previousSession", json);
        Assert.Contains("trendDeltas", json);
        Assert.DoesNotContain("\"previousSession\":null", json);
    }

    // ── 6. Controller returns structured DTO ─────────────────────────────────

    [Fact]
    public async Task Controller_GenerateTrendAwareDebrief_ReturnsOkWithStructuredResponse()
    {
        var expected = new TrendAwareDebriefResponse
        {
            Headline     = "Good shift!",
            OverallTrend = "improved",
            CoachMessage = "Keep it up.",
        };

        var controller = new MedicalSupplyDebriefController(
            new FixedResultDebriefService(expected),
            NullLogger<MedicalSupplyDebriefController>.Instance);

        var request = ValidRequest();
        var result  = await controller.GenerateTrendAwareDebrief(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TrendAwareDebriefResponse>(ok.Value);
        Assert.Equal("improved", response.OverallTrend);
        Assert.Equal("Good shift!", response.Headline);
    }

    // ── 7. Missing currentSessionId or userId returns error ──────────────────

    [Fact]
    public async Task Controller_MissingCurrentSessionId_ReturnsBadRequest()
    {
        var controller = new MedicalSupplyDebriefController(
            new FixedResultDebriefService(new TrendAwareDebriefResponse()),
            NullLogger<MedicalSupplyDebriefController>.Instance);

        var request = ValidRequest();
        request.CurrentSessionId = "";

        var result = await controller.GenerateTrendAwareDebrief(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Controller_MissingUserId_ReturnsBadRequest()
    {
        var controller = new MedicalSupplyDebriefController(
            new FixedResultDebriefService(new TrendAwareDebriefResponse()),
            NullLogger<MedicalSupplyDebriefController>.Instance);

        var request = ValidRequest();
        request.UserId = "   ";

        var result = await controller.GenerateTrendAwareDebrief(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RawEventRecord Event(string eventType) => new()
    {
        EventId      = Guid.NewGuid().ToString(),
        EventType    = eventType,
        TimestampUtc = DateTime.UtcNow,
        Metrics      = new EventMetrics(),
    };

    private static MedicalSupplySessionSummary Session(
        string sessionId,
        int wrongBin     = 0,
        int safetyErrors = 0,
        int success      = 0) => new()
    {
        SessionId        = sessionId,
        PerformanceScore = 0.7,
        EventCounts      = new MedicalSupplyRawEventCounts
        {
            WrongBinErrors       = wrongBin,
            SafetyErrors         = safetyErrors,
            SuccessCount         = success,
        },
    };

    private static TrendAwareDebriefRequest ValidRequest() => new()
    {
        UserId           = "test-user-001",
        SimId            = "medical-supply-stocking",
        ScenarioId       = "basic-supply-room",
        CurrentSessionId = "session-001",
        PerformanceScore = 0.78,
        ErrorsText       = "Two wrong-bin placements.",
    };

    // ── Test doubles ──────────────────────────────────────────────────────────

    // Tracks which session ID was passed as the exclusion argument.
    private class FakeAdxDebriefQueryService : IAdxMedicalSupplyDebriefQueryService
    {
        public string? PreviousSessionIdToReturn { get; set; }
        public string? LastExcludedSessionId { get; private set; }
        public bool GetPreviousSessionWasCalled { get; private set; }

        public Task<MedicalSupplyRawEventCounts> GetRawEventCountsAsync(string sessionId)
            => Task.FromResult(new MedicalSupplyRawEventCounts());

        public Task<MedicalSupplyBehaviorSummary?> GetBehaviorSummaryAsync(string sessionId)
            => Task.FromResult<MedicalSupplyBehaviorSummary?>(null);

        public Task<string?> GetPreviousSessionIdAsync(
            string userId, string simId, string scenarioId, string currentSessionId)
        {
            GetPreviousSessionWasCalled = true;
            LastExcludedSessionId = currentSessionId;
            return Task.FromResult(PreviousSessionIdToReturn);
        }
    }

    private class FakeAdxRecursorQueryService : IAdxRecursorQueryService
    {
        public Task<IEnumerable<FeatureWindowRow>> GetLatestFeatureWindowsAsync(string sessionId, int count = 5)
            => Task.FromResult<IEnumerable<FeatureWindowRow>>([]);

        public Task<IEnumerable<BehaviorProfileRow>> GetLatestBehaviorProfilesAsync(string sessionId, int count = 5)
            => Task.FromResult<IEnumerable<BehaviorProfileRow>>([]);

        public Task<IEnumerable<HypothesisSetRow>> GetLatestHypothesisSetsAsync(string sessionId, int count = 5)
            => Task.FromResult<IEnumerable<HypothesisSetRow>>([]);

        public Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsAsync(string sessionId, int count = 5)
            => Task.FromResult<IEnumerable<AdaptationDecisionRow>>([]);

        public Task<IEnumerable<AdaptationDecisionRow>> GetLatestAdaptationDecisionsByUserAsync(string userId, int count = 10)
            => Task.FromResult<IEnumerable<AdaptationDecisionRow>>([]);

        public Task<long> GetTrainingRowCountByUserAsync(string userId)
            => Task.FromResult(0L);

        public Task<AdaptationDecisionRow?> GetBestMatchingDecisionForSignalsAsync(
            string userId, string sessionId, DateTime signalsTimestampUtc)
            => Task.FromResult<AdaptationDecisionRow?>(null);
    }

    private class FixedResultDebriefService : IMedicalSupplyDebriefService
    {
        private readonly TrendAwareDebriefResponse _response;
        public FixedResultDebriefService(TrendAwareDebriefResponse response) => _response = response;
        public Task<TrendAwareDebriefResponse> GenerateTrendAwareDebriefAsync(TrendAwareDebriefRequest _)
            => Task.FromResult(_response);
    }

    private class FakeDebriefService : IMedicalSupplyDebriefService
    {
        public Task<TrendAwareDebriefResponse> GenerateTrendAwareDebriefAsync(TrendAwareDebriefRequest _)
            => Task.FromResult(new TrendAwareDebriefResponse());
    }
}
