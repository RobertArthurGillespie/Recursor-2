# Medical-Supply Stocking Sim — Recursor Adapter Spec

## Overview

The medical-supply stocking sim trains users to correctly stock medical supplies into bins.
Users handle supplies one at a time: inspect them, identify the correct bin, and either
stock or reject each item. Errors include placing supplies in the wrong bin, stocking
damaged or expired supplies, rejecting usable supplies, and skipping inspection steps.

Recursor learns which learners are struggling with visual discrimination vs procedural
memory vs safety compliance, and adapts difficulty, hint level, time pressure, and
error tolerance accordingly.

---

## Sim identity

| Field     | Value |
|-----------|-------|
| SimId     | `medical-supply-stocking` |
| SimVersion| Semantic version of the Unity build. E.g. `1.0.0` |

---

## Initial scenario IDs

| ScenarioId               | Description |
|--------------------------|-------------|
| basic-supply-room        | Introductory stocking with common, visually distinct supplies |
| damaged-supply-detection | Focus on identifying damaged vs usable supplies |
| high-pressure-restock    | Time-constrained stocking under urgency |
| mixed-bin-challenge      | Multiple bins with visually similar supplies |

---

## Event taxonomy

### task_action events (user performs a domain action)

| EventType      | Meaning | Target | Key Metrics |
|----------------|---------|--------|-------------|
| supply_selected | User picks up a supply item | supplyId | attemptNumberForItem, timeSincePreviousActionSeconds |
| supply_inspected | User examines a supply item | supplyId | timeSincePreviousActionSeconds |
| bin_selected   | User selects a destination bin | binId | timeSincePreviousActionSeconds |
| supply_stocked | User places a supply into a bin | binId | attemptNumberForItem |
| supply_rejected | User deliberately rejects a supply | supplyId | hintsUsedThisItem |

### success events (correct outcome)

| EventType | Meaning | Metrics |
|-----------|---------|---------|
| supply_stocked_correctly | Supply placed in correct bin | isCorrect=1.0 |
| damaged_supply_rejected_correctly | Damaged supply correctly rejected | isCorrect=1.0 |
| expired_supply_rejected_correctly | Expired supply correctly rejected | isCorrect=1.0 |

### error events (non-safety mistake)

| EventType | Meaning | Metrics |
|-----------|---------|---------|
| wrong_bin_error | Correct supply placed in wrong bin | isCorrect=0.0, errorSeverity=2, repeatedErrorCount |
| usable_supply_rejected | Good supply incorrectly rejected | isCorrect=0.0, errorSeverity=1 |

### safety_error events (safety-critical mistake)

| EventType | Meaning | Metrics |
|-----------|---------|---------|
| damaged_supply_stocked | Damaged supply placed in bin | isCorrect=0.0, errorSeverity=4, safetyCritical=1.0 |
| expired_supply_stocked | Expired supply placed in bin | isCorrect=0.0, errorSeverity=5, safetyCritical=1.0 |
| inspection_skipped | User stocked without inspecting | isCorrect=0.0, errorSeverity=3, safetyCritical=1.0 |

### hint events

| EventType | Meaning |
|-----------|---------|
| hint_requested | User explicitly requested a hint |
| hint_shown | Sim showed a hint (auto or requested) |
| hint_followed | User acted on the hint correctly |
| hint_ignored | User received a hint and did not follow it |

### feedback events

| EventType | Meaning | Metrics |
|-----------|---------|---------|
| corrective_feedback_shown | Sim displayed corrective feedback after an error | — |
| corrected_after_feedback | User corrected their action after feedback | repeatedErrorCount |
| repeated_error_after_feedback | User repeated the same error after receiving feedback | repeatedErrorCount, errorSeverity |

### system events

| EventType | Meaning |
|-----------|---------|
| scenario_completed | User completed the scenario |

---

## Payload fields

Payload is attached to task_action, success, error, and safety_error events.
Include only fields that are relevant to the specific event.

