# Medical-Supply Event Interpretation

## What the adapter does

`MedicalSupplyEventInterpretationAdapter` translates the medical-supply-stocking sim's domain event vocabulary into Recursor behavioral dimensions.  It is selected automatically when `SimId == "medical-supply-stocking"`.  All other sims continue to use `DefaultSimEventInterpretationAdapter`.

The generic adapter counted `error`, `safety_violation`, `hint_request`, and `step_complete` event types, none of which the medical-supply sim emits.  This produced artificially perfect dimension scores (SafetyCompliance = 1.0, AttentionDetection = 1.0) for sessions with 16+ safety errors and zero successes.  The medical adapter fixes this by counting sim-specific event types.

---

## Event → dimension mapping

| Dimension | Formula | Medical events counted |
|---|---|---|
| **SafetyCompliance** | `correctRejections / (safetyErrors + correctRejections)` or 0.75 if no quality decisions | `damaged_supply_stocked`, `expired_supply_stocked`, `inspection_skipped` (errors); `damaged_supply_rejected_correctly`, `expired_supply_rejected_correctly` (successes) |
| **AttentionDetection** | `identificationSuccesses / (identificationErrors + identificationSuccesses)` or 0.75 | Errors = safety errors + `usable_supply_rejected`; Successes = correct rejections + `supply_stocked_correctly` |
| **GoalUnderstanding** | `goalSuccesses / (goalSuccesses + goalErrors)` or 0.5 | Successes = `supply_stocked_correctly` + correct rejections; Errors = `usable_supply_rejected` + `wrong_bin_error` |
| **ProcedureSequencing** | `procedureSuccesses / (procedureErrors + procedureSuccesses)` or 0.5 if < 2 events | Errors = `wrong_bin_error` + `inspection_skipped` + `usable_supply_rejected`; Successes = `supply_stocked_correctly` + correct rejections |
| **PaceRegulation** | `1.0 - avgLatencySeconds / 30.0`; falls back to DurationMs then 0.65 | `timeSincePreviousActionSeconds` from AdditionalMetrics |
| **SelfCorrection** | `correctedAfterFeedback / feedbackEvents`; falls back to `(1 - errorRate) * 0.8` | `corrected_after_feedback`, `repeated_error_after_feedback` |
| **FeedbackResponsiveness** | `hint_followed / (hint_followed + hint_ignored)`; 0.60 if only requests present | `hint_followed`, `hint_ignored`, `hint_requested` |
| **TaskContinuity** | `0.40 + progressRate * 0.40`; `0.65 + correctRate * 0.30` on scenario completion | `supply_stocked_correctly`, correct rejections, `scenario_completed` |

### Score interpretation

All dimensions are in [0.0, 1.0].  Higher = better for all dimensions.

| Range | Meaning |
|---|---|
| 0.0–0.2 | Severe impairment — almost all decisions wrong |
| 0.2–0.5 | Below threshold — `BehaviorInterpreter` generates an impairment hypothesis |
| 0.5–0.8 | Average / recovering |
| 0.8–1.0 | Strong performance |

---

## How the adapter is selected

```
FeatureExtractionService → ISimEventInterpretationAdapterFactory.GetAdapter(session.SimId)
    ↓ SimId == "medical-supply-stocking"
    MedicalSupplyEventInterpretationAdapter
    ↓ any other SimId
    DefaultSimEventInterpretationAdapter
```

The factory is a singleton registered in `Program.cs`.  Adding a new sim requires implementing `ISimEventInterpretationAdapter` and registering it before `DefaultSimEventInterpretationAdapter` in `SimEventInterpretationAdapterFactory`.

---

## Window-trigger behaviour

The medical adapter adds these force-trigger event types (equivalent to `safety_violation` in the generic adapter):

- `damaged_supply_stocked`
- `expired_supply_stocked`
- `inspection_skipped`
- `scenario_completed`

A window is created immediately when any of these events appear in a batch, regardless of the 50-event accumulation threshold.

---

## Expected downstream effects after the fix

For a session with high safety errors and zero successes:

| Signal | Before fix | After fix |
|---|---|---|
| SafetyCompliance | 1.0 | 0.0 |
| AttentionDetection | 1.0 | 0.0–0.2 |
| GoalUnderstanding | 0.5 | 0.0 |
| ConfusionScore | ~0.23 | >0.60 |
| Hypotheses | recovery_pattern | safety-risk, goal-confusion, attention-deficit, learner-overload |
| GuardrailOverallBehaviorState | recovering | conflicted |
| Adaptations | 0 | difficulty-reduction, scaffold-hints, pace-support |

