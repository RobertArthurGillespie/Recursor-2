# Phase 10C-2: Baseline Temporal Risk Predictor

## Purpose

Phase 10C-2 validates the end-to-end temporal prediction pipeline:

1. Train a shadow-only ML.NET multiclass model that predicts `TargetNearTermRisk` from `TemporalEmbeddingVector.Values`.
2. Run that model at runtime during each ingestion window (shadow only).
3. Persist predictions to a new ADX table `TemporalRiskPredictions` for observation.
4. **Do not change adaptation behavior.** This phase is purely observability.

---

## Why `TargetNearTermRisk` first

`TargetNearTermRisk` has three well-separated classes (`low`, `medium`, `high`) that derive
from deterministic trajectory-classification logic already implemented in `TrajectoryAnalysisService`.
This makes it a meaningful but well-grounded first target: the model can be evaluated against a
known reference, making accuracy metrics interpretable without needing expert ground-truth labeling.

`TargetBehaviorState` is deferred because it has an `"unknown"` label concentrated in the
steady-user archetype. Until that label coverage issue is resolved, using it as the primary
model target would produce artificially high confusion rates and misleading metrics.

---

## Dataset requirements

Before training, the ADX tables must have sufficient joined rows:

```kql
TemporalEmbeddings
| join kind=inner (TemporalPredictionTargets) on SessionId, UserId
| where WindowIndex == SourceWindowIndex
| where EmbeddingVersion == "v1.0"
| summarize count() by Horizon, TargetNearTermRisk
```

Minimum: **10 rows per horizon** with at least **2 distinct label classes**.
Recommended: **50+ rows per horizon** for a meaningful baseline accuracy measurement.

---

## How to train

1. Ensure ADX is configured and the tables above have data.
2. Call the training endpoint (dev/admin only):

```
POST /api/recursor/train-temporal-risk
```

The endpoint returns a `TemporalRiskTrainingReport` with:
- `totalRowsQueried` — total rows returned by the join query
- `horizons[]` — per-horizon summary:
  - `horizon` — 1, 2, or 3
  - `rowCount` — rows used for this horizon
  - `labelDistribution` — `{"low": N, "medium": N, "high": N}`
  - `macroAccuracy` — macro-averaged accuracy on the held-out 20% test split
  - `logLoss` — multiclass log loss
  - `modelPath` — the artifact's path inside its published immutable version directory
    (`Recursor/TrainingModels/versions/temporal-risk/{version}/h{horizon}.zip`) — **not** the
    flat path the running app actually loads from (see below)
  - `error` — set if training failed or was skipped for this horizon

3. **Point the runtime config at the published artifacts, then restart or redeploy.**
   Unlike the elevated-risk and behavior-state families, `TemporalRiskPredictionService`'s
   runtime wiring in `Program.cs` only ever loads from the three explicit
   `Recursor:Models:TemporalRiskH{1,2,3}ModelPath` config keys — it does not auto-derive a path
   from `TemporalRiskModelVersion` the way the other two families do. After training, set those
   three keys to the `h1.zip`/`h2.zip`/`h3.zip` paths inside the version directory the training
   report's `modelPath` values point at (see `docs/recursor-model-versioning.md` for the full
   artifact contract). `TemporalRiskPredictionService` is a singleton that loads model files
   once at startup — the running process will not pick up a newly published version until the
   app restarts with the updated config.

After restart, `TemporalRiskPredictionService` will load all three horizon models and
run shadow inference on every ingestion window.

> **Azure App Service caution:** If the app is deployed as a read-only package (the default
> zip-deploy mode), relative model paths under `Recursor/TrainingModels/versions/...` will
> refer to a location inside the read-only package mount and training writes will fail. In that
> environment, either train locally and include the published version directory in the
> deployment artifact, or set the three `Recursor:Models:TemporalRiskH*ModelPath` config keys to
> absolute paths on a writable volume (e.g. `D:\home\data\models\temporal_risk_h1_v1.zip` on
> Windows App Service — an explicit override path is not required to live under `ModelRoot` or
> follow the version-directory naming).

---

## Model paths (configured in `appsettings.json`)

