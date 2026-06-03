using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using NCATAIBlazorFrontendTest.Shared;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Phase 10S-1 tests: Recursor sim adapter contract and validator.
/// Covers valid batches, missing identity fields, safety-error metric checks,
/// category/event-type counts, graceful empty-metrics handling, and adaptation isolation.
///
/// All tests are pure unit tests. No ADX, no DI container, no pipeline side effects.
/// </summary>
public class Phase10S1SimAdapterTests
{
    private static RecursorSimContractValidator MakeValidator() => new();

    // ── Valid batch ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidMedicalSupplyBatch_ProducesNoWarnings()
    {
        var batch = MakeValidMedicalSupplyBatch();
        var result = MakeValidator().Validate(batch);

        Assert.True(result.IsValidEnoughForShadowMode);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ValidBatch_DetectedSimId_MatchesBatchSimId()
    {
        var batch = MakeValidMedicalSupplyBatch();
        var result = MakeValidator().Validate(batch);

        Assert.Equal("medical-supply-stocking", result.DetectedSimId);
    }

    [Fact]
    public void ValidBatch_DetectedScenarioId_MatchesBatchScenarioId()
    {
        var batch = MakeValidMedicalSupplyBatch();
        var result = MakeValidator().Validate(batch);

        Assert.Equal("basic-supply-room", result.DetectedScenarioId);
    }

    [Fact]
    public void ValidBatch_EventCount_IsCorrect()
    {
        var batch = MakeValidMedicalSupplyBatch();
        var result = MakeValidator().Validate(batch);

        Assert.Equal(batch.Events.Count, result.EventCount);
    }

    // ── Missing identity fields ───────────────────────────────────────────────

    [Fact]
    public void MissingSimId_ProducesWarning_AndInvalidForShadow()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.SimId = "";
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings, w => w.Contains("SimId"));
        Assert.False(result.IsValidEnoughForShadowMode);
    }

    [Fact]
    public void MissingSessionId_ProducesWarning_AndInvalidForShadow()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.SessionId = "";
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings, w => w.Contains("SessionId"));
        Assert.False(result.IsValidEnoughForShadowMode);
    }

    [Fact]
    public void MissingUserId_ProducesWarning_AndInvalidForShadow()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.UserId = "";
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings, w => w.Contains("UserId"));
        Assert.False(result.IsValidEnoughForShadowMode);
    }

    [Fact]
    public void MissingSimVersion_ProducesWarning_ButDoesNotBlockShadow()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.SimVersion = "";
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings, w => w.Contains("SimVersion"));
        // SimVersion is not required for isValidEnoughForShadowMode
        Assert.True(result.IsValidEnoughForShadowMode);
    }

    [Fact]
    public void MissingScenarioId_ProducesWarning_ButDoesNotBlockShadow()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.ScenarioId = "";
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings, w => w.Contains("ScenarioId"));
        Assert.True(result.IsValidEnoughForShadowMode);
    }

    // ── safety_error metric checks ────────────────────────────────────────────

    [Fact]
    public void SafetyErrorEvent_WithoutErrorSeverity_ProducesWarning()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-safety-1",
            EventType = MedicalSupplyEventTypes.DamagedSupplyStocked,
            Category  = RecursorEventCategories.SafetyError,
            Target    = "bin-sterile",
            Metrics   = new EventMetrics(), // no AdditionalMetrics
        });
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings,
            w => w.Contains(RecursorCommonMetricKeys.ErrorSeverity));
    }

    [Fact]
    public void SafetyErrorEvent_WithoutSafetyCritical_ProducesWarning()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-safety-2",
            EventType = MedicalSupplyEventTypes.ExpiredSupplyStocked,
            Category  = RecursorEventCategories.SafetyError,
            Target    = "bin-medication",
            Metrics   = new EventMetrics
            {
                AdditionalMetrics = new Dictionary<string, double>
                {
                    [RecursorCommonMetricKeys.ErrorSeverity] = 5.0
                    // safetyCritical deliberately missing
                }
            },
        });
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings,
            w => w.Contains(RecursorCommonMetricKeys.SafetyCritical));
    }

    [Fact]
    public void SafetyErrorEvent_WithBothRequiredMetrics_ProducesNoSafetyWarning()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-safety-3",
            EventType = MedicalSupplyEventTypes.InspectionSkipped,
            Category  = RecursorEventCategories.SafetyError,
            Target    = "supply-vaccine-001",
            Metrics   = new EventMetrics
            {
                AdditionalMetrics = new Dictionary<string, double>
                {
                    [RecursorCommonMetricKeys.ErrorSeverity]  = 3.0,
                    [RecursorCommonMetricKeys.SafetyCritical] = 1.0,
                    [RecursorCommonMetricKeys.IsCorrect]      = 0.0
                }
            },
        });
        var result = MakeValidator().Validate(batch);

        Assert.DoesNotContain(result.Warnings,
            w => w.Contains(RecursorCommonMetricKeys.ErrorSeverity) && w.Contains("e-safety-3"));
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains(RecursorCommonMetricKeys.SafetyCritical) && w.Contains("e-safety-3"));
    }

    // ── error event metric check ──────────────────────────────────────────────

    [Fact]
    public void ErrorEvent_WithoutErrorSeverity_ProducesWarning()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-error-1",
            EventType = MedicalSupplyEventTypes.WrongBinError,
            Category  = RecursorEventCategories.Error,
            Target    = "bin-wound-care",
            Metrics   = new EventMetrics
            {
                AdditionalMetrics = new Dictionary<string, double>
                {
                    [RecursorCommonMetricKeys.IsCorrect] = 0.0
                }
            },
        });
        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings,
            w => w.Contains("e-error-1") && w.Contains(RecursorCommonMetricKeys.ErrorSeverity));
    }

    // ── Category counts ───────────────────────────────────────────────────────

    [Fact]
    public void CategoryCounts_AreAccurate()
    {
        var batch = MakeValidMedicalSupplyBatch(); // 1 task_action
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-success",
            EventType = MedicalSupplyEventTypes.SupplyStockedCorrectly,
            Category  = RecursorEventCategories.Success,
            Target    = "bin-wound-care",
        });
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-success-2",
            EventType = MedicalSupplyEventTypes.SupplyStockedCorrectly,
            Category  = RecursorEventCategories.Success,
            Target    = "bin-wound-care",
        });
        var result = MakeValidator().Validate(batch);

        Assert.Equal(1, result.CategoryCounts[RecursorEventCategories.TaskAction]);
        Assert.Equal(2, result.CategoryCounts[RecursorEventCategories.Success]);
        Assert.False(result.CategoryCounts.ContainsKey(RecursorEventCategories.SafetyError));
    }

    // ── EventType counts ──────────────────────────────────────────────────────

    [Fact]
    public void EventTypeCounts_AreAccurate()
    {
        var batch = MakeValidMedicalSupplyBatch(); // 1 supply_selected
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-dup",
            EventType = MedicalSupplyEventTypes.SupplySelected,
            Category  = RecursorEventCategories.TaskAction,
            Target    = "supply-syringe-001",
        });
        var result = MakeValidator().Validate(batch);

        Assert.Equal(2, result.EventTypeCounts[MedicalSupplyEventTypes.SupplySelected]);
    }

    [Fact]
    public void EventTypeCounts_DoNotIncludeMissingEventTypes()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-no-type",
            EventType = "",           // missing
            Category  = RecursorEventCategories.System,
            Target    = "system",
        });
        var result = MakeValidator().Validate(batch);

        // The blank EventType must not appear as a key in the counts dictionary
        Assert.False(result.EventTypeCounts.ContainsKey(""));
    }

    // ── Graceful handling of empty metrics and context ────────────────────────

    [Fact]
    public void ValidatorDoesNotThrow_OnEmptyMetricsAndContext()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-bare",
            EventType = MedicalSupplyEventTypes.BinSelected,
            Category  = RecursorEventCategories.TaskAction,
            Target    = "bin-A",
            // Metrics is default (new EventMetrics()) — AdditionalMetrics is null
            // Context is default (new Dictionary) — empty
        });

        var ex = Record.Exception(() => MakeValidator().Validate(batch));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidatorDoesNotThrow_OnNullAdditionalMetrics()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Add(new RawEventRecord
        {
            EventId   = "e-null-metrics",
            EventType = MedicalSupplyEventTypes.SupplySelected,
            Category  = RecursorEventCategories.TaskAction,
            Target    = "supply-1",
            Metrics   = new EventMetrics { AdditionalMetrics = null },
        });

        var ex = Record.Exception(() => MakeValidator().Validate(batch));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidatorDoesNotThrow_OnEmptyEventList()
    {
        var batch = MakeValidMedicalSupplyBatch();
        batch.Events.Clear();

        var ex = Record.Exception(() => MakeValidator().Validate(batch));
        Assert.Null(ex);
    }

    // ── Context key warnings ──────────────────────────────────────────────────

    [Fact]
    public void BatchWithContext_MissingCurrentDifficulty_ProducesWarning()
    {
        var batch = MakeValidMedicalSupplyBatch();
        // Remove currentDifficulty from all events
        foreach (var evt in batch.Events)
            evt.Context.Remove(RecursorCommonContextKeys.CurrentDifficulty);

        var result = MakeValidator().Validate(batch);

        Assert.Contains(result.Warnings,
            w => w.Contains(RecursorCommonContextKeys.CurrentDifficulty));
    }

    [Fact]
    public void BatchWithNoContext_ProducesNoContextWarnings()
    {
        var batch = MakeValidMedicalSupplyBatch();
        foreach (var evt in batch.Events)
            evt.Context.Clear();

        var result = MakeValidator().Validate(batch);

        Assert.DoesNotContain(result.Warnings,
            w => w.Contains(RecursorCommonContextKeys.CurrentDifficulty));
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains(RecursorCommonContextKeys.CurrentHintMode));
    }

    // ── Adaptation isolation ──────────────────────────────────────────────────

    [Fact]
    public void Validator_IsStateless_IdenticalCallsProduceIdenticalResults()
    {
        var batch = MakeValidMedicalSupplyBatch();
        var validator = MakeValidator();

        var r1 = validator.Validate(batch);
        var r2 = validator.Validate(batch);

        Assert.Equal(r1.IsValidEnoughForShadowMode, r2.IsValidEnoughForShadowMode);
        Assert.Equal(r1.EventCount,                 r2.EventCount);
        Assert.Equal(r1.Warnings.Count,             r2.Warnings.Count);
    }

    [Fact]
    public void ValidatorInterface_ExposesOnlyValidate()
    {
        var methods = typeof(IRecursorSimContractValidator)
            .GetMethods()
            .Select(m => m.Name)
            .ToList();

        Assert.Contains("Validate", methods);
        Assert.DoesNotContain("Adapt",    methods);
        Assert.DoesNotContain("Suppress", methods);
        Assert.DoesNotContain("Ingest",   methods);
        Assert.DoesNotContain("Predict",  methods);
    }

    [Fact]
    public void SimBatchValidationResult_HasNoAdaptationFields()
    {
        var propNames = typeof(SimBatchValidationResult)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("ParameterChanges",   propNames);
        Assert.DoesNotContain("AdaptationDecision", propNames);
        Assert.DoesNotContain("InterventionFamily", propNames);
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void RecursorEventCategories_AllExpectedValuesPresent()
    {
        Assert.Equal("task_action",  RecursorEventCategories.TaskAction);
        Assert.Equal("success",      RecursorEventCategories.Success);
        Assert.Equal("error",        RecursorEventCategories.Error);
        Assert.Equal("safety_error", RecursorEventCategories.SafetyError);
        Assert.Equal("hint",         RecursorEventCategories.Hint);
        Assert.Equal("feedback",     RecursorEventCategories.Feedback);
        Assert.Equal("navigation",   RecursorEventCategories.Navigation);
        Assert.Equal("system",       RecursorEventCategories.System);
    }

    [Fact]
    public void MedicalSupplyEventTypes_AllExpectedValuesPresent()
    {
        Assert.Equal("supply_selected",                   MedicalSupplyEventTypes.SupplySelected);
        Assert.Equal("damaged_supply_stocked",            MedicalSupplyEventTypes.DamagedSupplyStocked);
        Assert.Equal("inspection_skipped",                MedicalSupplyEventTypes.InspectionSkipped);
        Assert.Equal("repeated_error_after_feedback",     MedicalSupplyEventTypes.RepeatedErrorAfterFeedback);
        Assert.Equal("scenario_completed",                MedicalSupplyEventTypes.ScenarioCompleted);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RawEventBatch MakeValidMedicalSupplyBatch() => new()
    {
        SchemaVersion      = "1.0",
        BatchId            = "batch-test-001",
        SessionId          = "session-test-001",
        UserId             = "user-test-001",
        SimId              = "medical-supply-stocking",
        SimVersion         = "1.0.0",
        ScenarioId         = "basic-supply-room",
        ClientTimestampUtc = DateTime.UtcNow,
        Events             =
        [
            new RawEventRecord
            {
                EventId       = "e-001",
                EventType     = MedicalSupplyEventTypes.SupplySelected,
                Category      = RecursorEventCategories.TaskAction,
                Actor         = "user",
                Target        = "supply-bandage-001",
                Metrics       = new EventMetrics
                {
                    AdditionalMetrics = new Dictionary<string, double>
                    {
                        [RecursorCommonMetricKeys.AttemptNumberForItem]      = 1.0,
                        [RecursorCommonMetricKeys.TimeSincePreviousActionSec] = 4.2,
                    }
                },
                Context       = new Dictionary<string, object?>
                {
                    [RecursorCommonContextKeys.CurrentDifficulty]     = 0.3,
                    [RecursorCommonContextKeys.CurrentHintMode]       = "minimal",
                    [RecursorCommonContextKeys.CurrentTimePressure]   = 0.0,
                    [RecursorCommonContextKeys.CurrentErrorTolerance] = 3,
                    [RecursorCommonContextKeys.ScenarioPhase]         = "practice",
                },
                Payload       = new Dictionary<string, object?>
                {
                    [MedicalSupplyPayloadKeys.SupplyId]       = "bandage-001",
                    [MedicalSupplyPayloadKeys.SupplyCategory] = "wound-care",
                    [MedicalSupplyPayloadKeys.WasInspected]   = 0.0,
                },
            }
        ]
    };
}
