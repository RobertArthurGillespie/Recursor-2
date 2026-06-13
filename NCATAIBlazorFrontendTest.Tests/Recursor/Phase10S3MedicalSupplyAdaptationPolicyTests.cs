using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10S-3: Medical-supply adaptation policy tests.
///
/// Regression tests that verify:
///   1. SimCatalogRepository has a medical-supply-stocking entry (root-cause regression guard).
///   2. AdaptationPolicyService produces adaptations for struggling medical-supply sessions.
///   3. Produced adaptations include the expected intervention families and parameter changes.
///   4. The original test sim (sim-training-v1) behaviour is unaffected.
///   5. Mastery sessions produce mastery adaptations, not safety-support ones.
///   6. Phase 8A guardrail still preserves hintMode guided when hint-reduction is also present.
///
/// All tests are pure unit tests. No ADX, no DI container, no pipeline side effects.
/// </summary>
public class Phase10S3MedicalSupplyAdaptationPolicyTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static AdaptationPolicyService CreateService() =>
        new(Options.Create(new RecursorPoliciesOptions
        {
            EnableNextWindowHintDependenceGuardrail            = false,
            EnablePersonalizedHintFadeRule                     = false,
            EnablePersonalizedConfusionBlockDifficultyRule     = false,
            EnablePersonalizedDifficultyAccelerationRule       = false,
            EnablePhase8AGuardrailModifier                     = false,
            HintFadeRequiresSupportFadeEligibleWindows         = 2,
            HintRemoveRequiresStableMasteryWindows             = 2,
        }), NullLogger<AdaptationPolicyService>.Instance);

    private static SimCatalogDocument MedicalSupplyCatalog() => new()
    {
        SimId = "medical-supply-stocking",
        AdaptiveParameters =
        [
            new AdaptiveParameterDefinition { Name = "difficulty",     Type = "float", Min = 0.0, Max = 1.0 },
            new AdaptiveParameterDefinition { Name = "hintMode",       Type = "enum",  AllowedValues = ["off", "minimal", "guided"] },
            new AdaptiveParameterDefinition { Name = "timePressure",   Type = "float", Min = 0.0, Max = 1.0 },
            new AdaptiveParameterDefinition { Name = "errorTolerance", Type = "int",   Min = 0,   Max = 5   },
        ]
    };

    private static SimCatalogDocument OriginalSimCatalog() => new()
    {
        SimId = "sim-training-v1",
        AdaptiveParameters =
        [
            new AdaptiveParameterDefinition { Name = "difficulty",     Type = "float", Min = 0.0, Max = 1.0 },
            new AdaptiveParameterDefinition { Name = "hintMode",       Type = "enum",  AllowedValues = ["off", "minimal", "guided"] },
            new AdaptiveParameterDefinition { Name = "timePressure",   Type = "float", Min = 0.0, Max = 1.0 },
            new AdaptiveParameterDefinition { Name = "errorTolerance", Type = "int",   Min = 0,   Max = 5   },
        ]
    };

    private static SessionDocument StrugglingMedicalSession() => new()
    {
        SessionId                             = "s-med-test-001",
        UserId                                = "medical-demo-user-001",
        SimId                                 = "medical-supply-stocking",
        ScenarioId                            = "basic-supply-room",
        Status                                = "active",
        ConsecutiveSupportFadeEligibleWindows = 0,
        ConsecutiveStableMasteryWindows       = 0,
        ConsecutiveRelapseWindows             = 0,
        CurrentDifficultyProfile              = new Dictionary<string, string>()
    };

    private static SessionDocument MasteryMedicalSession() => new()
    {
        SessionId                             = "s-med-test-mastery",
        UserId                                = "medical-demo-user-001",
        SimId                                 = "medical-supply-stocking",
        ScenarioId                            = "basic-supply-room",
        Status                                = "active",
        ConsecutiveSupportFadeEligibleWindows = 3,
        ConsecutiveStableMasteryWindows       = 3,
        ConsecutiveRelapseWindows             = 0,
        CurrentDifficultyProfile              = new Dictionary<string, string> { ["hintMode"] = "guided" }
    };

    private static HypothesisSetDocument HypothesisSetWith(params string[] labels) => new()
    {
        Id         = "hs-test-001",
        SessionId  = "s-med-test-001",
        Hypotheses = labels.Select(l => new BehavioralHypothesis { Label = l, Confidence = 0.75 }).ToList()
    };

    // ── Root-cause regression guard ───────────────────────────────────────────

    [Fact]
    public void SimCatalogRepository_HasMedicalSupplyStockingEntry()
    {
        var repo    = new SimCatalogRepository();
        var catalog = repo.Get("medical-supply-stocking");

        Assert.NotNull(catalog);
    }

    [Fact]
    public void SimCatalogRepository_MedicalSupplyEntry_HasHintModeParameter()
    {
        var repo    = new SimCatalogRepository();
        var catalog = repo.Get("medical-supply-stocking")!;

        Assert.Contains(catalog.AdaptiveParameters, p => p.Name == "hintMode");
        var hintParam = catalog.AdaptiveParameters.First(p => p.Name == "hintMode");
        Assert.NotNull(hintParam.AllowedValues);
        Assert.Contains("guided", hintParam.AllowedValues);
    }

    [Fact]
    public void SimCatalogRepository_MedicalSupplyEntry_HasDifficultyAndTimePressure()
    {
        var repo    = new SimCatalogRepository();
        var catalog = repo.Get("medical-supply-stocking")!;

        Assert.Contains(catalog.AdaptiveParameters, p => p.Name == "difficulty");
        Assert.Contains(catalog.AdaptiveParameters, p => p.Name == "timePressure");
    }

    // ── Adaptation is produced ────────────────────────────────────────────────

    [Fact]
    public void StrugglingSession_WithSafetyRisk_ProducesAdaptation()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "attention-deficit", "goal-confusion");

        var result = sut.ApplyPolicy(session, catalog, hypSet);

        Assert.NotNull(result);
    }

    [Fact]
    public void StrugglingSession_WithConfusionPatternAndLearnerOverload_ProducesAdaptation()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet);

        Assert.NotNull(result);
    }

    [Fact]
    public void StrugglingSession_WithCompoundHypothesisSet_ProducesAdaptation()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        // Full realistic label set from a bad medical-supply session.
        var hypSet = HypothesisSetWith(
            "attention-deficit",
            "goal-confusion",
            "sequencing-difficulty",
            "safety-risk",
            "confusion_pattern",
            "hint_dependence_pattern",
            "compound_confusion_hint_dependence",
            "learner-overload",
            "impulsivity_pattern"
        );

        var result = sut.ApplyPolicy(session, catalog, hypSet);

        Assert.NotNull(result);
    }

    // ── Parameter changes produced ────────────────────────────────────────────

    [Fact]
    public void StrugglingSession_AdaptationIncludesHintModeGuided()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "hintMode" && (string?)c.Value == "guided");
    }

    [Fact]
    public void StrugglingSession_AdaptationIncludesScaffoldHintsFamily()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.Contains(result.InterventionFamilies, f => f == "scaffold-hints");
    }

    [Fact]
    public void StrugglingSession_AdaptationIncludesDifficultyReduction()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "difficulty" && c.Operation == "decrease");
    }

    [Fact]
    public void StrugglingSession_AdaptationIncludesPaceSupport()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "timePressure" && c.Operation == "decrease");
    }

    // ── Original test sim is unaffected ──────────────────────────────────────

    [Fact]
    public void OriginalSim_StrugglingBehavior_StillProducesAdaptation()
    {
        var sut     = CreateService();
        var catalog = OriginalSimCatalog();
        var session = new SessionDocument
        {
            SessionId                             = "s-orig-test-001",
            SimId                                 = "sim-training-v1",
            CurrentDifficultyProfile              = new Dictionary<string, string>(),
            ConsecutiveSupportFadeEligibleWindows = 0,
            ConsecutiveStableMasteryWindows       = 0,
            ConsecutiveRelapseWindows             = 0,
        };
        var hypSet = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet);

        Assert.NotNull(result);
    }

    [Fact]
    public void OriginalSim_StrugglingBehavior_AlsoProducesHintModeGuided()
    {
        var sut     = CreateService();
        var catalog = OriginalSimCatalog();
        var session = new SessionDocument
        {
            SessionId                             = "s-orig-test-001",
            SimId                                 = "sim-training-v1",
            CurrentDifficultyProfile              = new Dictionary<string, string>(),
            ConsecutiveSupportFadeEligibleWindows = 0,
            ConsecutiveStableMasteryWindows       = 0,
            ConsecutiveRelapseWindows             = 0,
        };
        var hypSet = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.Contains(result.ParameterChanges,
            c => c.Parameter == "hintMode" && (string?)c.Value == "guided");
    }

    // ── Mastery session does not produce safety-support adaptation ────────────

    [Fact]
    public void MasterySession_ProducesDifficultyIncreaseNotDecrease()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = MasteryMedicalSession();
        var hypSet  = HypothesisSetWith("stable_mastery_pattern");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.NotNull(result);
        Assert.DoesNotContain(result.ParameterChanges, c => c.Parameter == "difficulty" && c.Operation == "decrease");
        Assert.Contains(result.ParameterChanges, c => c.Parameter == "difficulty" && c.Operation == "increase");
    }

    [Fact]
    public void MasterySession_DoesNotIncludeScaffoldHintsFamily()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = MasteryMedicalSession();
        var hypSet  = HypothesisSetWith("stable_mastery_pattern");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.DoesNotContain(result.InterventionFamilies, f => f == "scaffold-hints");
    }

    // ── Phase 8A: hintMode guided is preserved when hint-reduction is also present ──

    [Fact]
    public void Phase8A_WhenHintDependenceRiskHigh_HintModeGuidedPreservedBecauseScaffoldHintsRestores()
    {
        // hint_dependence_pattern adds hint-reduction; safety-risk + confusion_pattern add scaffold-hints.
        // Phase 8A Rule D removes hint-reduction family when HintDependenceRiskLevel=high.
        // scaffold-hints is a restoration family so the hintMode guided ParameterChange must survive.
        var policyService = CreateService();
        var guardrailService = new Phase8AGuardrailModifierService(
            NullLogger<Phase8AGuardrailModifierService>.Instance);

        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith(
            "safety-risk", "confusion_pattern", "hint_dependence_pattern", "learner-overload");

        var adaptation = policyService.ApplyPolicy(session, catalog, hypSet)!;

        var guardrail = new MultiSignalGuardrailSummary
        {
            OverallBehaviorState    = "struggling",
            HintDependenceRiskLevel = "high",
            GuardrailConflictCount  = 0
        };

        var modified = guardrailService.Apply(adaptation, guardrail, shadowPrediction: null);

        // The adaptation must survive with hintMode guided intact.
        Assert.NotNull(modified);
        Assert.Contains(modified.ParameterChanges,
            c => c.Parameter == "hintMode" && (string?)c.Value == "guided");
    }

    // ── ReasoningSummary is populated ────────────────────────────────────────

    [Fact]
    public void StrugglingSession_ReasoningSummaryIsNotEmpty()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith("safety-risk", "confusion_pattern", "learner-overload");

        var result = sut.ApplyPolicy(session, catalog, hypSet)!;

        Assert.NotEmpty(result.ReasoningSummary);
    }

    // ── Empty hypothesis set returns null ────────────────────────────────────

    [Fact]
    public void EmptyHypothesisSet_ReturnsNull()
    {
        var sut     = CreateService();
        var catalog = MedicalSupplyCatalog();
        var session = StrugglingMedicalSession();
        var hypSet  = HypothesisSetWith();

        var result = sut.ApplyPolicy(session, catalog, hypSet);

        Assert.Null(result);
    }
}