`GuardrailOverallBehaviorState = "conflicted"` occurs because the rule engine detects confusion but the shadow ML model (untrained) returns 0.0 confusion probability — creating a guardrail disagreement.  Once ML models are trained on medical-supply data, the state will transition to `"struggling"` when both signals agree.

---

## KQL validation queries

Replace `<SESSION_ID>` with the actual session ID from the Sim Explorer dashboard.

### 1 — Dimension scores over time

```kusto
BehaviorStateTrainingRows
| where SessionId == "<SESSION_ID>"
| project
    CreatedAtUtc,
    WindowIndex,
    GuardrailOverallBehaviorState,
    GoalUnderstanding,
    ProcedureSequencing,
    AttentionDetection,
    SafetyCompliance,
    FeedbackResponsiveness,
    SelfCorrection,
    TaskContinuity,
    ConfusionScore,
    HesitationScore,
    ImpulsivityScore,
    HintDependenceScore,
    MeanConfusionScore,
    VolatilityScore,
    StabilityScore,
    PredictedNearTermRisk
| order by WindowIndex asc
```

**Expected after fix:** SafetyCompliance < 0.3, AttentionDetection < 0.3, ConfusionScore > 0.60, GuardrailOverallBehaviorState in ("conflicted", "struggling").

### 2 — Cross-signal mismatch check

```kusto
let rawSummary =
RawEvents
| where SessionId == "<SESSION_ID>"
| summarize
    RawSafetyErrors=countif(Category == "safety_error"),
    RawErrors=countif(Category == "error"),
    RawSuccesses=countif(Category == "success"),
    DamagedStocked=countif(EventType == "damaged_supply_stocked"),
    UsableRejected=countif(EventType == "usable_supply_rejected")
by SessionId;
let behaviorSummary =
BehaviorStateTrainingRows
| where SessionId == "<SESSION_ID>"
| summarize
    Windows=count(),
    AvgSafetyCompliance=avg(SafetyCompliance),
    AvgAttentionDetection=avg(AttentionDetection),
    AvgGoalUnderstanding=avg(GoalUnderstanding),
    AvgConfusion=avg(ConfusionScore),
    States=make_set(GuardrailOverallBehaviorState)
by SessionId;
rawSummary
| join kind=inner behaviorSummary on SessionId
```

**Expected after fix:**
- `DamagedStocked > 0` → `AvgSafetyCompliance` is low (< 0.3)
- High `RawErrors` / zero `RawSuccesses` → `States` does not include only "recovering"
- `RawSafetyErrors > 0` → `AvgAttentionDetection` is depressed

### 3 — Adaptations produced

```kusto
AdaptationDecisions_v2
| where SessionId == "<SESSION_ID>"
| project CreatedAtUtc, WindowIndex, InterventionFamilies, ReasoningSummary
| order by WindowIndex asc
```

**Expected after fix:** `InterventionFamilies` should include `difficulty-reduction`, `scaffold-hints`, and/or `pace-support`.

---

## Known limitations

1. **PredictedNearTermRisk** — With only 1–2 windows, the trajectory slope is near zero, so `PredictedNearTermRisk` remains `"low"`.  This is expected behaviour; trajectory risk requires at least 3 windows to compute meaningful slopes.

2. **GuardrailOverallBehaviorState = "conflicted" vs "struggling"** — Without trained ML models for the medical-supply domain, the shadow ML always returns ConfusionProbability = 0.0, creating a guaranteed disagreement with the rule engine's confusion signal.  The state will become `"struggling"` once a confusion model is trained on medical-supply sessions.

3. **ImpulsivityScore** — Sessions with no timing data default PaceRegulation to 0.65 (neutral).  High ImpulsivityScore in no-timing-data sessions reflects the `(1 - SelfCorrection)` and `(1 - GoalUnderstanding)` components, which are legitimate signals of reckless or disengaged behavior even without pace evidence.

4. **Generic "error" events** — If the medical-supply sim sends a generic `error` event alongside its domain-specific events, the medical adapter ignores it.  Only recognised medical event types are counted.