| Key | Type | Applies to | Description |
|-----|------|-----------|-------------|
| supplyId | string | supply_selected, supply_inspected, supply_stocked, supply_rejected | Unique ID of the supply item |
| supplyCategory | string | any supply event | E.g. `wound-care`, `medication`, `sterile-equipment` |
| requiredBin | string | supply_stocked, wrong_bin_error | The bin the supply should have gone to |
| selectedBin | string | supply_stocked, wrong_bin_error | The bin the user actually chose |
| isDamaged | bool→double | any supply event | 1.0 if the supply is damaged |
| isExpired | bool→double | any supply event | 1.0 if the supply is expired |
| isContaminated | bool→double | any supply event | 1.0 if the supply is contaminated |
| shouldReject | bool→double | supply_stocked, supply_rejected | 1.0 if the correct action is rejection |
| wasInspected | bool→double | supply_stocked, supply_rejected | 1.0 if the user inspected before acting |
| visualSimilarityGroup | string | supply_selected, wrong_bin_error | Groups visually similar supplies that cause confusion |
| difficultyTag | string | any supply event | Difficulty metadata tag for this item. E.g. `easy`, `medium`, `confusable` |

---

## Context keys (on every event)

All context keys from the base contract apply. For this sim:

| Key | Notes |
|-----|-------|
| currentDifficulty | 0.0–1.0. Higher = more visually similar supplies, more confusable items |
| currentHintMode | `off` / `minimal` / `guided` |
| currentTimePressure | 0.0–1.0. Higher = shorter decision windows per supply |
| currentErrorTolerance | Int. Max errors before automatic intervention |
| scenarioPhase | E.g. `tutorial`, `practice`, `challenge`, `assessment` |

---

## Feature mapping to Recursor dimensions

| Recursor Dimension | Medical-Supply Signals |
|--------------------|------------------------|
| attentionDetection | `timeSincePreviousActionSeconds` distribution; very long pauses (>30s) signal distraction; very short (<1s) signal rushing |
| goalUnderstanding | `isCorrect` on supply_stocked events; `wrong_bin_error` rate; pattern of `requiredBin` vs `selectedBin` mismatches |
| procedureSequencing | Whether `supply_inspected` precedes `supply_stocked`; `inspection_skipped` rate |
| paceRegulation | Task completion time; time-pressure context vs error rate correlation |
| selfCorrection | `corrected_after_feedback` vs `repeated_error_after_feedback` ratio |
| feedbackResponsiveness | `hint_followed` vs `hint_ignored` ratio; `corrected_after_feedback` count |
| safetyCompliance | `safety_error` event rate; `damaged_supply_stocked` and `expired_supply_stocked` counts; `safetyCritical` metric |
| taskContinuity | `scenario_completed` presence; mid-scenario session abandonment detection |

---

## Adaptation parameter contract

Register these parameters in `SimCatalogRepository` under `medical-supply-stocking`:

| Parameter | Type | Range | Meaning |
|-----------|------|-------|---------|
| difficulty | float | 0.0–1.0 | Controls visual similarity of supplies; higher = more confusable items |
| hintMode | enum | `off` / `minimal` / `guided` | Hint level during supply handling |
| timePressure | float | 0.0–1.0 | Time allowed per supply decision |
| errorTolerance | int | 0–5 | Max non-safety errors before hint escalation |

Sim behavior when parameter changes arrive:
- `difficulty` increase: introduce more visually similar supplies in the next supply batch
- `difficulty` decrease: use more visually distinct supplies; reduce confusable distractors
- `hintMode` increase: show bin labels, supply inspection prompts, or bin highlights
- `timePressure` decrease: extend per-supply decision windows
- `errorTolerance` increase: delay automatic corrective feedback

Do not change parameters mid-supply. Apply at the next supply selection checkpoint.

---

## Shadow validation plan

Before any live adaptation, validate these in ADX after running test sessions:

