# Phase 10A — Sequence-Aware Feature Extraction + Basic Trajectory Intelligence

## Purpose

Phase 10A adds a **sliding-window, sequence-aware feature extraction layer** on top of the
existing per-window behavioral scores. It computes ordinary-least-squares (OLS) regression slopes,
volatility (population std dev), momentum (split-window comparison), and a compact state-transition
string from the last N trajectory snapshots (N ≤ 5). A rule-based classifier converts those features
into an interpretable `TrajectorySummary` with predicted near-term risk.

Phase 10A is **additive and non-breaking**:
- Existing adaptation decisions and pipeline behavior are unchanged.
- All new data is observability / audit only except `Phase10TrajectoryNotes` stamped on the
  adaptation decision row (string column, no new ADX tables).
- The sliding window cap of 5 snapshots is unchanged from the existing `RecentSnapshots` cap.

---

## What Phase 10A adds

| Artifact | Where |
|----------|-------|
| `SequenceFeatureSummary` model | `TrajectoryModels.cs` |
| `TrajectorySummary` model | `TrajectoryModels.cs` |
| `OverallBehaviorState` on `TrajectorySnapshot` | `TrajectoryModels.cs` |
| `ISequenceFeatureExtractor` + `SequenceFeatureExtractor` | `SequenceFeatureExtractor.cs` |
| `Phase10TrajectoryNotes` on `AdaptationDecisionDocument` | `AdaptationModels.cs` |
| `Phase10TrajectoryNotes` on `AdaptationDecisionRow` | `AdxRowModels.cs` |
| 9 new columns on `BehaviorStateTrainingRow` | `AdxRowModels.cs` |
| `SequenceSummary` + `TrajectorySummary` on `BatchApiResponse` | `RecursorApiModels.cs` |
| `SessionTimelineResponse` + `WindowTimelineEntry` | `RecursorApiModels.cs` |
| `SequenceSummary` + `TrajectorySummary` on `IngestionResult` | `RecursorIngestionService.cs` |
| `SequenceSummary` + `TrajectorySummary` on `ProcessBatchResult` | `RecursorSessionService.cs` |
| Guardrail state stamped on `RecentSnapshots[^1]` after each window | `RecursorIngestionService.cs` |
| `GET /api/recursor/session-timeline/{sessionId}` endpoint | `RecursorController.cs` |
| DI registration | `Program.cs` |
| 32 tests | `Phase10ASequenceFeatureTests.cs` |

---

## Models

### `SequenceFeatureSummary`

Computed from the last N windows (≤ 5). Contains:

| Field | Description |
|-------|-------------|
| `WindowCount` | Number of snapshots used |
| `MeanConfusionScore` | Average confusion across the window |
| `ConfusionTrendSlope` | OLS slope of confusion over window indices |
| `GoalTrendSlope` | OLS slope of goal understanding |
| `HintTrendSlope` | OLS slope of hint dependence |
| `ErrorRateTrendSlope` | OLS slope of derived error rate `≈ (1 - SelfCorrection) / 2` |
| `VolatilityScore` | Population std dev of confusion scores |
| `MomentumScore` | `(earlier_confusion − recent_confusion) + (recent_goal − earlier_goal)`; positive = improving |
| `RecentStateTransitions` | Dedup-consecutive state string, e.g. `"struggling→recovering→stable"` |

### `TrajectorySummary`

Classification derived from `SequenceFeatureSummary` using interpretable thresholds:

| Field | Condition |
|-------|-----------|
| `IsImproving` | ConfusionSlope < −0.05 AND GoalSlope > 0.03 |
| `IsDeteriorating` | ConfusionSlope > 0.05 AND GoalSlope < −0.03 |
| `IsVolatile` | VolatilityScore > 0.12 |
| `StabilityScore` | `clamp(0, 1 − volatility × 4, 1)` |
| `PredictedNearTermRisk` | "high" / "medium" / "low" (see risk rules below) |
| `InsufficientData` | Always `false` — insufficient data handled by `Extract()` returning null |

**Risk rules (cascade — first match wins):**

- `high`: ConfusionSlope > 0.08 OR MomentumScore < −0.12
- `medium`: ConfusionSlope > 0.03 OR MomentumScore < −0.05
- `low`: otherwise

### `TrajectorySnapshot.OverallBehaviorState`

Empty string by default. Stamped with `guardrailSummary.OverallBehaviorState` by
`RecursorIngestionService` immediately after the multi-signal guardrail runs. This allows
`BuildStateTransitions` to use the guardrail-derived state rather than the fallback rule-based
derivation when the guardrail fires.

