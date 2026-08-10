# Phase 10E — Behavior-State Label Policy

Centralized normalizer: `Server/Recursor/Services/BehaviorStateLabelPolicy.cs`.

## Canonical label set

```
struggling | recovering | stable | mastering | conflicted
```

These are the exact five values `MultiSignalGuardrailService.Evaluate` (Phase 7C) produces —
see `Server/Recursor/Models/GuardrailModels.cs`, `MultiSignalGuardrailSummary.OverallBehaviorState`
doc comment, which already documented this set before Phase 10E existed. `TemporalEmbeddingService
.BuildPredictionTargets` stamps `TargetBehaviorState = current.OverallBehaviorState`, so this is
also the label vocabulary `TemporalPredictionTargets.TargetBehaviorState` uses today.

## `stable` vs. `steady`: the decision

The Phase 10E roadmap text listed `"stable" or "steady"` as a candidate label. The runtime has
never produced `"steady"` anywhere in the codebase — `MultiSignalGuardrailService` has always
used `"stable"`. Phase 10E kept the runtime's existing term (`stable`) as canonical, per the
constraint "do not automatically impose \[the roadmap's] list if the runtime currently produces
a different legitimate state set." `"steady"` is mapped as a legacy alias to `"stable"` purely
defensively (see below) — it is not expected to ever appear in real data.

## Legacy aliases

| Alias | Canonical | Status |
|---|---|---|
| `steady` | `stable` | Defensive only — not currently produced anywhere |

No other aliases exist. `BehaviorStateLabelPolicy.Normalize` also trims whitespace and is
case-insensitive for both canonical labels and aliases.

## Why `unknown` is not "missing" and is not "stable"

Two structurally different "no real answer" situations exist in `TargetBehaviorState`, and the
policy keeps them distinct rather than collapsing either into a guess:

1. **`Missing`** (blank/null `TargetBehaviorState`) — the guardrail evaluation threw or the
   pipeline hadn't stamped the window yet. `TrajectorySnapshot.OverallBehaviorState` defaults
   to `""`, and `RecursorIngestionService` swallows guardrail exceptions
   (`catch (Exception ex)` around the guardrail call), leaving the snapshot unstamped for that
   window. This is a pipeline gap, not a legitimate outcome.
2. **`NoSignal`** (`TargetBehaviorState == "unknown"`) — `MultiSignalGuardrailService.Evaluate`
   explicitly returns `"unknown"` when the rule-based hypothesis set is empty for that window
   (`if (labels.Count == 0) return new MultiSignalGuardrailSummary { OverallBehaviorState =
   "unknown" };`). The guardrail *ran successfully* and found nothing notable to report. This
   is meaningfully different from "stable" (which has its own explicit code path — the
   fallback default at the bottom of `Evaluate`, and the `labelStableMastery` branch) and
   meaningfully different from "missing data."

Both are excluded from training (see below) but are counted and reported separately in the
audit KQL and the trainer's `ExcludedRowCountsByReason`, satisfying "distinguish missing data
from a legitimate stable state" and "do not convert every unknown value to stable."

**No fix was made to `MultiSignalGuardrailService`.** It is consumed by other live paths
(Phase 8A guardrail modifier, dashboard rendering, hypothesis notes), so changing what it
returns risks live adaptation behavior — explicitly out of scope for a shadow-only phase. This
was a deliberate choice, not an oversight: see
[`recursor-phase10e-behavior-state-temporal-predictor.md`](recursor-phase10e-behavior-state-temporal-predictor.md)
"Known limitations" for the door left open to revisit this in a later phase, once `unknown`
rates from the real audit justify the (separate, riskier) decision to touch shared guardrail
logic.

## When a row is excluded from training

`TemporalBehaviorStateModelTrainingService.ClassifyRow` excludes a row under exactly one of:

| Reason | Trigger |
|---|---|
| `missing-or-invalid-embedding` | `EmbeddingValues.Length != 25` |
| `missing-target-label` | `TargetBehaviorState` is null/blank/whitespace |
| `unknown-no-signal` | `TargetBehaviorState == "unknown"` |
| `invalid-label-literal` | any other unrecognized string |

Only rows whose label resolves to `Valid` (one of the five canonical classes) are used for
training. No row is ever assigned a fabricated label to avoid exclusion.

## Reused by

- `TemporalBehaviorStateModelTrainingService.ClassifyRow` (training exclusion).
- `Server/Recursor/Adx/AdxDashboardQueryService.cs`,
  `DashboardTimelineBuilder.ComputeBehaviorStateCorrectness` — calls
  `BehaviorStateLabelPolicy.Normalize` directly on both the target and the prediction (not an
  ad-hoc `== "unknown"` string check), so a blank/`unknown`/unrecognized target is excluded as
  "pending," and a differently-cased or `steady`-aliased value on either side is normalized to
  its canonical form before comparing.
- `AdxDashboardQueryService.BuildBehaviorStateModelComparisonKql` and
  `BuildBehaviorStateCoverageKql` — both queries open with a shared `NormalizeBehaviorState` KQL
  function (`BehaviorStateNormalizationKql` constant) that reproduces the exact same rules as
  `BehaviorStateLabelPolicy.Normalize`: trim, lowercase, `steady`→`stable`, then classify as
  `missing` / `unknown` / `invalid` / a canonical label. This replaced two independent, looser
  KQL checks (`isempty(...) or ... == "unknown"`) that did not trim, lowercase, or apply the
  `steady` alias, and so could have silently miscounted a differently-cased or legacy value.
- `docs/kql/phase10e-temporal-behavior-state-target-audit.kql` and
  `docs/kql/phase10e-temporal-behavior-state-prediction-evaluation.kql` — updated to use the
  same shared `NormalizeBehaviorState` block (see those files) instead of ad-hoc literal checks.

## Query-service label decoupling (Stage 5 corrective pass)

`AdxTemporalTrainingQueryService.QueryTrainingRowsAsync` previously discarded any row whose
`TargetNearTermRisk` was blank *before* any trainer saw it — including rows with a perfectly
valid `TargetBehaviorState`. This silently starved the Phase 10E trainer of usable rows. The
shared query service now returns all structurally valid rows (correct embedding vector length)
regardless of label content; each trainer (temporal-risk, elevated-risk, Phase 10E) validates
and excludes rows against its own target column and reports its own
`ExcludedRowCountsByReason`.

## Audit results

Not yet run against production ADX data as part of this implementation — see
`docs/kql/phase10e-temporal-behavior-state-target-audit.kql` and the "Known limitations" /
"Criteria before Phase 10F begins" sections of the main Phase 10E design doc.
