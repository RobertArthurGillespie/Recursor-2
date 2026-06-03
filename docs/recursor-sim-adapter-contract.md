# Recursor Sim Adapter Contract

## Purpose

This document defines what any sim must provide to integrate with Recursor, and what Recursor
guarantees to return. All sims speak to the same backend API. Recursor does not know anything
about the sim domain — the sim must map its domain to this contract.

---

## 1. Session identity fields

Every session must carry these fields on both the start request and every subsequent batch.
Missing identity fields cause session lookup to fail silently.

| Field       | Type   | Description |
|-------------|--------|-------------|
| SimId       | string | Stable sim identifier. Must match a registered entry in the sim catalog. E.g. `sim-training-v1`, `medical-supply-stocking`. |
| SimVersion  | string | Sim build version. Used for artifact tracing. E.g. `1.0`, `2.3.1`. |
| ScenarioId  | string | Identifies the current scenario within the sim. E.g. `basic-supply-room`. |
| UserId      | string | Identifies the learner. Passed through to all ADX records. |
| SessionId   | string | Assigned by Recursor at session start. Must be passed back on every batch. |

---

## 2. Raw event envelope

Every event in a batch must conform to `RawEventRecord`. The batch wraps events with common
identity fields, a batch sequence number, and a client-side timestamp.

### Batch-level fields

```
SchemaVersion      string   Always "1.0" for the current schema.
BatchId            string   Unique identifier for this batch (GUID recommended).
SessionId          string   Returned by POST /api/recursor/sessions/start.
UserId             string   Same as on the start request.
SimId              string   Same as on the start request.
SimVersion         string   Same as on the start request.
ScenarioId         string   Current scenario within the session.
ClientTimestampUtc datetime UTC timestamp from the client when the batch was assembled.
BatchSequence      int      Monotonically increasing batch counter within the session.
Events             array    One or more RawEventRecord objects.
```

### Event-level fields

```
EventId            string   Unique event identifier (GUID recommended).
SequenceNumber     long     Monotonically increasing within the batch.
TimestampUtc       datetime UTC timestamp when the event occurred on the client.
EventType          string   Specific event name. See taxonomy per sim adapter doc.
Category           string   Broad event category. See Section 3.
Actor              string   Who performed the action. E.g. "user", "system".
Target             string   What was acted upon. E.g. bin ID, supply ID, step name.
Context            object   Key-value map of sim state at the time of the event. See Section 5.
Metrics            object   Numeric measurements. See Section 4.
Payload            object   Domain-specific supplementary data. See sim adapter doc.
```

---

## 3. Required event categories

Recursor uses `Category` to classify the behavioral significance of an event.
Every event must have a category. Unknown categories are accepted but produce no feature signal.

| Category     | Meaning |
|--------------|---------|
| task_action  | User performed a domain-specific action (selection, placement, navigation). |
| success      | User completed an item, step, or task correctly. |
| error        | User made a non-safety mistake (wrong choice, wrong sequence, wrong timing). |
| safety_error | User violated a safety rule (contamination, dangerous handling, critical omission). |
| hint         | A hint was requested or shown. |
| feedback     | Corrective feedback was provided by the sim. |
| navigation   | User navigated between screens, menus, or zones. |
| system       | System-generated event (timer, state change, scenario transition). |

---

## 4. Required metrics

Metrics go in the `Metrics.AdditionalMetrics` dictionary (keyed by string, valued by double).
These are `recommended` unless otherwise noted.

| Key                            | Required for                    | Notes |
|--------------------------------|---------------------------------|-------|
| isCorrect                      | success, error events           | 1.0 = correct, 0.0 = incorrect |
| errorSeverity                  | error, safety_error events      | Scale 1–5. 5 = most severe. |
| safetyCritical                 | safety_error events             | 1.0 = safety-critical violation |
| timeSincePreviousActionSeconds | all task_action events          | Floating-point elapsed time |
| attemptNumberForItem           | all task_action events          | 1-indexed attempt count per item |
| hintsUsedThisItem              | hint, task_action events        | Count of hints used for current item |
| repeatedErrorCount             | error, safety_error events      | Times the same error has been made |

---

## 5. Required context keys

Context goes in `RawEventRecord.Context` (string → object? dictionary).
Context should be attached to every event that has meaningful sim-state at that moment.

| Key                  | Type   | Notes |
|----------------------|--------|-------|
| currentDifficulty    | double | Current difficulty level. Mirrors the active adaptation parameter. |
| currentHintMode      | string | Current hint mode. E.g. `off`, `minimal`, `guided`. |
| currentTimePressure  | double | Current time pressure. |
| currentErrorTolerance| int    | Remaining allowed errors before intervention. |
| scenarioPhase        | string | Named phase within the scenario. E.g. `orientation`, `practice`, `assessment`. |

---

## 6. Payload conventions

Payload is a free-form dictionary for domain-specific supplementary data.
Payload keys do not feed into Recursor's core feature extraction directly.
They are stored in ADX raw events for offline analysis only.

