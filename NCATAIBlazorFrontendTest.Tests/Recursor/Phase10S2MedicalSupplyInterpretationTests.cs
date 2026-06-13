using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Server.Recursor.Services.SimEventInterpretation;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10S-2: Medical-supply event interpretation adapter tests.
///
/// Tests the MedicalSupplyEventInterpretationAdapter in isolation (pure unit tests)
/// and the full pipeline (FeatureExtraction → BehaviorScoring → BehaviorInterpreter)
/// to verify that medical-supply sessions produce correct behavioral dimensions and
/// hypothesis labels — in particular that sessions with safety errors and zero
/// successes no longer produce perfect scores or a "recovery_pattern" hypothesis.
///
/// Non-medical sims are verified to be unaffected (DefaultAdapter path).
/// </summary>
public class Phase10S2MedicalSupplyInterpretationTests
{
    // ── Adapter factory helpers ───────────────────────────────────────────────

    private static MedicalSupplyEventInterpretationAdapter MedicalAdapter() => new();

    private static SimEventInterpretationAdapterFactory MakeFactory() =>
        new(new MedicalSupplyEventInterpretationAdapter(),
            new DefaultSimEventInterpretationAdapter());

    private static FeatureExtractionService MakeFeatureService() =>
        new(MakeFactory());

    private static BehaviorScoringService MakeScoringService() => new();

    private static BehaviorInterpreter MakeInterpreter() =>
        new(MakeScoringService());

    // ── Session / batch builders ──────────────────────────────────────────────

    private static SessionDocument MedicalSession(string sessionId = "s-med-001") => new()
    {
        SessionId            = sessionId,
        UserId               = "user-001",
        SimId                = "medical-supply-stocking",
        ScenarioId           = "basic-supply-room",
        Status               = "active",
        StartedAtUtc         = DateTime.UtcNow,
        EventCount           = 0,
        EventsSinceLastWindow = 0,
        BatchCount           = 0,
    };

