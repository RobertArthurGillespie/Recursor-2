namespace NCATAIBlazorFrontendTest.Shared;

// ── Phase 10S-1: Recursor Sim Adapter Contract — shared constants and response DTOs ──
// These are naming constants only. They do not change runtime behavior.

public static class RecursorEventCategories
{
    public const string TaskAction  = "task_action";
    public const string Success     = "success";
    public const string Error       = "error";
    public const string SafetyError = "safety_error";
    public const string Hint        = "hint";
    public const string Feedback    = "feedback";
    public const string Navigation  = "navigation";
    public const string System      = "system";
}

public static class RecursorCommonMetricKeys
{
    public const string IsCorrect                  = "isCorrect";
    public const string ErrorSeverity              = "errorSeverity";
    public const string SafetyCritical             = "safetyCritical";
    public const string TimeSincePreviousActionSec = "timeSincePreviousActionSeconds";
    public const string AttemptNumberForItem       = "attemptNumberForItem";
    public const string HintsUsedThisItem          = "hintsUsedThisItem";
    public const string RepeatedErrorCount         = "repeatedErrorCount";
}

public static class RecursorCommonContextKeys
{
    public const string CurrentDifficulty     = "currentDifficulty";
    public const string CurrentHintMode       = "currentHintMode";
    public const string CurrentTimePressure   = "currentTimePressure";
    public const string CurrentErrorTolerance = "currentErrorTolerance";
    public const string ScenarioPhase         = "scenarioPhase";
}

public static class MedicalSupplyEventTypes
{
    public const string SupplySelected                 = "supply_selected";
    public const string SupplyInspected                = "supply_inspected";
    public const string BinSelected                    = "bin_selected";
    public const string SupplyStocked                  = "supply_stocked";
    public const string SupplyRejected                 = "supply_rejected";
    public const string SupplyStockedCorrectly         = "supply_stocked_correctly";
    public const string WrongBinError                  = "wrong_bin_error";
    public const string DamagedSupplyStocked           = "damaged_supply_stocked";
    public const string ExpiredSupplyStocked           = "expired_supply_stocked";
    public const string DamagedSupplyRejectedCorrectly = "damaged_supply_rejected_correctly";
    public const string ExpiredSupplyRejectedCorrectly = "expired_supply_rejected_correctly";
    public const string UsableSupplyRejected           = "usable_supply_rejected";
    public const string InspectionSkipped              = "inspection_skipped";
    public const string HintRequested                  = "hint_requested";
    public const string HintShown                      = "hint_shown";
    public const string HintFollowed                   = "hint_followed";
    public const string HintIgnored                    = "hint_ignored";
    public const string CorrectiveFeedbackShown        = "corrective_feedback_shown";
    public const string CorrectedAfterFeedback         = "corrected_after_feedback";
    public const string RepeatedErrorAfterFeedback     = "repeated_error_after_feedback";
    public const string ScenarioCompleted              = "scenario_completed";
}

public static class MedicalSupplyPayloadKeys
{
    public const string SupplyId              = "supplyId";
    public const string SupplyCategory        = "supplyCategory";
    public const string RequiredBin           = "requiredBin";
    public const string SelectedBin           = "selectedBin";
    public const string IsDamaged             = "isDamaged";
    public const string IsExpired             = "isExpired";
    public const string IsContaminated        = "isContaminated";
    public const string ShouldReject          = "shouldReject";
    public const string WasInspected          = "wasInspected";
    public const string VisualSimilarityGroup = "visualSimilarityGroup";
    public const string DifficultyTag         = "difficultyTag";
}

public static class RecursorAdaptationParameterNames
{
    public const string Difficulty        = "difficulty";
    public const string HintMode          = "hintMode";
    public const string TimePressure      = "timePressure";
    public const string ErrorTolerance    = "errorTolerance";
    public const string DistractorDensity = "distractorDensity";
}

// Response DTO for POST /api/recursor/validate-sim-batch.
// Shared so Unity clients can deserialize the response without extra JSON mapping.
public class SimBatchValidationResult
{
    public bool IsValidEnoughForShadowMode { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string DetectedSimId { get; set; } = "";
    public string DetectedScenarioId { get; set; } = "";
    public int EventCount { get; set; }
    public Dictionary<string, int> CategoryCounts { get; set; } = [];
    public Dictionary<string, int> EventTypeCounts { get; set; } = [];
}