Conventions:
- Use camelCase key names.
- Boolean values should be stored as double 1.0 / 0.0 in metrics, not in payload.
- Payload values should be strings, numbers, or booleans only.
- Do not put large blobs or nested objects in payload.

---

## 7. Feature mapping rules

Recursor derives behavioral dimensions from events. Each sim must produce events that
allow the following feature signals to be computed across a feature window.

| Dimension              | Primary signal sources |
|------------------------|------------------------|
| attentionDetection     | timeSincePreviousActionSeconds, navigation patterns |
| goalUnderstanding      | isCorrect on task_action, error rates, wrong-target frequency |
| procedureSequencing    | sequence of EventTypes against expected procedure order |
| paceRegulation         | timeSincePreviousActionSeconds distribution, task completion time |
| selfCorrection         | correctedAfterFeedback events, error→success transitions |
| feedbackResponsiveness | hint_followed vs hint_ignored ratios |
| safetyCompliance       | safety_error count and severity |
| taskContinuity         | session abandonment signals, task_complete completion rate |

Sims that cannot produce signal for a dimension should document which dimensions are
unsupported and configure `SupportedDimensions` in the sim catalog accordingly.

---

## 8. Adaptation parameter contract

Recursor returns `ParameterChanges` in the batch response. Each change has:

```
Parameter  string   Name of the parameter to change.
Operation  string   "set" or "delta"
Value      object   New value or delta value.
```

Sims must:
- Read `ParameterChanges` from the batch response.
- Apply each parameter change immediately before the next scenario element.
- Clamp to the declared Min/Max or AllowedValues in the sim catalog.
- Never apply changes with unknown `Parameter` names.
- Log which changes were applied and which were ignored.

Active-mode sims receive real parameter changes. Shadow-mode sims receive no changes
(empty `ParameterChanges` array) but Recursor logs what would have been returned.

---

## 9. Shadow-mode requirements

A sim is ready for shadow integration when:

- [ ] All events include EventType and Category.
- [ ] Safety errors include `errorSeverity` and `safetyCritical`.
- [ ] At least one context key (`currentDifficulty` or `currentHintMode`) is populated per event.
- [ ] `POST /api/recursor/validate-sim-batch` returns `isValidEnoughForShadowMode: true` for sample batches.
- [ ] Sessions complete without session-not-found errors.
- [ ] ADX receives populated `BehaviorStateTrainingRows` records.
- [ ] ADX receives populated `TemporalEmbeddings` records.
- [ ] Dashboard shows sessions and predictions under `/recursor-dashboard`.

In shadow mode:
- The sim does NOT apply adaptation parameter changes.
- Recursor logs and persists everything as normal.
- Predictions, guardrails, and policy decisions run but have no effect on the sim.

---

## 10. Conservative active-mode requirements

A sim should not enter active adaptation until ALL of the following gates pass:

- [ ] At least 30 complete sessions in shadow mode with no ingestion errors.
- [ ] Dashboard shows reasonable H1 prediction accuracy (> 50% for the configured model).
- [ ] Policy reliability report shows at least one `promising` family with sufficient data.
- [ ] No `safety_error` events from parameter changes applied by Recursor.
- [ ] Explicit sign-off that the declared `AdaptiveParameters` min/max are safe for learners.
- [ ] Sim has been smoke-tested with all declared parameter combinations.

In active mode:
- Recursor returns real parameter changes in `ParameterChanges`.
- Phase 8A guardrail modifier may suppress changes based on current guardrail state.
- Phase 8E reliability weighting may suppress or protect families based on observed data.
- Phase 9D conditional suppression applies if enabled.

---

## 11. Dashboard requirements

For a sim to appear correctly in the Recursor evaluation dashboard:

- Sessions must emit events within a window (default: 5+ events per window trigger).
- `BehaviorStateTrainingRows` must include a valid `GuardrailOverallBehaviorState`.
- `TemporalRiskPredictions` must be populated (requires training the temporal risk model).
- The sim must use stable `SessionId` values (session IDs must match across ADX tables).

---

## 12. Minimum acceptance criteria for a new sim

A sim passes minimum acceptance when:

1. `POST /api/recursor/validate-sim-batch` → `isValidEnoughForShadowMode: true` with 0 critical warnings.
2. `POST /api/recursor/sessions/start` returns a valid `SessionId`.
3. `POST /api/recursor/events/batch` returns `Success: true` for at least one batch.
4. ADX `BehaviorStateTrainingRows` has at least one row for the sim after a full session.
5. The sim is registered in `SimCatalogRepository` with correct `SupportedDimensions` and `AdaptiveParameters`.

---

## 13. Validation endpoint

```
POST /api/recursor/validate-sim-batch
Body: RawEventBatch (JSON)
```

Returns `SimBatchValidationResult`:
```
isValidEnoughForShadowMode  bool
warnings                    string[]
detectedSimId               string
detectedScenarioId          string
eventCount                  int
categoryCounts              { category: count }
eventTypeCounts             { eventType: count }
```

This endpoint is for Unity development and CI validation only. It does not ingest data.