---

## `ISequenceFeatureExtractor`

```csharp
public interface ISequenceFeatureExtractor
{
    // Returns null when fewer than 2 snapshots (insufficient history).
    SequenceFeatureSummary? Extract(IReadOnlyList<TrajectorySnapshot> snapshots);

    // Classifies a non-null SequenceFeatureSummary into a TrajectorySummary.
    TrajectorySummary Classify(SequenceFeatureSummary summary);
}
```

Internal static helpers (unit-testable directly):
- `LinearSlope(double[] values)` — OLS slope for index-0..N-1
- `StdDev(double[] values)` — population standard deviation
- `ComputeMomentum(IReadOnlyList<TrajectorySnapshot> snapshots)` — split-window average comparison
- `BuildStateTransitions(IReadOnlyList<TrajectorySnapshot> snapshots)` — dedup state string
- `DeriveState(TrajectorySnapshot s)` — rule-based fallback state

---

## ADX impact

### `AdaptationDecisions_v2` — one new column

```kql
.alter-merge table AdaptationDecisions_v2 (Phase10TrajectoryNotes: string)
```

CSV ingest mapping — append to existing mapping:
```json
{ "column": "Phase10TrajectoryNotes", "path": "$.Phase10TrajectoryNotes", "datatype": "string" }
```

Example value:
```
trajectory: improving; volatility=low; predicted-risk=low; windows=4; confusion-slope=-0.082; stability=0.87
```

### `BehaviorStateTrainingRows` — 9 new columns

```kql
.alter-merge table BehaviorStateTrainingRows (
    SequenceWindowCount: int,
    MeanConfusionScore: real,
    ConfusionTrendSlope: real,
    GoalTrendSlope: real,
    HintTrendSlope: real,
    VolatilityScore: real,
    MomentumScore: real,
    StabilityScore: real,
    PredictedNearTermRisk: string
)
```

All numeric columns default to 0 and the string column defaults to `""` when fewer than 2 snapshots
are available (the mapper passes nulls which are coerced to zero/empty).

---

## API exposure

### `POST /api/recursor/events/batch` response — non-breaking additions

New optional fields on `BatchApiResponse`:

```json
{
  "success": true,
  "adaptationProduced": false,
  "parameterChanges": [],
  "hypothesisLabels": ["confusion_pattern"],
  "sequenceSummary": {
    "windowCount": 3,
    "meanConfusionScore": 0.61,
    "confusionTrendSlope": -0.082,
    "goalTrendSlope": 0.044,
    "hintTrendSlope": 0.012,
    "errorRateTrendSlope": -0.021,
    "volatilityScore": 0.09,
    "momentumScore": 0.18,
    "recentStateTransitions": "struggling→recovering→stable"
  },
  "trajectorySummary": {
    "isImproving": true,
    "isDeteriorating": false,
    "isVolatile": false,
    "stabilityScore": 0.64,
    "predictedNearTermRisk": "low",
    "insufficientData": false
  }
}
```

Both fields are `null` when fewer than 2 windows are available. Existing clients that ignore
unknown JSON fields are unaffected.

### `GET /api/recursor/session-timeline/{sessionId}` — new endpoint

Demo-friendly endpoint. Reads in-memory session state only; no ADX call.

**Request:** `GET /api/recursor/session-timeline/a1b2c3d4-...`

**Response (200):**
```json
{
  "sessionId": "a1b2c3d4-...",
  "windowCount": 3,
  "windows": [
    { "windowIndex": 0, "confusionScore": 0.72, "goalUnderstanding": 0.38, "hintDependenceScore": 0.51, "behaviorState": "struggling", "createdAtUtc": "2026-04-28T10:00:00Z" },
    { "windowIndex": 1, "confusionScore": 0.54, "goalUnderstanding": 0.55, "hintDependenceScore": 0.42, "behaviorState": "recovering", "createdAtUtc": "2026-04-28T10:05:00Z" },
    { "windowIndex": 2, "confusionScore": 0.31, "goalUnderstanding": 0.72, "hintDependenceScore": 0.28, "behaviorState": "stable",     "createdAtUtc": "2026-04-28T10:10:00Z" }
  ],
  "currentTrajectory": {
    "isImproving": true,
    "isDeteriorating": false,
    "isVolatile": false,
    "stabilityScore": 0.64,
    "predictedNearTermRisk": "low",
    "insufficientData": false
  }
}
```

