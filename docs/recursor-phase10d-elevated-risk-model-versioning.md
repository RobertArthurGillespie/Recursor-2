# Recursor Phase 10D-4 — Elevated-Risk Model Versioning & Evaluation Comparison

## Why versioning was added

Before Phase 10D-4, the elevated-risk predictor always stamped `ModelVersion = "temporal-elevated-risk-v1"` on every prediction row, regardless of retraining. This made it impossible to tell which predictions came from which training run, and prevented any meaningful before/after comparison.

Phase 10D-4 makes every training run produce a clearly versioned artifact (e.g. `temporal_elevated_risk_h1_v2.zip`) and stamps that version on all predictions emitted by the loaded model. A new analytics endpoint lets you query accuracy, precision, recall, and F1 split by model version so you can compare v1 vs v2 from real session data.

**Nothing in this phase changes adaptation behavior.** The elevated-risk predictor remains shadow-only.

---

## How to train a new version

### Using the default version (v1)

```
POST /api/recursor/train-temporal-elevated-risk
```

Uses the version configured in `appsettings.json` → `Recursor:Models:TemporalElevatedRiskModelVersion`
(default: `temporal-elevated-risk-v1`).

Model files saved to paths configured in `appsettings.json`:
- `Recursor/TrainingModels/temporal_elevated_risk_h1_v1.zip`
- `Recursor/TrainingModels/temporal_elevated_risk_h2_v1.zip`
- `Recursor/TrainingModels/temporal_elevated_risk_h3_v1.zip`

### Training a new version (v2)

```
POST /api/recursor/train-temporal-elevated-risk?modelVersion=temporal-elevated-risk-v2
```

- Only letters, numbers, dash, underscore, and dot are allowed in `modelVersion`.
- The version suffix is extracted (`v2` from `temporal-elevated-risk-v2`).
- Model files are saved to **new paths** that do not overwrite v1:
  - `Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip`
  - `Recursor/TrainingModels/temporal_elevated_risk_h2_v2.zip`
  - `Recursor/TrainingModels/temporal_elevated_risk_h3_v2.zip`

The training report includes `ModelVersion`, `GeneratedAtUtc`, and per-horizon metrics (accuracy, precision, recall, F1, FP, FN) for the test split.

---

## How to configure the app to load v2

After training v2 and verifying the model files exist, update `appsettings.json`:

```json
"Recursor": {
  "Models": {
    "TemporalElevatedRiskModelVersion": "temporal-elevated-risk-v2",
    "TemporalElevatedRiskH1ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip",
    "TemporalElevatedRiskH2ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h2_v2.zip",
    "TemporalElevatedRiskH3ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h3_v2.zip"
  }
}
```

Then **restart or redeploy** the app. `TemporalElevatedRiskPredictionService` loads models once at startup and stamps `LoadedModelVersion` on every prediction row for the lifetime of that process.

---

## How to run validation sessions for the new model

After redeploying with v2 config:

1. Start sessions normally via `POST /api/recursor/sessions/start`.
2. Submit event batches via `POST /api/recursor/events/batch`.
3. The prediction service will produce rows with `ModelVersion = temporal-elevated-risk-v2` in `TemporalElevatedRiskPredictions`.
4. Simultaneously, `TemporalPredictionTargets` captures the observed future outcomes that the predictions can be evaluated against.

Run enough sessions to accumulate statistically meaningful data before comparing.

---

## How to compare v1 vs v2

```
GET /api/recursor/dashboard/elevated-risk/model-comparison
```

Returns a list of `ElevatedRiskModelComparisonRow` objects grouped by `(ModelVersion, Horizon)`:

| Field            | Description                                          |
|------------------|------------------------------------------------------|
| ModelVersion     | e.g. `temporal-elevated-risk-v1`                     |
| Horizon          | 1, 2, or 3                                           |
| Total            | Total rows where prediction and target overlap       |
| Correct          | Count where predicted == actual elevated label       |
| Accuracy         | Correct / Total                                      |
| ActualElevated   | Count of rows where the observed outcome was elevated|
| TruePositive     | Correctly predicted elevated                         |
| FalseNegative    | Missed elevated (predicted low, was elevated)        |
| FalsePositive    | False alarm (predicted elevated, was low)            |
| ElevatedPrecision| TP / (TP + FP), null if no elevated predictions     |
| ElevatedRecall   | TP / (TP + FN), null if no actual elevated rows     |
| ElevatedF1       | Harmonic mean of precision + recall                  |

The dashboard also exposes this as a collapsible table at `/recursor-dashboard` → "Show Elevated-Risk Model Comparison".

---

## Why predictions remain shadow-only

The elevated-risk predictor is intentionally disconnected from the adaptation pipeline. It:
- Does not suppress policy families
- Does not protect policy families
- Does not produce parameter changes
- Does not influence adaptation decisions in any way

Its purpose is to build a labeled dataset of predictions + outcomes so future active-mode use is grounded in measured accuracy rather than assumption.

---

## Why v1/v2 comparison should be based on fresh live prediction rows

The v1 model rows in `TemporalElevatedRiskPredictions` were produced by a different model instance from a different training run. Comparing them in the same query is fair as long as:

1. Both models were evaluated against the same target distribution (same sim, scenario, user population).
2. The comparison is based on rows where `TemporalPredictionTargets` has an observed outcome.

Do not compare training-set accuracy (from the training report) to live accuracy (from the comparison endpoint) — they use different splits and different data.

---

## Example end-to-end workflow

```
# 1. Train v2
POST /api/recursor/train-temporal-elevated-risk?modelVersion=temporal-elevated-risk-v2

# 2. Verify model files exist at:
#    Server/Recursor/TrainingModels/temporal_elevated_risk_h1_v2.zip  (etc.)

# 3. Update appsettings.json to point at v2 paths + set TemporalElevatedRiskModelVersion = temporal-elevated-risk-v2

# 4. Restart/redeploy the app

# 5. Run validation sessions — the new model stamps v2 on all prediction rows

# 6. After sufficient data is collected:
GET /api/recursor/dashboard/elevated-risk/model-comparison

# 7. Compare v1 rows vs v2 rows in the result — look for:
#    - Higher ElevatedRecall (fewer missed elevated risks)
#    - Maintained or improved ElevatedPrecision
#    - Higher ElevatedF1 overall
```

---

## v1 files remain intact

Training v2 writes to `temporal_elevated_risk_h{1,2,3}_v2.zip`. The v1 `.zip` files at their configured paths are never touched. Reverting to v1 is as simple as pointing `appsettings.json` back at the v1 paths and restarting.