| Config key | Default path |
|---|---|
| `Recursor:Models:TemporalRiskH1ModelPath` | `Recursor/TrainingModels/versions/temporal-risk/temporal-risk-v1/h1.zip` |
| `Recursor:Models:TemporalRiskH2ModelPath` | `Recursor/TrainingModels/versions/temporal-risk/temporal-risk-v1/h2.zip` |
| `Recursor:Models:TemporalRiskH3ModelPath` | `Recursor/TrainingModels/versions/temporal-risk/temporal-risk-v1/h3.zip` |

Paths are relative to the server `ContentRootPath` unless absolute. These are explicit
per-horizon overrides — temporal-risk's runtime wiring always requires them (it does not
auto-derive a path from a configured version label the way elevated-risk/behavior-state do; see
`docs/recursor-model-versioning.md`).

---

## How to verify prediction rows

After training, restarting, and running sessions:

```kql
// Row count by horizon and predicted label
TemporalRiskPredictions
| summarize count() by Horizon, PredictedNearTermRisk, ModelVersion
| order by Horizon asc

// Recent predictions for a specific session
TemporalRiskPredictions
| where SessionId == "<your-session-id>"
| order by CreatedAtUtc desc

// Average confidence by horizon
TemporalRiskPredictions
| summarize avg(Confidence) by Horizon, PredictedNearTermRisk
```

---

## Algorithm

**SdcaMaximumEntropy** (Stochastic Dual Coordinate Ascent, multiclass version) from the
base `Microsoft.ML` package. This is a linear model appropriate for a baseline slice:
fast, deterministic (seeded at 42), and interpretable. No deep learning is used.

Pipeline per horizon:
```
MapValueToKey("Label") → NormalizeMinMax("Features") → SdcaMaximumEntropy → MapKeyToValue("PredictedLabel")
```

The `Features` vector is the 25-dimensional `TemporalEmbeddingVector.Values` array
(see `TemporalEmbeddingService.FeatureNames` for the stable feature index mapping).

---

## Why predictions are shadow-only

Phase 10C-2 exists to validate the prediction pipeline infrastructure, not to make the
system smarter about adaptation. Specifically:

- The model is a **baseline with a small dataset** — accuracy is expected to be modest.
- The model's label space (`low`/`medium`/`high` near-term risk) is **derived from the same
  trajectory signals** already used by the adaptation policy. Looping it back into the policy
  before validating it independently would create circular feedback.
- Before any temporal model influences adaptation, it must be evaluated against
  **out-of-sample sessions** and reviewed via the existing Phase 8C/8D analytics pipeline.

Shadow-only predictions:
- Are logged at `Information` level per window/horizon
- Are persisted to `TemporalRiskPredictions` in ADX for offline analysis
- **Do not modify `ParameterChanges`, intervention families, guardrail behavior, or any
  Phase 8E/9D/9E suppression or promotion logic**

---

## New files

| File | Purpose |
|---|---|
| `Server/Recursor/ML/TemporalRiskModels.cs` | Data classes for training and inference |
| `Server/Recursor/Adx/AdxTemporalTrainingQueryService.cs` | ADX join query → training rows |
| `Server/Recursor/ML/TemporalRiskModelTrainer.cs` | Per-horizon SdcaMaximumEntropy trainer |
| `Server/Recursor/ML/TemporalRiskPredictionService.cs` | Singleton shadow inference service |
| `docs/kql/phase10c-temporal-risk-predictions-setup.kql` | ADX table + CSV mapping setup |
| `NCATAIBlazorFrontendTest.Tests/Recursor/Phase10CTemporalRiskPredictorTests.cs` | Unit tests |

## Changed files

| File | Change |
|---|---|
| `AdxRowModels.cs` | Added `TemporalRiskPredictionRow` |
| `AdxRowMapper.cs` | Added `MapTemporalRiskPrediction` |
| `AdxIngestionService.cs` | Added `IngestTemporalRiskPredictionAsync` |
| `RecursorController.cs` | Added `POST /api/recursor/train-temporal-risk` |
| `RecursorIngestionService.cs` | Wired Phase 10C-2 shadow block after Phase 10B |
| `Program.cs` | Registered 3 new services + model paths |
| `appsettings.json` | Added 3 temporal risk model path keys |