    private static RawEventRecord SafetyErrorEvent(string eventType, int seq = 0) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.SafetyError,
        Actor          = "user",
        Target         = "bin-sterile",
        Metrics        = new EventMetrics
        {
            AdditionalMetrics = new Dictionary<string, double>
            {
                [RecursorCommonMetricKeys.ErrorSeverity]  = 4.0,
                [RecursorCommonMetricKeys.SafetyCritical] = 1.0,
                [RecursorCommonMetricKeys.IsCorrect]      = 0.0,
            }
        },
    };

    private static RawEventRecord ErrorEvent(string eventType, int seq = 0) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.Error,
        Actor          = "user",
        Target         = "bin-wound-care",
        Metrics        = new EventMetrics
        {
            AdditionalMetrics = new Dictionary<string, double>
            {
                [RecursorCommonMetricKeys.ErrorSeverity] = 2.0,
                [RecursorCommonMetricKeys.IsCorrect]     = 0.0,
            }
        },
    };

    private static RawEventRecord SuccessEvent(string eventType, int seq = 0) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.Success,
        Actor          = "user",
        Target         = "bin-sterile",
        Metrics        = new EventMetrics
        {
            AdditionalMetrics = new Dictionary<string, double>
            {
                [RecursorCommonMetricKeys.IsCorrect] = 1.0,
            }
        },
    };

    private static RawEventRecord HintEvent(string eventType, int seq = 0) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.Hint,
        Actor          = "system",
        Target         = "user",
    };

    private static RawEventRecord FeedbackEvent(string eventType, int seq = 0) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.Feedback,
        Actor          = "system",
        Target         = "user",
    };

    private static RawEventRecord TaskActionEvent(string eventType, int seq = 0,
        double? timeSincePrevSec = null) => new()
    {
        EventId        = $"e-{seq}",
        SequenceNumber = seq,
        TimestampUtc   = DateTime.UtcNow,
        EventType      = eventType,
        Category       = RecursorEventCategories.TaskAction,
        Actor          = "user",
        Target         = "supply-001",
        Metrics        = new EventMetrics
        {
            AdditionalMetrics = timeSincePrevSec.HasValue
                ? new Dictionary<string, double>
                  { [RecursorCommonMetricKeys.TimeSincePreviousActionSec] = timeSincePrevSec.Value }
                : null,
        },
    };

    // ── A. Bad window: safety errors + errors, zero successes ─────────────────

    [Fact]
    public void BadWindow_SafetyCompliance_IsLow()
    {
        var events = Enumerable.Range(0, 8)
            .Select(i => SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.SafetyCompliance < 0.2,
            $"SafetyCompliance expected < 0.2 but was {features.SafetyCompliance:0.000}");
    }

    [Fact]
    public void BadWindow_AttentionDetection_IsLow()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
            events.Add(SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i));
        for (int i = 8; i < 18; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.UsableSupplyRejected, i));

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.AttentionDetection < 0.2,
            $"AttentionDetection expected < 0.2 but was {features.AttentionDetection:0.000}");
    }

    [Fact]
    public void BadWindow_GoalUnderstanding_IsLow()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 12; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.UsableSupplyRejected, i));
        for (int i = 12; i < 16; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.WrongBinError, i));

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.GoalUnderstanding < 0.1,
            $"GoalUnderstanding expected < 0.1 but was {features.GoalUnderstanding:0.000}");
    }

    [Fact]
    public void BadWindow_ConfusionScore_IsHigh()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
            events.Add(SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i));
        for (int i = 8; i < 20; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.UsableSupplyRejected, i));

        var session = MedicalSession();
        var featureWindow = new FeatureWindowDocument
        {
            SessionId = session.SessionId,
            SimId     = session.SimId,
            Features  = MedicalAdapter().ExtractFeatures(session, events)
        };
        var scores = MakeScoringService().Score(featureWindow);

        Assert.True(scores.ConfusionScore > 0.60,
            $"ConfusionScore expected > 0.60 but was {scores.ConfusionScore:0.000}");
    }

    [Fact]
    public void BadWindow_DoesNotProduceRecoveryPattern()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
            events.Add(SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i));
        for (int i = 8; i < 24; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.UsableSupplyRejected, i));

        var session = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var profile       = MakeInterpreter().BuildBehaviorProfile(featureWindow);
        var hypothesisSet = MakeInterpreter().BuildHypothesisSet(profile);

        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToList();
        Assert.DoesNotContain("recovery_pattern", labels);
    }

    [Fact]
    public void BadWindow_ProducesSafetyRiskHypothesis()
    {
        var events = Enumerable.Range(0, 10)
            .Select(i => SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i))
            .ToList();

        var session       = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var profile       = MakeInterpreter().BuildBehaviorProfile(featureWindow);
        var hypothesisSet = MakeInterpreter().BuildHypothesisSet(profile);

        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToList();
        Assert.Contains("safety-risk", labels);
    }

    [Fact]
    public void BadWindow_ProducesGoalConfusionOrLearnerOverload()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
            events.Add(SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i));
        for (int i = 8; i < 20; i++)
            events.Add(ErrorEvent(MedicalSupplyEventTypes.UsableSupplyRejected, i));

        var session       = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var profile       = MakeInterpreter().BuildBehaviorProfile(featureWindow);
        var hypothesisSet = MakeInterpreter().BuildHypothesisSet(profile);

        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToList();
        bool hasGoalOrOverload = labels.Contains("goal-confusion") || labels.Contains("learner-overload");
        Assert.True(hasGoalOrOverload,
            $"Expected goal-confusion or learner-overload, got: [{string.Join(", ", labels)}]");
    }

    [Fact]
    public void BadWindow_SafetyCompliance_IsZero_WhenAllDecisionsAreWrong()
    {
        // 16 damaged supplies stocked, 0 correct rejections
        var events = Enumerable.Range(0, 16)
            .Select(i => SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.Equal(0.0, features.SafetyCompliance, precision: 9);
    }

    [Fact]
    public void BadWindow_ExpiredSupplyStocked_AlsoPenalizesSafety()
    {
        var events = Enumerable.Range(0, 6)
            .Select(i => SafetyErrorEvent(MedicalSupplyEventTypes.ExpiredSupplyStocked, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.SafetyCompliance < 0.2,
            $"SafetyCompliance expected < 0.2 for expired supply errors but was {features.SafetyCompliance:0.000}");
    }

    // ── B. Good window: correct rejections + correct stockings ────────────────

    [Fact]
    public void GoodWindow_SafetyCompliance_IsHigh()
    {
        var events = Enumerable.Range(0, 8)
            .Select(i => SuccessEvent(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.SafetyCompliance > 0.85,
            $"SafetyCompliance expected > 0.85 but was {features.SafetyCompliance:0.000}");
    }

    [Fact]
    public void GoodWindow_AttentionDetection_IsHigh()
    {
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 6; i++)
            events.Add(SuccessEvent(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly, i));
        for (int i = 6; i < 12; i++)
            events.Add(SuccessEvent(MedicalSupplyEventTypes.SupplyStockedCorrectly, i));

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.AttentionDetection > 0.85,
            $"AttentionDetection expected > 0.85 but was {features.AttentionDetection:0.000}");
    }

    [Fact]
    public void GoodWindow_GoalUnderstanding_IsHigh()
    {
        var events = Enumerable.Range(0, 10)
            .Select(i => SuccessEvent(MedicalSupplyEventTypes.SupplyStockedCorrectly, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.GoalUnderstanding > 0.85,
            $"GoalUnderstanding expected > 0.85 but was {features.GoalUnderstanding:0.000}");
    }

    [Fact]
    public void GoodWindow_DoesNotProduceSafetyRisk()
    {
        var events = Enumerable.Range(0, 8)
            .Select(i => SuccessEvent(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly, i))
            .Concat(Enumerable.Range(8, 8)
                .Select(i => SuccessEvent(MedicalSupplyEventTypes.SupplyStockedCorrectly, i)))
            .ToList();

        var session       = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var profile       = MakeInterpreter().BuildBehaviorProfile(featureWindow);
        var hypothesisSet = MakeInterpreter().BuildHypothesisSet(profile);

        var labels = hypothesisSet.Hypotheses.Select(h => h.Label).ToList();
        Assert.DoesNotContain("safety-risk", labels);
    }

    // ── C. Latency-based pace tests ───────────────────────────────────────────

    [Fact]
    public void HighLatency_WithErrors_ProducesElevatedHesitationScore()
    {
        // Decision latency = 25 s each (very slow) paired with wrong decisions
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
        {
            var e = SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i);
            e.Metrics.AdditionalMetrics![RecursorCommonMetricKeys.TimeSincePreviousActionSec] = 25.0;
            events.Add(e);
        }

        var session = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var scores = MakeScoringService().Score(featureWindow);

        Assert.True(scores.HesitationScore > 0.45,
            $"HesitationScore expected > 0.45 for slow incorrect decisions but was {scores.HesitationScore:0.000}");
    }

    [Fact]
    public void LowLatency_WithErrors_ProducesElevatedImpulsivityScore()
    {
        // Decision latency = 0.5 s each (very fast) paired with wrong decisions
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
        {
            var e = SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked, i);
            e.Metrics.AdditionalMetrics![RecursorCommonMetricKeys.TimeSincePreviousActionSec] = 0.5;
            events.Add(e);
        }

        var session = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var scores = MakeScoringService().Score(featureWindow);

        Assert.True(scores.ImpulsivityScore > 0.45,
            $"ImpulsivityScore expected > 0.45 for fast incorrect decisions but was {scores.ImpulsivityScore:0.000}");
    }

    [Fact]
    public void HighLatency_WithCorrectDecisions_DoesNotProduceHighImpulsivity()
    {
        // Slow but correct — careful inspection, not impulsive
        var events = new List<RawEventRecord>();
        for (int i = 0; i < 8; i++)
        {
            var e = SuccessEvent(MedicalSupplyEventTypes.DamagedSupplyRejectedCorrectly, i);
            e.Metrics.AdditionalMetrics = new Dictionary<string, double>
            {
                [RecursorCommonMetricKeys.TimeSincePreviousActionSec] = 20.0,
                [RecursorCommonMetricKeys.IsCorrect]                  = 1.0,
            };
            events.Add(e);
        }

        var session = MedicalSession();
        var featureWindow = MakeFeatureWindowFor(session, events);
        var scores = MakeScoringService().Score(featureWindow);

        // With correct decisions, attention is high, so impulsivity should stay low
        // even when pace is slow (pace ≈ 0.33 for 20 s, but attention near 1.0)
        Assert.True(scores.ImpulsivityScore < 0.60,
            $"ImpulsivityScore should be low for slow+correct but was {scores.ImpulsivityScore:0.000}");
    }

    [Fact]
    public void PaceRegulation_HighLatency_IsLow()
    {
        // Average 25 s per action → PaceRegulation should be low (near 0.17)
        var events = Enumerable.Range(0, 5)
            .Select(i => TaskActionEvent(MedicalSupplyEventTypes.SupplySelected, i, timeSincePrevSec: 25.0))
            .ToList();

        double pace = MedicalSupplyEventInterpretationAdapter.PaceRegulation(events);

        Assert.True(pace < 0.20,
            $"PaceRegulation expected < 0.20 for 25 s avg latency but was {pace:0.000}");
    }

    [Fact]
    public void PaceRegulation_LowLatency_IsHigh()
    {
        // Average 2 s per action → PaceRegulation should be high (near 0.93)
        var events = Enumerable.Range(0, 5)
            .Select(i => TaskActionEvent(MedicalSupplyEventTypes.SupplySelected, i, timeSincePrevSec: 2.0))
            .ToList();

        double pace = MedicalSupplyEventInterpretationAdapter.PaceRegulation(events);

        Assert.True(pace > 0.90,
            $"PaceRegulation expected > 0.90 for 2 s avg latency but was {pace:0.000}");
    }

    // ── D. Hint responsiveness tests ──────────────────────────────────────────

    [Fact]
    public void AllHintsFollowed_ProducesHighFeedbackResponsiveness()
    {
        var events = Enumerable.Range(0, 5)
            .Select(i => HintEvent(MedicalSupplyEventTypes.HintFollowed, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.FeedbackResponsiveness > 0.90,
            $"FeedbackResponsiveness expected > 0.90 but was {features.FeedbackResponsiveness:0.000}");
    }

    [Fact]
    public void AllHintsIgnored_ProducesLowFeedbackResponsiveness()
    {
        var events = Enumerable.Range(0, 5)
            .Select(i => HintEvent(MedicalSupplyEventTypes.HintIgnored, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.FeedbackResponsiveness < 0.10,
            $"FeedbackResponsiveness expected < 0.10 but was {features.FeedbackResponsiveness:0.000}");
    }

    [Fact]
    public void CorrectedAfterFeedback_BoostsSelfCorrection()
    {
        var events = Enumerable.Range(0, 6)
            .Select(i => FeedbackEvent(MedicalSupplyEventTypes.CorrectedAfterFeedback, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.SelfCorrection > 0.90,
            $"SelfCorrection expected > 0.90 after all corrections but was {features.SelfCorrection:0.000}");
    }

    [Fact]
    public void RepeatedErrorAfterFeedback_LowersSelfCorrection()
    {
        var events = Enumerable.Range(0, 6)
            .Select(i => FeedbackEvent(MedicalSupplyEventTypes.RepeatedErrorAfterFeedback, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.SelfCorrection < 0.10,
            $"SelfCorrection expected < 0.10 with all repeated errors but was {features.SelfCorrection:0.000}");
    }

    // ── E. Window trigger tests ───────────────────────────────────────────────

    [Fact]
    public void MedicalAdapter_ShouldForceWindow_OnDamagedSupplyStocked()
    {
        var batch = new RawEventBatch
        {
            SimId  = "medical-supply-stocking",
            Events = [SafetyErrorEvent(MedicalSupplyEventTypes.DamagedSupplyStocked)]
        };

        Assert.True(MedicalAdapter().ShouldForceWindow(batch));
    }

    [Fact]
    public void MedicalAdapter_ShouldForceWindow_OnScenarioCompleted()
    {
        var batch = new RawEventBatch
        {
            SimId  = "medical-supply-stocking",
            Events =
            [
                new RawEventRecord
                {
                    EventType = MedicalSupplyEventTypes.ScenarioCompleted,
                    Category  = RecursorEventCategories.System,
                }
            ]
        };

        Assert.True(MedicalAdapter().ShouldForceWindow(batch));
    }

    [Fact]
    public void MedicalAdapter_ShouldNotForceWindow_OnNonTriggerEvents()
    {
        var batch = new RawEventBatch
        {
            SimId  = "medical-supply-stocking",
            Events = [TaskActionEvent(MedicalSupplyEventTypes.SupplySelected)]
        };

        Assert.False(MedicalAdapter().ShouldForceWindow(batch));
    }

    [Fact]
    public void DefaultAdapter_ShouldForceWindow_OnSafetyViolation()
    {
        var batch = new RawEventBatch
        {
            SimId  = "generic-sim",
            Events =
            [
                new RawEventRecord { EventType = "safety_violation", Category = "safety_error" }
            ]
        };

        Assert.True(new DefaultSimEventInterpretationAdapter().ShouldForceWindow(batch));
    }

    [Fact]
    public void MedicalAdapter_DoesNotForceWindow_OnGenericSafetyViolation()
    {
        // Medical adapter only looks for its own trigger events, not generic "safety_violation"
        var batch = new RawEventBatch
        {
            SimId  = "medical-supply-stocking",
            Events =
            [
                new RawEventRecord { EventType = "safety_violation", Category = "safety_error" }
            ]
        };

        Assert.False(MedicalAdapter().ShouldForceWindow(batch));
    }

    // ── F. AppliesTo routing ──────────────────────────────────────────────────

    [Fact]
    public void Factory_SelectsMedicalAdapter_ForMedicalSim()
    {
        var factory = MakeFactory();
        var adapter = factory.GetAdapter("medical-supply-stocking");
        Assert.IsType<MedicalSupplyEventInterpretationAdapter>(adapter);
    }

    [Fact]
    public void Factory_SelectsDefaultAdapter_ForUnknownSim()
    {
        var factory = MakeFactory();
        var adapter = factory.GetAdapter("some-other-sim");
        Assert.IsType<DefaultSimEventInterpretationAdapter>(adapter);
    }

    [Fact]
    public void MedicalAdapter_AppliesTo_IsCaseInsensitive()
    {
        var adapter = MedicalAdapter();
        Assert.True(adapter.AppliesTo("MEDICAL-SUPPLY-STOCKING"));
        Assert.True(adapter.AppliesTo("Medical-Supply-Stocking"));
        Assert.True(adapter.AppliesTo("medical-supply-stocking"));
    }

    // ── G. Non-medical sim is unaffected ──────────────────────────────────────

    [Fact]
    public void DefaultAdapter_ErrorEvent_LowersAttentionDetection()
    {
        // The generic "error" event type should still lower attention in the default adapter.
        var events = Enumerable.Range(0, 10)
            .Select(i => new RawEventRecord
            {
                EventType = "error",
                Category  = "error",
                SequenceNumber = i,
                TimestampUtc = DateTime.UtcNow,
                Metrics   = new EventMetrics()
            })
            .ToList();

        var session = new SessionDocument
        {
            SessionId = "s-generic",
            SimId     = "generic-sim",
            EventCount = 0,
        };

        var features = new DefaultSimEventInterpretationAdapter().ExtractFeatures(session, events);

        Assert.True(features.AttentionDetection < 0.5,
            $"DefaultAdapter should lower AttentionDetection for generic 'error' events but was {features.AttentionDetection:0.000}");
    }

    [Fact]
    public void DefaultAdapter_SafetyViolation_LowersSafetyCompliance()
    {
        var events = Enumerable.Range(0, 5)
            .Select(i => new RawEventRecord
            {
                EventType      = "safety_violation",
                Category       = "safety_error",
                SequenceNumber = i,
                TimestampUtc   = DateTime.UtcNow,
                Metrics        = new EventMetrics()
            })
            .ToList();

        // 5 safety violations + 5 padding events → safetyRate = 5/10 = 0.5
        // SafetyCompliance = Clamp(1.0 - 0.5 * 5) = 0.0
        var padding = Enumerable.Range(5, 5)
            .Select(i => new RawEventRecord
            {
                EventType = "action", Category = "task_action",
                SequenceNumber = i, TimestampUtc = DateTime.UtcNow,
                Metrics = new EventMetrics()
            })
            .ToList();

        events.AddRange(padding);

        var session = new SessionDocument
        {
            SessionId = "s-generic",
            SimId     = "generic-sim",
            EventCount = 0,
        };

        var features = new DefaultSimEventInterpretationAdapter().ExtractFeatures(session, events);

        Assert.True(features.SafetyCompliance < 0.5,
            $"DefaultAdapter should penalize SafetyCompliance for safety_violation events but was {features.SafetyCompliance:0.000}");
    }

    [Fact]
    public void MedicalAdapter_IgnoresGenericErrorEvent()
    {
        // A generic "error" event sent from the medical sim should NOT affect dimensions,
        // because the medical adapter counts specific event types only.
        var events = Enumerable.Range(0, 10)
            .Select(i => new RawEventRecord
            {
                EventType = "error", // not a recognised medical event type
                Category  = RecursorEventCategories.Error,
                SequenceNumber = i,
                TimestampUtc = DateTime.UtcNow,
                Metrics   = new EventMetrics()
            })
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        // No recognised medical events — all dimension formulas default to neutral
        Assert.True(features.SafetyCompliance >= 0.70,
            $"SafetyCompliance should be neutral (no quality decisions), was {features.SafetyCompliance:0.000}");
        Assert.True(features.GoalUnderstanding >= 0.45,
            $"GoalUnderstanding should be near neutral, was {features.GoalUnderstanding:0.000}");
    }

    // ── H. Safety compliance neutral when no quality decisions ────────────────

    [Fact]
    public void NoSupplyQualityEvents_SafetyCompliance_IsNeutral()
    {
        // Only task action events — no damaged/expired supply decisions
        var events = Enumerable.Range(0, 8)
            .Select(i => TaskActionEvent(MedicalSupplyEventTypes.SupplySelected, i))
            .ToList();

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        // Neutral default: 0.75
        Assert.Equal(0.75, features.SafetyCompliance, precision: 9);
    }

    // ── I. Scenario completed boosts task continuity ──────────────────────────

    [Fact]
    public void ScenarioCompleted_TaskContinuity_IsHigh()
    {
        var events = new List<RawEventRecord>
        {
            new()
            {
                EventType  = MedicalSupplyEventTypes.ScenarioCompleted,
                Category   = RecursorEventCategories.System,
                TimestampUtc = DateTime.UtcNow,
            }
        };

        var features = MedicalAdapter().ExtractFeatures(MedicalSession(), events);

        Assert.True(features.TaskContinuity >= 0.65,
            $"TaskContinuity expected >= 0.65 on scenario completed but was {features.TaskContinuity:0.000}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FeatureWindowDocument MakeFeatureWindowFor(
        SessionDocument session, List<RawEventRecord> events) => new()
    {
        Id          = "fw-test",
        SessionId   = session.SessionId,
        UserId      = session.UserId,
        SimId       = session.SimId,
        ScenarioId  = session.ScenarioId,
        WindowIndex = 0,
        Features    = MedicalAdapter().ExtractFeatures(session, events)
    };
}
