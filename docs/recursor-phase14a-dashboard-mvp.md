# Phase 14A — Internal Recursor Evaluation Dashboard MVP

## Purpose

Phase 14A adds a lightweight internal evaluation and demo dashboard that makes the Recursor
pipeline explainable end-to-end. The dashboard is not a polished production UI. It is an
internal tool for developers and evaluators to inspect what Recursor inferred, predicted,
adapted, suppressed, protected, and later observed.

The core narrative the dashboard surfaces per session window:

> Behavior → State → Adaptation decision → Policy notes → Temporal prediction → Actual outcome

No adaptation behavior, policy logic, ML models, or ADX schemas were changed as part of this phase.

---

## Endpoints

### GET /api/recursor/dashboard/recent-sessions

Returns up to 20 recent sessions summarized from ADX, ordered by most recently active.

**Query:** Joins `BehaviorStateTrainingRows`, `AdaptationDecisions_v2`, and
`TemporalRiskPredictions` in KQL to compute per-session counts.

**Response shape:**

```json
[
  {
    "sessionId": "abc-123",
    "userId": "user-a",
    "simId": "sim-fire",
    "scenarioId": "scene-1",
    "firstSeenUtc": "2026-05-29T10:00:00Z",
    "lastSeenUtc": "2026-05-29T10:15:00Z",
    "windowCount": 12,
    "adaptationCount": 3,
    "predictionCount": 36
  }
]
```

---

### GET /api/recursor/dashboard/session/{sessionId}

Returns a full per-window timeline for one session.

**Query:** Runs four sequential ADX queries (training rows, adaptations, risk predictions,
prediction targets), then assembles the timeline server-side via `DashboardTimelineBuilder`.

**Response shape (abbreviated):**

```json
{
  "sessionId": "abc-123",
  "totalWindows": 12,
  "totalAdaptations": 3,
  "totalPredictions": 36,
  "averagePredictionConfidence": 0.72,
  "h1PredictionAccuracy": 0.75,
  "h2PredictionAccuracy": 0.60,
  "h3PredictionAccuracy": 0.50,
  "windows": [
    {
      "windowIndex": 0,
      "createdAtUtc": "2026-05-29T10:00:00Z",
      "userId": "user-a",
      "confusionScore": 0.30,
      "goalUnderstanding": 0.75,
      "hintDependenceScore": 0.15,
      "guardrailOverallBehaviorState": "stable",
      "guardrailHintDependenceRiskLevel": "low",
      "sequenceWindowCount": 1,
      "volatilityScore": 0.0,
      "momentumScore": 0.0,
      "stabilityScore": 1.0,
      "predictedNearTermRisk": "low",
      "adaptationDecision": null,
      "temporalRiskPredictions": [
        { "horizon": 1, "predictedNearTermRisk": "low", "confidence": 0.82, "modelVersion": "trm-v1" }
      ],
      "temporalPredictionTargets": [
        { "horizon": 1, "targetWindowIndex": 1, "targetNearTermRisk": "low", "targetBehaviorState": "stable", ... }
      ],
      "h1PredictionCorrect": true,
      "h2PredictionCorrect": null,
      "h3PredictionCorrect": null
    }
  ]
}
```

**Correctness flags:**
- `h1PredictionCorrect = true` — model predicted "low" at H1 and the observed state at window+1 was also "low"
- `h1PredictionCorrect = false` — mismatch
- `h1PredictionCorrect = null` — target window not yet processed; not enough data to evaluate

---

## ADX Tables Used

| Table | Use |
|---|---|
| `BehaviorStateTrainingRows` | Per-window behavior scores, guardrail state, sequence features |
| `AdaptationDecisions_v2` | Adaptation decisions with Phase 8A/8E/10A notes |
| `TemporalRiskPredictions` | Shadow temporal risk model outputs (H1/H2/H3) |
| `TemporalPredictionTargets` | Observed future states for training and correctness evaluation |

`AdaptationEffectiveness` is not currently queried by the dashboard but is available in ADX
if confusion delta analysis is added in a future phase.

---

## Frontend

Route: `/recursor-dashboard`

The Blazor WASM page at `Client/Pages/RecursorDashboard.razor` provides:

1. **Sessions table** — lists recent sessions with window, adaptation, and prediction counts;
   click "View" to open a session timeline.

2. **Summary cards** — total windows, adaptations, predictions, average model confidence, and
   H1/H2/H3 prediction accuracy for the selected session.

3. **Timeline table** — one row per window with:
   - Behavior state badge (struggling / recovering / stable / mastering / conflicted)
   - Confusion, goal understanding, hint dependence scores
   - Near-term risk badge (low / medium / high)
   - Sequence window count, volatility, momentum
   - Adaptation families and reasoning summary (when fired)
   - Phase 8A guardrail notes, Phase 8E reliability notes, Phase 10A trajectory notes
   - H1/H2/H3 columns showing predicted risk → observed target risk with ✓/✗ correctness icon

Row color coding: `struggling` = red, `recovering` = yellow, `mastering` = green.

---

## How to Use for Demos

1. Run a Recursor session via `POST /api/recursor/sessions/start` and submit event batches.
2. Navigate to `/recursor-dashboard` in the browser.
3. Select the session from the recent sessions table.
4. Walk through the timeline:
   - Point to `GuardrailOverallBehaviorState` to explain what state the learner was in.
   - Point to `AdaptationDecision` cells to show when and why Recursor intervened.
   - Point to Phase 8A/8E/10A notes to explain guardrail vetoes and reliability suppression.
   - Point to H1/H2/H3 columns to show temporal risk model predictions vs observed outcomes.
5. Check summary accuracy cards to evaluate model calibration across the session.

---

## Code Locations

| File | Purpose |
|---|---|
| `Shared/RecursorDashboardContracts.cs` | Shared DTOs (Server + Client) |
| `Server/Recursor/Adx/AdxDashboardQueryService.cs` | ADX queries + timeline assembly |
| `Server/Controllers/RecursorDashboardController.cs` | Two GET endpoints |
| `Client/Pages/RecursorDashboard.razor` | Blazor UI |
| `Tests/Recursor/Phase14ADashboardTests.cs` | Unit tests |

---

## Limitations

- The dashboard reads from ADX only. Sessions that exist only in memory (not yet flushed to ADX) will not appear.
- Adaptation decisions are matched to windows by `DecisionIndex == WindowIndex`. Windows with no adaptation fired will show no adaptation row.
- Temporal prediction targets are only available for source windows where a later window has already been processed. The correctness flag for H1 at the last window will always be `null`.
- The timeline is limited to 1000 windows per session (`| take 1000` in KQL) to prevent large result sets.
- The dashboard is not authenticated. It is for internal/dev use only.
- No charts are included in the MVP. A future phase can add sparklines or trend charts per the full production roadmap.