**Response (404):** `{ "error": "Session 'xxx' not found." }`

---

## Runtime flow

In `RecursorIngestionService.ProcessBatchAsync`, after the multi-signal guardrail runs:

1. **Stamp `OverallBehaviorState`** on `session.RecentSnapshots[^1]` from the guardrail result.
2. **Persist** session (one additional `Update` call).
3. **Extract** `SequenceFeatureSummary` from the updated snapshots. Returns `null` when fewer than 2.
4. **Classify** → `TrajectorySummary` (or `null` when extract returned `null`).
5. **Training row**: mapper receives `sequenceSummary` + `trajectorySummary` and fills the 9 new columns.
6. When an **adaptation is produced**, `Phase10TrajectoryNotes` is stamped before any ingest path
   (including Phase 8A veto and Phase 8E suppress paths — all ADX ingestion paths get the notes).
7. **All `IngestionResult` return paths** carry `SequenceSummary` and `TrajectorySummary` so the
   API response surfaces them regardless of whether an adaptation fired.

---

## What Phase 10A does NOT do

- Does not retrain or modify any ML model.
- Does not change existing adaptation decisions.
- Does not change the adaptation policy or guardrail rules.
- Does not add new ADX tables.
- Does not change `AdaptationPolicyService`, `PhaseGuardrailModifierService`, or
  `PolicyReliabilityWeightingService`.
- Does not change the Unity/client parameter-change response shape.
- Does not change Phase 8A, 8E, 9C, 9D, or 9E behavior.
- Does not block or gate any pipeline step.

---

## Verification KQL

### Confirm trajectory notes are being produced

```kql
AdaptationDecisions_v2
| where Phase10TrajectoryNotes != ""
| project SessionId, Phase10TrajectoryNotes, ingestion_time()
| take 50
```

### Distribution of trajectory labels

```kql
AdaptationDecisions_v2
| where Phase10TrajectoryNotes != ""
| extend Trajectory = extract(@"trajectory: (\w+)", 1, Phase10TrajectoryNotes)
| summarize Count = count() by Trajectory
| order by Count desc
```

### Risk distribution

```kql
AdaptationDecisions_v2
| where Phase10TrajectoryNotes != ""
| extend Risk = extract(@"predicted-risk=(\w+)", 1, Phase10TrajectoryNotes)
| summarize Count = count() by Risk
```

### Confusion slope vs risk tier

```kql
BehaviorStateTrainingRows
| where SequenceWindowCount > 0
| summarize AvgSlope = avg(ConfusionTrendSlope), Count = count() by PredictedNearTermRisk
| order by PredictedNearTermRisk asc
```

### Volatility over time (session trend)

```kql
BehaviorStateTrainingRows
| where SequenceWindowCount > 0
| summarize AvgVolatility = avg(VolatilityScore) by bin(CreatedAtUtc, 1h)
| order by CreatedAtUtc asc
```

---

## Manual test plan

### 1. Single-window session — no trajectory data

1. Start a session, send one batch that produces one feature window.
2. Call `POST /api/recursor/events/batch`.
3. Verify response: `sequenceSummary` and `trajectorySummary` are both `null`.
4. Call `GET /api/recursor/session-timeline/{sessionId}`.
5. Verify `windowCount = 1`, `currentTrajectory = null`.

### 2. Multi-window session — trajectory data present

1. Same session, send additional batches until 3 feature windows are produced.
2. Verify response has non-null `sequenceSummary.windowCount = 3`.
3. Verify `recentStateTransitions` is a non-empty string.
4. Verify `predictedNearTermRisk` is one of `"low"`, `"medium"`, `"high"`.

### 3. Timeline endpoint reflects in-memory state

1. After 3 windows, call `GET /api/recursor/session-timeline/{sessionId}`.
2. Verify `windows` array has 3 entries with ascending `windowIndex`.
3. Verify `behaviorState` is non-empty on each entry.
4. Verify `currentTrajectory.insufficientData = false`.

### 4. Unknown session returns 404

1. `GET /api/recursor/session-timeline/nonexistent-session-id`
2. Expect HTTP 404.

### 5. ADX trajectory notes (requires live ADX)

1. Run session to produce an adaptation.
2. Query `AdaptationDecisions_v2` for the row.
3. Verify `Phase10TrajectoryNotes` contains `"trajectory:"`, `"windows="`, `"confusion-slope="`, `"stability="`.