1. `BehaviorStateTrainingRows` rows exist for `SimId = "medical-supply-stocking"`.
2. `safetyCompliance` dimension scores are non-zero for sessions containing `safety_error` events.
3. `selfCorrection` scores are non-zero for sessions containing `corrected_after_feedback` events.
4. `procedureSequencing` scores reflect the `inspection_skipped` rate.
5. `TemporalRiskPredictions` exist and H1 accuracy is measurable.
6. Dashboard `/recursor-dashboard` shows sessions with realistic behavior-state distributions.
7. `POST /api/recursor/validate-sim-batch` returns `isValidEnoughForShadowMode: true` for 10+ sample batches.

---

## Conservative active adaptation plan

Only promote to active adaptation after ALL shadow validation gates pass AND:

1. At least 30 complete sessions with no ingestion errors.
2. Policy reliability report shows `difficulty-reduction` or `hint-escalation` as `promising`.
3. Explicit sign-off that `damaged_supply_stocked` and `expired_supply_stocked` cannot be triggered
   by parameter changes from Recursor.
4. Smoke test: verify the sim ignores unknown parameter names gracefully.
5. Verify that `errorTolerance = 0` does not cause the sim to become unresponsive or freeze.

---

## Example minimal valid event batch

```json
{
  "schemaVersion": "1.0",
  "batchId": "batch-0001",
  "sessionId": "<returned by start session>",
  "userId": "user-123",
  "simId": "medical-supply-stocking",
  "simVersion": "1.0.0",
  "scenarioId": "basic-supply-room",
  "clientTimestampUtc": "2026-06-02T14:00:00Z",
  "batchSequence": 1,
  "events": [
    {
      "eventId": "evt-0001",
      "sequenceNumber": 1,
      "timestampUtc": "2026-06-02T14:00:01Z",
      "eventType": "supply_selected",
      "category": "task_action",
      "actor": "user",
      "target": "supply-bandage-001",
      "context": {
        "currentDifficulty": 0.3,
        "currentHintMode": "minimal",
        "currentTimePressure": 0.0,
        "currentErrorTolerance": 3,
        "scenarioPhase": "practice"
      },
      "metrics": {
        "additionalMetrics": {
          "attemptNumberForItem": 1,
          "timeSincePreviousActionSeconds": 4.2
        }
      },
      "payload": {
        "supplyId": "bandage-001",
        "supplyCategory": "wound-care",
        "isDamaged": 0,
        "isExpired": 0,
        "wasInspected": 0
      }
    },
    {
      "eventId": "evt-0002",
      "sequenceNumber": 2,
      "timestampUtc": "2026-06-02T14:00:03Z",
      "eventType": "supply_inspected",
      "category": "task_action",
      "actor": "user",
      "target": "supply-bandage-001",
      "context": {
        "currentDifficulty": 0.3,
        "currentHintMode": "minimal",
        "scenarioPhase": "practice"
      },
      "metrics": {
        "additionalMetrics": {
          "timeSincePreviousActionSeconds": 1.8
        }
      },
      "payload": {
        "supplyId": "bandage-001",
        "wasInspected": 1
      }
    },
    {
      "eventId": "evt-0003",
      "sequenceNumber": 3,
      "timestampUtc": "2026-06-02T14:00:06Z",
      "eventType": "supply_stocked_correctly",
      "category": "success",
      "actor": "user",
      "target": "bin-wound-care",
      "context": {
        "currentDifficulty": 0.3,
        "currentHintMode": "minimal",
        "scenarioPhase": "practice"
      },
      "metrics": {
        "additionalMetrics": {
          "isCorrect": 1.0,
          "hintsUsedThisItem": 0,
          "attemptNumberForItem": 1
        }
      },
      "payload": {
        "supplyId": "bandage-001",
        "requiredBin": "bin-wound-care",
        "selectedBin": "bin-wound-care",
        "wasInspected": 1
      }
    }
  ]
}
```
