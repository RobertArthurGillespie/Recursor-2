using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Recursor.Adx;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 14A tests: dashboard DTOs, timeline assembly, correctness computation,
/// and graceful ADX fallback when no query provider is configured.
///
/// All tests are pure unit tests. No ADX, no real data, no DI container required.
/// </summary>
public class Phase14ADashboardTests
{
    // ── DTO construction ──────────────────────────────────────────────────────

    [Fact]
    public void DashboardSessionSummaryDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new DashboardSessionSummaryDto();
        Assert.Equal("", dto.SessionId);
        Assert.Equal("", dto.UserId);
        Assert.Equal(0, dto.WindowCount);
        Assert.Equal(0, dto.AdaptationCount);
        Assert.Equal(0, dto.PredictionCount);
    }

    [Fact]
    public void SessionTimelineDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new SessionTimelineDto();
        Assert.Equal("", dto.SessionId);
        Assert.Equal(0, dto.TotalWindows);
        Assert.Null(dto.AveragePredictionConfidence);
        Assert.Null(dto.H1PredictionAccuracy);
        Assert.Null(dto.H2PredictionAccuracy);
        Assert.Null(dto.H3PredictionAccuracy);
        Assert.Empty(dto.Windows);
    }

    [Fact]
    public void WindowTimelineDto_DefaultConstruction_HasSensibleDefaults()
    {
        var dto = new WindowTimelineDto();
        Assert.Equal(0, dto.WindowIndex);
        Assert.Null(dto.AdaptationDecision);
        Assert.Null(dto.H1PredictionCorrect);
        Assert.Null(dto.H2PredictionCorrect);
        Assert.Null(dto.H3PredictionCorrect);
        Assert.Empty(dto.TemporalRiskPredictions);
        Assert.Empty(dto.TemporalPredictionTargets);
    }

    [Fact]
    public void AdaptationDecisionTimelineDto_DefaultConstruction_NullableNotesAreNull()
    {
        var dto = new AdaptationDecisionTimelineDto();
        Assert.Null(dto.Phase8AGuardrailNotes);
        Assert.Null(dto.Phase8EReliabilityNotes);
        Assert.Null(dto.Phase10TrajectoryNotes);
        Assert.Equal("", dto.ReasoningSummary);
        // Explanation fields default to null (older rows lack them)
        Assert.Null(dto.HypothesisLabelsJson);
        Assert.Null(dto.LearnerStateSummary);
        Assert.Null(dto.WhySupportChanged);
        Assert.Null(dto.CoachMessage);
        Assert.Null(dto.ConfidenceNote);
    }

    // ── DashboardTimelineBuilder.ComputeCorrectness ───────────────────────────

    [Fact]
    public void ComputeCorrectness_BothAbsent_ReturnsNull()
    {
        var result = DashboardTimelineBuilder.ComputeCorrectness([], [], horizon: 1);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCorrectness_NoPrediction_ReturnsNull()
    {
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetNearTermRisk = "high" }
        };
        var result = DashboardTimelineBuilder.ComputeCorrectness([], targets, horizon: 1);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCorrectness_NoTarget_ReturnsNull()
    {
        var preds = new List<TemporalRiskPredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedNearTermRisk = "high" }
        };
        var result = DashboardTimelineBuilder.ComputeCorrectness(preds, [], horizon: 1);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCorrectness_EmptyTargetRisk_ReturnsNull()
    {
        var preds = new List<TemporalRiskPredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedNearTermRisk = "high" }
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetNearTermRisk = "" }
        };
        var result = DashboardTimelineBuilder.ComputeCorrectness(preds, targets, horizon: 1);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCorrectness_MatchingRisk_ReturnsTrue()
    {
        var preds = new List<TemporalRiskPredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedNearTermRisk = "high" }
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetNearTermRisk = "high" }
        };
        var result = DashboardTimelineBuilder.ComputeCorrectness(preds, targets, horizon: 1);
        Assert.True(result);
    }

    [Fact]
    public void ComputeCorrectness_MismatchedRisk_ReturnsFalse()
    {
        var preds = new List<TemporalRiskPredictionTimelineDto>
        {
            new() { Horizon = 2, PredictedNearTermRisk = "low" }
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 2, TargetNearTermRisk = "high" }
        };
        var result = DashboardTimelineBuilder.ComputeCorrectness(preds, targets, horizon: 2);
        Assert.False(result);
    }

    [Fact]
    public void ComputeCorrectness_WrongHorizon_ReturnsNull()
    {
        var preds = new List<TemporalRiskPredictionTimelineDto>
        {
            new() { Horizon = 1, PredictedNearTermRisk = "low" }
        };
        var targets = new List<TemporalTargetTimelineDto>
        {
            new() { Horizon = 1, TargetNearTermRisk = "low" }
        };
        // Asking for H2 but only H1 data present
        var result = DashboardTimelineBuilder.ComputeCorrectness(preds, targets, horizon: 2);
        Assert.Null(result);
    }

    // ── DashboardTimelineBuilder.Build ─────────────────────────────────────────

    [Fact]
    public void Build_EmptyRows_ReturnsEmptyTimeline()
    {
        var result = DashboardTimelineBuilder.Build("sess-1", [], [], [], [], [], []);
        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal(0, result.TotalWindows);
        Assert.Equal(0, result.TotalAdaptations);
        Assert.Equal(0, result.TotalPredictions);
        Assert.Null(result.AveragePredictionConfidence);
        Assert.Null(result.H1PredictionAccuracy);
        Assert.Empty(result.Windows);
    }

    [Fact]
    public void Build_OneWindowNoAdaptation_CorrectCounts()
    {
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow(sessionId: "s1", windowIndex: 0, confusion: 0.5)
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, [], [], [], [], []);

        Assert.Equal(1, result.TotalWindows);
        Assert.Equal(0, result.TotalAdaptations);
        Assert.Equal(0, result.TotalPredictions);
        Assert.Single(result.Windows);
        Assert.Null(result.Windows[0].AdaptationDecision);
    }

    [Fact]
    public void Build_AdaptationMappedToMatchingWindow()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
            MakeTrainingRow("s1", 1, createdAt: baseTime.AddSeconds(30)),
        };
        // DecisionIndex is 0 for both (as observed in live data); mapping relies on CreatedAtUtc.
        var adaptations = new List<DashboardAdaptationResult>
        {
            new(baseTime.AddSeconds(30), 0, "[]", "[]", "test reasoning", null, null, null, null, null, null, null, null)
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        Assert.Null(result.Windows[0].AdaptationDecision);   // 30 s gap > 5 s tolerance
        Assert.NotNull(result.Windows[1].AdaptationDecision); // 0 s gap — exact match
        Assert.Equal("test reasoning", result.Windows[1].AdaptationDecision!.ReasoningSummary);
        Assert.Equal(1, result.TotalAdaptations);
    }

    // ── Timestamp-based adaptation mapping ────────────────────────────────────

    [Fact]
    public void Build_TimestampMapping_AllDecisionIndexZero_MapsCorrectlyByTimestamp()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
            MakeTrainingRow("s1", 1, createdAt: baseTime.AddSeconds(30)),
            MakeTrainingRow("s1", 2, createdAt: baseTime.AddSeconds(60)),
        };
        var adaptations = new List<DashboardAdaptationResult>
        {
            new(baseTime.AddSeconds(1),  0, "[]", "[]", "reason-w0", null, null, null, null, null, null, null, null),
            new(baseTime.AddSeconds(31), 0, "[]", "[]", "reason-w1", null, null, null, null, null, null, null, null),
            new(baseTime.AddSeconds(61), 0, "[]", "[]", "reason-w2", null, null, null, null, null, null, null, null),
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        Assert.Equal("reason-w0", result.Windows[0].AdaptationDecision!.ReasoningSummary);
        Assert.Equal("reason-w1", result.Windows[1].AdaptationDecision!.ReasoningSummary);
        Assert.Equal("reason-w2", result.Windows[2].AdaptationDecision!.ReasoningSummary);
        Assert.Equal(3, result.TotalAdaptations);
    }

    [Fact]
    public void Build_TimestampMapping_AdaptationOutsideTolerance_DoesNotMap()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
        };
        var adaptations = new List<DashboardAdaptationResult>
        {
            // 10 s away — exceeds the 5 s tolerance
            new(baseTime.AddSeconds(10), 0, "[]", "[]", "should-not-appear", null, null, null, null, null, null, null, null),
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        Assert.Null(result.Windows[0].AdaptationDecision);
        Assert.Equal(0, result.TotalAdaptations);
    }

    [Fact]
    public void Build_TimestampMapping_MultipleAdaptations_EachMapsToNearestWindow()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
            MakeTrainingRow("s1", 1, createdAt: baseTime.AddSeconds(30)),
        };
        var adaptations = new List<DashboardAdaptationResult>
        {
            new(baseTime.AddSeconds(2),  0, "[]", "[]", "reason-for-w0", null, null, null, null, null, null, null, null),
            new(baseTime.AddSeconds(28), 0, "[]", "[]", "reason-for-w1", null, null, null, null, null, null, null, null),
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        Assert.NotNull(result.Windows[0].AdaptationDecision);
        Assert.Equal("reason-for-w0", result.Windows[0].AdaptationDecision!.ReasoningSummary);
        Assert.NotNull(result.Windows[1].AdaptationDecision);
        Assert.Equal("reason-for-w1", result.Windows[1].AdaptationDecision!.ReasoningSummary);
        Assert.Equal(2, result.TotalAdaptations);
    }

    [Fact]
    public void Build_TemporalPredictionsGroupedByWindow()
    {
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0),
        };
        var preds = new List<DashboardRiskPredictionResult>
        {
            new(WindowIndex: 0, Horizon: 1, PredictedNearTermRisk: "low",  Confidence: 0.8, ModelVersion: "v1"),
            new(WindowIndex: 0, Horizon: 2, PredictedNearTermRisk: "high", Confidence: 0.6, ModelVersion: "v1"),
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, [], preds, [], [], []);

        Assert.Equal(2, result.TotalPredictions);
        Assert.Equal(2, result.Windows[0].TemporalRiskPredictions.Count);
        Assert.Equal(0.7, result.AveragePredictionConfidence!.Value, precision: 5);
    }

    [Fact]
    public void Build_CorrectnessFlagComputedForMatchingHorizons()
    {
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0),
        };
        var preds = new List<DashboardRiskPredictionResult>
        {
            new(WindowIndex: 0, Horizon: 1, "high", 0.9, "v1"),
        };
        var targets = new List<DashboardTargetResult>
        {
            new(SourceWindowIndex: 0, TargetWindowIndex: 1, Horizon: 1,
                TargetNearTermRisk: "high", TargetBehaviorState: "struggling",
                TargetConfusionScore: 0.8, TargetGoalUnderstanding: 0.3, TargetHintDependenceScore: 0.6)
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, [], preds, [], [], targets);

        Assert.True(result.Windows[0].H1PredictionCorrect);
        Assert.Null(result.Windows[0].H2PredictionCorrect);
        Assert.Null(result.Windows[0].H3PredictionCorrect);
        Assert.Equal(1.0, result.H1PredictionAccuracy!.Value, precision: 5);
        Assert.Null(result.H2PredictionAccuracy);
    }

    [Fact]
    public void Build_AccuracyIsZeroWhenAllPredictionsWrong()
    {
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0),
            MakeTrainingRow("s1", 1),
        };
        var preds = new List<DashboardRiskPredictionResult>
        {
            new(0, 1, "low",  0.8, "v1"),
            new(1, 1, "high", 0.7, "v1"),
        };
        var targets = new List<DashboardTargetResult>
        {
            new(0, 1, 1, "high",   "struggling", 0.8, 0.3, 0.6),
            new(1, 2, 1, "medium", "recovering",  0.5, 0.5, 0.4),
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, [], preds, [], [], targets);

        Assert.Equal(0.0, result.H1PredictionAccuracy!.Value, precision: 5);
    }

    // ── AdxDashboardQueryService — graceful null-ADX fallback ─────────────────

    [Fact]
    public async Task AdxDashboardQueryService_NullQueryProvider_RecentSessionsReturnsEmpty()
    {
        var service = BuildServiceWithoutAdx();
        var result = await service.GetRecentSessionsAsync();
        Assert.Empty(result.Rows);
        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
    }

    [Fact]
    public async Task AdxDashboardQueryService_NullQueryProvider_SessionTimelineReturnsNull()
    {
        var service = BuildServiceWithoutAdx();
        var result = await service.GetSessionTimelineAsync("test-session-id");
        Assert.Null(result.Value);
        Assert.Equal(DashboardQueryStatus.ServiceUnavailable, result.Status);
    }

    // ── Explanation field mapping ─────────────────────────────────────────────

    [Fact]
    public void Build_ExplanationFieldsMappedFromAdaptationResult()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
        };
        var adaptations = new List<DashboardAdaptationResult>
        {
            new(baseTime.AddSeconds(1), 0, "[]", "[]", "reasoning",
                null, null, null,
                "[\"worsening_pattern\"]", "State summary.", "Why changed.", "Coach message.", "High confidence.")
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        var ad = result.Windows[0].AdaptationDecision;
        Assert.NotNull(ad);
        Assert.Equal("[\"worsening_pattern\"]", ad!.HypothesisLabelsJson);
        Assert.Equal("State summary.", ad.LearnerStateSummary);
        Assert.Equal("Why changed.", ad.WhySupportChanged);
        Assert.Equal("Coach message.", ad.CoachMessage);
        Assert.Equal("High confidence.", ad.ConfidenceNote);
    }

    [Fact]
    public void Build_NullExplanationFields_DoNotCrashTimeline()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<DashboardTrainingRowResult>
        {
            MakeTrainingRow("s1", 0, createdAt: baseTime),
        };
        // Older row: all explanation fields null
        var adaptations = new List<DashboardAdaptationResult>
        {
            new(baseTime.AddSeconds(1), 0, "[]", "[]", "old reasoning",
                null, null, null, null, null, null, null, null)
        };
        var result = DashboardTimelineBuilder.Build("s1", rows, adaptations, [], [], [], []);

        var ad = result.Windows[0].AdaptationDecision;
        Assert.NotNull(ad);
        Assert.Equal("old reasoning", ad!.ReasoningSummary);
        Assert.Null(ad.HypothesisLabelsJson);
        Assert.Null(ad.LearnerStateSummary);
        Assert.Null(ad.CoachMessage);
    }

    // ── KQL text regression guards ────────────────────────────────────────────

    [Fact]
    public void AdaptationsKql_ContainsColumnIfExists()
    {
        var kql = AdxDashboardQueryService.BuildAdaptationsKql("test-session");
        Assert.Contains("column_ifexists(\"HypothesisLabelsJson\"", kql);
        Assert.Contains("column_ifexists(\"LearnerStateSummary\"",  kql);
        Assert.Contains("column_ifexists(\"WhySupportChanged\"",    kql);
        Assert.Contains("column_ifexists(\"CoachMessage\"",         kql);
        Assert.Contains("column_ifexists(\"ConfidenceNote\"",       kql);
    }

    [Fact]
    public void AdaptationsKql_ContainsSessionIdFilter()
    {
        var kql = AdxDashboardQueryService.BuildAdaptationsKql("my-session-abc");
        Assert.Contains("my-session-abc", kql);
        Assert.Contains("AdaptationDecisions_v2", kql);
    }

    [Fact]
    public void RecentSessionsKql_DoesNotContain0LLiteral()
    {
        // "0L" is not accepted as a long literal in this ADX environment.
        // The fix uses tolong(0) instead — this test guards against regression.
        var kql = AdxDashboardQueryService.BuildRecentSessionsKql(20);
        Assert.DoesNotContain("0L", kql);
    }

    [Fact]
    public void RecentSessionsKql_ContainsTolongCoalesce()
    {
        var kql = AdxDashboardQueryService.BuildRecentSessionsKql(20);
        Assert.Contains("coalesce(AdaptationCount, tolong(0))", kql);
        Assert.Contains("coalesce(PredictionCount, tolong(0))", kql);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdxDashboardQueryService BuildServiceWithoutAdx()
    {
        // No ICslQueryProvider registered — simulates unconfigured ADX environment.
        var services = new ServiceCollection().BuildServiceProvider();
        var config   = new ConfigurationBuilder().Build();
        var logger   = NullLogger<AdxDashboardQueryService>.Instance;
        return new AdxDashboardQueryService(services, config, logger);
    }

    private static DashboardTrainingRowResult MakeTrainingRow(
        string sessionId    = "s1",
        int windowIndex     = 0,
        double confusion    = 0.3,
        DateTime? createdAt = null)
        => new(
            SessionId:                       sessionId,
            UserId:                          "user-1",
            SimId:                           "sim-a",
            ScenarioId:                      "scene-1",
            WindowIndex:                     windowIndex,
            CreatedAtUtc:                    createdAt ?? DateTime.UtcNow,
            ConfusionScore:                  confusion,
            GoalUnderstanding:               0.7,
            HintDependenceScore:             0.2,
            GuardrailOverallBehaviorState:   "stable",
            GuardrailHintDependenceRiskLevel: "low",
            SequenceWindowCount:             windowIndex + 1,
            VolatilityScore:                 0.1,
            MomentumScore:                   0.5,
            StabilityScore:                  0.9,
            PredictedNearTermRisk:           "low");
}
