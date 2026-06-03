# Phase 10D: Elevated-Risk Binary Temporal Predictor

## Why this exists

The 3-class temporal risk model (Phase 10C-2) predicts `low / medium / high` near-term risk.
In practice, the `medium` class is weak and `high` recall at horizon 3 is poor.
This is a known limitation of 3-class models on imbalanced time-series data with a small label set.

The operational question that matters most for future guardrails is simpler:

> **Is the learner likely to enter elevated risk in the next 1–3 windows?**

Phase 10D-1 answers that binary question directly by collapsing the label space:

| Original label | Binary label |
|----------------|-------------|
| `low`          | `low`       |
| `medium`       | `elevated`  |
| `high`         | `elevated`  |

A binary model trained on this collapsed vocabulary learns a cleaner decision boundary and
produces higher precision and recall for the `elevated` class than the 3-class model achieves
for `medium` + `high` combined.

## How it complements the 3-class model

The two models answer different questions and are stored separately:

| Aspect              | 3-class (Phase 10C-2)                 | Binary (Phase 10D-1)                    |
|---------------------|---------------------------------------|-----------------------------------------|
| Labels              | `low` / `medium` / `high`             | `low` / `elevated`                      |
| ADX table           | `TemporalRiskPredictions`             | `TemporalElevatedRiskPredictions`        |
| Model version       | `temporal-risk-v1`                    | `temporal-elevated-risk-v1`             |
| Operational question | What risk tier is the learner in?    | Will the learner enter elevated risk?   |
| Best use case       | Nuanced severity grading              | Binary alerting / guardrail gate        |
| Config keys         | `TemporalRiskH{1,2,3}ModelPath`       | `TemporalElevatedRiskH{1,2,3}ModelPath` |

Separate ADX tables avoid analytics ambiguity: joining `TemporalRiskPredictions` on a
`low` / `elevated` vocabulary would mix two incompatible label sets.

## Why it remains shadow-only

1. **No production validation yet.** Precision and recall figures come from a held-out
   slice of the same ADX dataset used for training. That is not sufficient for a policy gate.
2. **No false-positive cost model.** Suppressing or triggering an adaptation based on an
   elevated-risk prediction requires knowing how costly a false positive is for learner
   experience. That analysis has not been done.
3. **Volume.** Early deployments may have fewer than a few hundred training rows per horizon.
   Binary classification on small, class-imbalanced data over-fits easily.

The model is useful now for:
- Logging and monitoring the live prediction rate.
- Measuring how often `elevated` predictions co-occur with actual `medium` / `high` outcomes.
- Providing a secondary signal for future manual review in the dashboard.

## Guardrail readiness checklist (before promoting to active)

The following gates apply before any elevated-risk prediction is used to modify adaptation
behavior:

1. At least 500 labeled training rows per horizon with balanced class distribution.
2. Elevated precision >= 0.75 on a held-out test set.
3. Elevated recall >= 0.70 on a held-out test set.
4. Live false-positive rate < 20% measured over at least 5 sessions.
5. Human review of one full session timeline in the dashboard showing plausible predictions.
6. Explicit approval in the policy config (`Recursor:PolicyReliability`) before any
   adaptation decision reads the prediction.

## Training endpoint

```
POST /api/recursor/train-temporal-elevated-risk
```

Returns `TemporalElevatedRiskTrainingReport`:

```json
{
  "warning": "Shadow-only binary elevated-risk model...",
  "totalRowsQueried": 1200,
  "horizons": [
    {
      "horizon": 1,
      "rowCount": 400,
      "labelDistribution": { "low": 280, "medium": 80, "high": 40 },
      "accuracy": 0.84,
      "elevatedPrecision": 0.78,
      "elevatedRecall": 0.71,
      "elevatedF1": 0.74,
      "falsePositives": 12,
      "falseNegatives": 17,
      "modelPath": "Recursor/TrainingModels/temporal_elevated_risk_h1_v1.zip"
    },
    ...
  ]
}
```

`labelDistribution` shows the original 3-class counts before collapsing.
Restart the server after training to reload the new model files.

## Model paths (appsettings.json)

```json
"Recursor": {
  "Models": {
    "TemporalElevatedRiskH1ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h1_v1.zip",
    "TemporalElevatedRiskH2ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h2_v1.zip",
    "TemporalElevatedRiskH3ModelPath": "Recursor/TrainingModels/temporal_elevated_risk_h3_v1.zip"
  }
}
```

## Pipeline wiring

After the Phase 10C-2 3-class prediction block in `RecursorIngestionService`, the elevated-risk
prediction runs as a separate try/catch block:

1. Call `ITemporalElevatedRiskPredictionService.Predict(embedding)`.
2. For each non-null horizon prediction, log and ingest one row to `TemporalElevatedRiskPredictions`.
3. No adaptation decision reads or is affected by the elevated-risk prediction.
4. The block is non-blocking — any exception is caught and logged, and the pipeline continues.

## Dashboard

The dashboard `WindowTimelineDto` now includes `ElevatedRiskPredictions` alongside
`TemporalRiskPredictions`. The timeline builder queries `TemporalElevatedRiskPredictions`
from ADX and attaches the result per window. Model version and predicted label
(`low` / `elevated`) are visible in the session detail view.
