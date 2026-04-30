# Phase 10B — Temporal Embeddings + Next-N-Window Prediction Dataset

## Purpose

Phase 10B creates the **temporal learning substrate** for Recursor: a reproducible,
fixed-length feature vector (embedding) for each processed learner window, together with
labelled prediction targets that link a source window to later observed outcomes.

No deep learning model is trained in this phase. The outputs are training *data*, not
trained *models*. Phase 10C will read these tables and fit sequence prediction models
(LSTM, Transformer, or similar).

---

## Why engineered embeddings first?

Neural embeddings (learned by an LSTM or Transformer) require enough historical data to
train reliably. Engineered embeddings give us:

1. **Immediate interpretability** — every dimension has a name and unit we understand.
2. **A baseline** — a linear model on engineered features is a sanity-check reference
   before adding complexity.
3. **A cold-start path** — new simulations and new users have no training history; an
   engineered vector degrades gracefully whereas a neural encoder would produce garbage.
4. **Backwards-compatible schema** — the `EmbeddingVersion` field lets Phase 10C models
   detect schema changes and retrain when the vector layout changes.

The same 25-feature layout can later be *prepended* to a neural encoder's input, making
the transition to Phase 10C additive rather than a replacement.

---

## Embedding vector layout (v1.0, 25 features)

| Index | Feature name             | Source                                   |
|-------|--------------------------|------------------------------------------|
| 0     | ConfusionScore           | Current snapshot                         |
| 1     | HintDependenceScore      | Current snapshot                         |
| 2     | GoalUnderstanding        | Current snapshot                         |
| 3     | SelfCorrection           | Current snapshot                         |
| 4     | MeanConfusionScore       | SequenceFeatureSummary (Phase 10A)        |
| 5     | ConfusionTrendSlope      | SequenceFeatureSummary (OLS slope)        |
| 6     | GoalTrendSlope           | SequenceFeatureSummary (OLS slope)        |
| 7     | HintTrendSlope           | SequenceFeatureSummary (OLS slope)        |
| 8     | VolatilityScore          | SequenceFeatureSummary (std dev confusion)|
| 9     | MomentumScore            | SequenceFeatureSummary                   |
| 10    | StabilityScore           | TrajectorySummary (Phase 10A)            |
| 11    | IsImproving              | TrajectorySummary — 0.0 or 1.0           |
| 12    | IsDeteriorating          | TrajectorySummary — 0.0 or 1.0           |
| 13    | IsVolatile               | TrajectorySummary — 0.0 or 1.0           |
| 14    | PredictedRiskLow         | TrajectorySummary — one-hot              |
| 15    | PredictedRiskMedium      | TrajectorySummary — one-hot              |
| 16    | PredictedRiskHigh        | TrajectorySummary — one-hot              |
| 17    | StateStruggling          | MultiSignalGuardrail — one-hot           |
| 18    | StateRecovering          | MultiSignalGuardrail — one-hot           |
| 19    | StateStable              | MultiSignalGuardrail — one-hot           |
| 20    | StateMastering           | MultiSignalGuardrail — one-hot           |
| 21    | StateConflicted          | MultiSignalGuardrail — one-hot           |
| 22    | CurrentDifficulty        | Session adaptive parameters              |
| 23    | CurrentTimePressure      | Session adaptive parameters              |
| 24    | CurrentErrorTolerance    | Session adaptive parameters              |

`EmbeddingVersion = "v1.0"` is stamped on every row. If features are added or reordered,
bump the version so downstream queries can filter by version.

---

## Prediction target schema

When window **K** is processed, up to three prediction target rows are written:

| SourceWindowIndex | TargetWindowIndex | Horizon |
|-------------------|-------------------|---------|
| K − 1             | K                 | 1       |
| K − 2             | K                 | 2       |
| K − 3             | K                 | 3       |

Each row records what *actually happened* at window K (ground truth):

- `TargetBehaviorState` — guardrail-stamped overall state at window K
- `TargetNearTermRisk` — trajectory risk classification at window K
- `TargetConfusionScore`, `TargetGoalUnderstanding`, `TargetHintDependenceScore`

If the session has fewer than 2 snapshots in memory, no targets are written (safe skip).

---

## How TemporalEmbeddings joins with TemporalPredictionTargets

```kql
TemporalEmbeddings
| join kind=inner (
    TemporalPredictionTargets
    | where Horizon == 1
) on SessionId, $left.WindowIndex == $right.SourceWindowIndex
| project SessionId, UserId, SourceWindowIndex, EmbeddingVersion,
          Values, TargetBehaviorState, TargetNearTermRisk, TargetConfusionScore
```

- `TemporalEmbeddings.WindowIndex` = the source window (the embedding describes this window)
- `TemporalPredictionTargets.SourceWindowIndex` = the same window
- `TemporalPredictionTargets.TargetWindowIndex` = the window that was observed later

This join produces a supervised training dataset: **input = embedding at window K,
label = what happened at window K+N**.

---

## How Phase 10C will train prediction models

Phase 10C will:

1. Pull the joined training dataset from ADX.
2. Optionally concatenate embeddings across sequential windows to form a sequence input
   (e.g., [embedding at K-2, K-1, K] → label at K+1).
3. Train a model (LSTM, Transformer encoder, or gradient-boosted trees) on this data.
4. Score new embeddings at runtime against the trained model to predict next-window state.
5. Use that prediction to inform adaptation decisions (trajectory-aware policy).

The engineered v1.0 embedding serves as both the first model input and a strong baseline
for comparison.

---

## ADX tables

| Table                   | Rows written per window | Purpose                            |
|-------------------------|-------------------------|------------------------------------|
| `TemporalEmbeddings`    | 1                       | Fixed-length embedding per window  |
| `TemporalPredictionTargets` | 1–3                 | Ground-truth labels for training   |

Both tables are written non-blocking; a failure never stops the adaptation pipeline.

---

## API endpoint

`GET /api/recursor/session-timeline/{sessionId}/temporal`

Returns (on-demand, from in-memory state — no ADX query):
- `Windows` — per-window timeline entries (same as the Phase 10A endpoint)
- `LatestEmbedding` — the embedding built from the most recent snapshot
- `PredictionTargets` — up to 3 target rows available given current history
- `CurrentTrajectory` — trajectory classification (TrajectorySummary)

This endpoint is for demo and debugging only. The `featureVector` and `guardrail`
arguments are null when called via this path, so indices 17–24 may be zero or
reflect only the snapshot's stamped state.

---

## Invariants

- `TemporalEmbeddingService.FeatureNames.Length == 25` — tested
- `embedding.Values.Length == FeatureNames.Length` — tested
- Index i in `Values` always matches `FeatureNames[i]` — tested
- One-hot encodings sum to at most 1.0 within each group — by construction
- `BuildEmbedding` and `BuildPredictionTargets` never mutate their inputs — tested
- Adaptation behavior is unchanged by Phase 10B — no new intervention families,
  no changes to Phase 8E / 9D / 9E logic
