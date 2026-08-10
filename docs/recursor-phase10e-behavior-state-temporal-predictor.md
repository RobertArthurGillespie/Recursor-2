# Phase 10E: Behavior-State Temporal Predictor (Shadow-Only)

## Why this exists

Phase 10D-1 predicts *near-term risk* (`low` / `elevated`) at horizons H1–H3. It answers "is
this window heading somewhere bad?" but not "which of the five behavior states is the learner
likely to be in?" — the more expressive question the roadmap describes as Phase 10E.

`TemporalPredictionTargets.TargetBehaviorState` has existed since Phase 10B but was never
consumed by any trainer. Phase 10E is the first thing to read it, train against it, and
persist a prediction back to ADX — reusing the exact H1/H2/H3, versioned-artifact, and
graceful-degradation architecture already proven by Phase 10D-1/10D-4.

**This model is strictly shadow-only.** It never feeds adaptation decisions, guardrails,
policy selection, hint behavior, difficulty, time pressure, or any other simulation
parameter, regardless of configuration.

## Canonical label set

The canonical behavior-state classes are the five values `MultiSignalGuardrailService`
(Phase 7C) already produces — documented in
`Server/Recursor/Models/GuardrailModels.cs`, `MultiSignalGuardrailSummary.OverallBehaviorState`:

```
struggling | recovering | stable | mastering | conflicted
```

The roadmap's candidate list used `steady`; the runtime has always used `stable`. Phase 10E
adopted the runtime's existing term rather than introducing a synonym — see
[`recursor-phase10e-behavior-state-label-policy.md`](recursor-phase10e-behavior-state-label-policy.md)
for the full rationale, the centralized normalizer, and how `unknown`/blank rows are handled.

**Nothing in `MultiSignalGuardrailService` was changed.** It is consumed elsewhere (Phase 8A
guardrail modifier, dashboard, hypothesis notes), so Phase 10E treats its output as a read-only
input and applies exclusion/normalization entirely downstream, in the new
`BehaviorStateLabelPolicy` class and the new trainer.

## Architecture (reused from Phase 10D-1/10D-4)

| Concern | Phase 10D-1 (elevated risk) | Phase 10E (behavior state) |
|---|---|---|
| Labels | `low` / `elevated` (binary) | 5 canonical classes (multiclass) |
| Feature vector | `TemporalEmbeddingVector.Values` (25-dim) | same, unchanged |
| Trainer | `TemporalElevatedRiskModelTrainingService` | `TemporalBehaviorStateModelTrainingService` |
| Prediction service | `TemporalElevatedRiskPredictionService` | `TemporalBehaviorStatePredictionService` |
| ADX predictions table | `TemporalElevatedRiskPredictions` | `TemporalBehaviorStatePredictions` |
| Train/validation split | Random 80/20 (`TrainTestSplit`) | **Session-level, grouped-stratified** (see below) |
| Versioned artifact | `versions/temporal-elevated-risk/{version}/h{n}.zip` | `versions/temporal-behavior-state/{version}/h{n}.zip` |
| Config section | `Recursor:Models:TemporalElevatedRisk*` | `Recursor:Models:TemporalBehaviorState*` |
| Enable/disable | Always runs when models load | `Recursor:Policies:EnableTemporalBehaviorStatePrediction` (default `false`) |

### Deliberate deviation: session-level, grouped-stratified train/validation split

The 3-class and elevated-risk trainers use `mlContext.Data.TrainTestSplit(data, testFraction: 0.2)`
— a random per-row split. Because adjacent windows from the same session share most of their
sequence-derived features, a random split can place near-duplicate windows on both sides,
inflating validation accuracy. Phase 10E's spec explicitly required avoiding this, so
`TemporalBehaviorStateModelTrainingService.SplitSessionsForTraining` splits **session IDs**
(not rows) 80/20, guaranteeing every window from a given session lands entirely on one side.
Sessions are additionally stratified by (dominant label, SimId) so a rare class or an
under-represented simulation is not accidentally isolated entirely on one side of the split,
and the split is deterministic from a recorded seed.

**Explicit coverage reporting.** After the split, `SplitSessionsForTraining` computes, relative
to the classes/simulations actually present in the trainable data: which classes/simulations
are absent from training, which are absent from validation, and an explicit
`CrossSimulationValidationStatus` — `NotApplicable` (≤1 simulation present), `InsufficientCoverage`
(more than one simulation present but at least one is missing from one side of the split), or
`Passed`. This status, plus the absent-class/absent-simulation lists, per-class and per-simulation
train/validation distributions, and train/validation session counts, are recorded directly in the
published version's `manifest.json` — a reader never has to re-run the split or re-query ADX to
know whether a version's combined metrics can honestly be described as cross-simulation validated.
A coverage gap never blocks training or publishing by itself (a model can still be trained and
published with `InsufficientCoverage`), but it is surfaced prominently in the training report's
top-level warning text.

### Exclusion, never fabrication

`TemporalBehaviorStateModelTrainingService.ClassifyRow` applies `BehaviorStateLabelPolicy` plus
an embedding-length guard to every candidate row and returns one of:

- **Trainable** — one of the five canonical labels.
- Excluded as `missing-or-invalid-embedding` — `EmbeddingValues.Length != 25`.
- Excluded as `missing-target-label` — blank/null `TargetBehaviorState`.
- Excluded as `unknown-no-signal` — `TargetBehaviorState == "unknown"`.
- Excluded as `invalid-label-literal` — any other unrecognized string.

Excluded-row counts are reported per horizon, per reason, in
`TemporalBehaviorStateHorizonTrainingSummary.ExcludedRowCountsByReason`. No row lacking
evidence is ever assigned a fabricated label.

### Metrics

Per horizon, `TemporalBehaviorStateModelTrainingService.ComputeMetricsSlice` reports (combined,
and separately per `SimId`):

- Micro accuracy
- Per-class precision / recall / F1 (`TemporalBehaviorStateHorizonTrainingSummary.Combined.PerClass`)
- Macro F1
- Majority-class baseline accuracy (computed from the *training* split's label distribution,
  not the validation slice — so it reflects what a naive always-predict-the-common-class model
  would score)
- Confusion matrix (`Dictionary<string actual, Dictionary<string predicted, int>>`)

Combined (all-simulation) metrics are never sufficient evidence of cross-simulation
generalization — always check the per-`SimId` breakdown before drawing conclusions about
`medical-supply-stocking` specifically.

## ADX schema

`docs/kql/phase10e-temporal-behavior-state-predictions-setup.kql` creates
`TemporalBehaviorStatePredictions`:

```
SessionId: string, UserId: string, SimId: string, ScenarioId: string,
WindowIndex: int, Horizon: int, PredictedBehaviorState: string, Confidence: real,
ClassProbabilities: dynamic, ModelVersion: string, CreatedAtUtc: datetime,
PredictionId: string
```

`ClassProbabilities` is a JSON object (`{"stable": 0.62, "struggling": 0.10, ...}`), keyed by
canonical label — extracted at runtime via the trained model's `Score` column slot names
(`TemporalBehaviorStatePredictionService.ExtractClassNames`), not a hardcoded array index.

`PredictionId` (appended as the last column — an additive schema change) is a deterministic
hash of `SessionId+WindowIndex+Horizon+ModelVersion`
(`BehaviorStatePredictionKeys.ComputePredictionId`, `Server/Recursor/Models/SessionModels.cs`).
The same logical prediction always produces the same `PredictionId` no matter which process,
App Service instance, or post-restart replay computes it — this is what lets every downstream
KQL query deduplicate defensively instead of relying solely on the in-memory, non-durable,
single-instance `CompletedBehaviorStatePredictionKeys` guard described below. ADX itself is
append-only, so physical duplicate rows are tolerated; only the logical prediction identity is
authoritative.

Because `PredictionId` was added to an already-populated table via an additive `.create-merge`,
rows ingested before that migration have `PredictionId == ""`. Every downstream query dedups by a
`LogicalPredictionId` that equals `PredictionId` when present and otherwise falls back to the
same composite identity `ComputePredictionId` hashes
(`legacy|SessionId|WindowIndex|Horizon|ModelVersion`) — see
`AdxDashboardQueryService.BehaviorStatePredictionDedupKql` and
`BehaviorStatePredictionKeys.ComputeLogicalPredictionId`. This keeps legacy rows deduplicating
per (session, window, horizon, version) instead of every blank-ID row collapsing into a single
logical prediction. No backfill of legacy rows is required.

## Runtime inference

Wired into `RecursorIngestionService.ProcessBatchAsync` immediately after the existing
Phase 10D-1 block, gated by `_policies.EnableTemporalBehaviorStatePrediction` (default
`false`). Idempotency is per-horizon, not a single scalar: `SessionDocument
.CompletedBehaviorStatePredictionKeys` records each `(WindowIndex, Horizon, ModelVersion)` key
immediately after that horizon's ADX ingest succeeds, so a retry after a partial failure (e.g.
H1 ingested, H2 threw) only reprocesses H2/H3, never re-ingests H1. A per-session
`SemaphoreSlim` (`RecursorIngestionService.BehaviorStatePredictionLocks`) serializes concurrent
calls for the same session so two racing requests can't both observe "not yet completed" for
the same horizon. Each horizon's ingest is wrapped in its own try/catch — a failure on one
horizon is logged and leaves only that horizon eligible for retry; it never blocks or corrupts
the others.

This in-memory guard is explicitly **not durable** — it does not survive a process restart and
does not coordinate across multiple App Service instances (session state is in-memory only, per
the Recursor architecture spec). The defense against restart/multi-instance replay is the
deterministic `PredictionId` described above: every downstream ADX reader deduplicates by
`PredictionId`, so a duplicate physical row from a post-restart replay can never inflate a count
or a metric, even though the in-memory guard that ordinarily prevents redundant ingestion in the
first place cannot itself survive the restart.

## Training endpoint

```
POST /api/recursor/train-temporal-behavior-state
POST /api/recursor/train-temporal-behavior-state?modelVersion=temporal-behavior-state-v2
```

Returns `TemporalBehaviorStateTrainingReport` (per-horizon: excluded-row counts by reason,
label distribution, train/validation session counts, combined metrics, per-simulation
metrics, resolved model path, or an `Error` string if the horizon couldn't be trained).

## Configuration (appsettings.json)

```json
"Recursor": {
  "Models": {
    "TemporalBehaviorStateModelVersion": "temporal-behavior-state-v1"
  },
  "Policies": {
    "EnableTemporalBehaviorStatePrediction": false
  }
}
```

The authoritative configuration is just the model family (hardcoded as
`TemporalBehaviorStateModelTrainingService.ModelFamily = "temporal-behavior-state"`) and
`TemporalBehaviorStateModelVersion` — the runtime resolver derives all three horizon paths from
these by looking under `Recursor/TrainingModels/versions/temporal-behavior-state/{version}/`.
See `docs/recursor-model-versioning.md` for the full artifact contract, manifest validation, and
activation procedure (shared across all three temporal model families).

`TemporalBehaviorStateH1ModelPath` / `H2ModelPath` / `H3ModelPath` remain supported as explicit
overrides (legacy/non-standard deployments only) — if set, each is honored exactly as
configured, with no basename/suffix matching requirement. Prefer the version-directory
convention above; only use explicit overrides when you have a specific reason to point at a
model file outside the standard layout.

`EnableTemporalBehaviorStatePrediction` lives under `Recursor:Policies` (not `Recursor:Models`)
to match the codebase's existing convention of feature-flagging pipeline behavior through
`RecursorPoliciesOptions`, which `RecursorIngestionService` already injects — no new
constructor dependency was needed for the flag itself.

## Dashboard

- `GET /api/recursor/dashboard/behavior-state/model-comparison` — per-(ModelVersion, Horizon,
  ClassLabel) support/precision/recall/F1, joined from `TemporalBehaviorStatePredictions` to
  `TemporalPredictionTargets` (rows with a blank/`unknown` target excluded).
- `GET /api/recursor/dashboard/behavior-state/coverage` — resolved / pending / invalid-target
  counts per (ModelVersion, Horizon).
- `GET /api/recursor/dashboard/behavior-state/confusion-matrix` — actual-vs-predicted counts for
  every (ActualClass, PredictedClass) pair among the five canonical classes, per (ModelVersion,
  Horizon); all 25 cells are always emitted, even with zero count, mirroring the "never let a
  class silently vanish" rule already applied to per-class metrics.
- All three endpoints above accept the same optional `simId` / `scenarioId` / `modelVersion` /
  `horizon` query filters, and return a typed `DashboardQueryResult<T>` (`Status` +
  `Rows`/`ErrorMessage`) so an ADX failure is never rendered as "no data" — see
  `Shared/RecursorDashboardContracts.cs`.
- `Client/Pages/RecursorDashboard.razor` — collapsible panel labeled **"Behavior-State
  Temporal Prediction — Shadow-only behavior-state prediction"**, aggregating the per-class
  rows into per-(ModelVersion, Horizon) micro accuracy and macro F1 client-side, with
  Simulation/Horizon/Scenario/Model-version filter controls and a rendered confusion-matrix
  table.
- `Client/Pages/RecursorSimSessionDetail.razor` and `RecursorDashboard.razor` timelines both
  gained three additional columns (`H1/H2/H3 State (shadow)`) showing predicted state →
  eventual outcome with a ✓/✗/— correctness marker, clearly separate from the existing
  near-term-risk H1/H2/H3 columns. Both pages also expose an explicit behavior-state
  model-version selector for the session being viewed.
- Session list/detail (`GET recent-sessions`, `GET session/{sessionId}`) also return typed
  results now — an ADX outage renders as a distinct diagnostic state, never silently as "No
  sessions found" or a false-negative "session not found" 404.

Neither page implies the shadow prediction controls anything — every panel and column is
explicitly labeled "shadow" / "shadow-only."

## Operational guide

### How to run the target-label audit

Open `docs/kql/phase10e-temporal-behavior-state-target-audit.kql` in the ADX Web UI (or run
via Kusto CLI) against your `RecursorDb`. Run each `// ── Section N` block independently. This
is read-only — no tables are created or altered.

### How to train a new model version

```
POST /api/recursor/train-temporal-behavior-state?modelVersion=temporal-behavior-state-v2
```

- `modelVersion` accepts letters, numbers, dash, underscore, dot only.
- Artifacts are published atomically as an **immutable version directory** —
  `Recursor/TrainingModels/versions/temporal-behavior-state/temporal-behavior-state-v2/`
  containing `h1.zip`, `h2.zip`, `h3.zip`, and `manifest.json`. An existing version is never
  retrained/overwritten; choose a new `modelVersion` label to train again. See
  `docs/recursor-model-versioning.md` for the full contract (this is shared across all three
  temporal model families, not specific to Phase 10E).

### Where artifacts are stored

`{ContentRootPath}/Recursor/TrainingModels/versions/temporal-behavior-state/{version}/h{1,2,3}.zip`
plus `manifest.json` in the same directory.

### How to configure a trained version

Update `appsettings.json`:

```json
"TemporalBehaviorStateModelVersion": "temporal-behavior-state-v2"
```

That alone is sufficient — the runtime resolver derives the H1/H2/H3 paths from the version
directory. Restart/redeploy — models load once at startup. At startup the app validates the
target version's `manifest.json` (family/version/completion/all three horizon artifacts present,
no horizon pointing at another version) and fails loudly if the directory exists but is
inconsistent; it does not fail if nothing has been published yet for that version (a model
simply doesn't load, same as today's "no model configured" state).

### How to confirm runtime inference is active

1. Set `Recursor:Policies:EnableTemporalBehaviorStatePrediction = true` (dev/test config only).
2. Restart the app.
3. Run a session through the normal ingestion flow.
4. Check application logs for `[TemporalBehaviorStatePredictionService] Model status —
   H1=... H2=... H3=... Version=...` at startup, and `Behavior-state shadow prediction...`
   per-window log lines during ingestion.

### How to verify ADX rows

```
TemporalBehaviorStatePredictions
| summarize count() by Horizon, PredictedBehaviorState, ModelVersion
| order by Horizon asc
```

(Also included as the verification query at the bottom of the setup `.kql` file.)

### How to inspect dashboard results

`/recursor-dashboard` → "Show Behavior-State Temporal Prediction — Shadow-only behavior-state
prediction", or open any session under `/recursor-sims` → the session detail timeline's
"H1/H2/H3 State (shadow)" columns.

### How to roll back to an earlier model version

Point `TemporalBehaviorStateModelVersion` and the three `H{n}ModelPath` keys back at the
previous version's `.zip` files and restart. Nothing needs to be deleted — every version's
artifacts coexist under `Recursor/TrainingModels/`.

## Known limitations

- No temporal-behavior-state model version has been trained/published against a live ADX
  cluster in any environment as of this writing — the immutable-publish/runtime-resolve path is
  verified only by a synthetic-data integration test
  (`Stage3ModelPublisherResolverIntegrationTests.cs`), which trains on synthetic rows in a
  temporary directory. The first real training run is a Phase 10F entry criterion (see below),
  not something this document can report the outcome of in advance.
- Metrics reported in this document are architectural — actual row counts, class balance, and
  per-simulation accuracy can only be known by running the audit KQL and the training endpoint
  against real ADX data. Do not treat this document as a substitute for that run.
- `medical-supply-stocking` may not yet have enough labeled windows per horizon to clear the
  10-row / 2-class training guard — if so, that horizon's per-sim entry will simply be absent
  from `BySimulation`, not silently defaulted.
- `ClassProbabilities` depends on the trained model exposing `Score` column slot names via
  ML.NET's standard annotation mechanism; if that ever fails for a given model file, the
  prediction still persists with an empty `ClassProbabilities` object — `PredictedBehaviorState`
  and `Confidence` are unaffected.
- The `WindowIndex`/`SourceWindowIndex`/`TargetWindowIndex` horizon semantics established in
  Phase 10B (H1/H2/H3 = snapshot-list-position offsets, not calendar time or `WindowIndex`
  deltas) were reused unchanged. This is consistent with how the 3-class and elevated-risk
  models already train, but is a modeling assumption worth re-examining in a future phase.

## Confirmation: shadow-only

- No adaptation-policy parameter, guardrail rule, or intervention-family selector reads
  `TemporalBehaviorStatePredictions`, `ITemporalBehaviorStatePredictionService`, or any Phase
  10E type. Verified two ways: reflection tests in
  `Phase10EBehaviorStateTemporalPredictorTests.cs`
  (`ApplyPolicy_ParameterTypes_ContainNoPhase10ETemporalBehaviorStateType`,
  `AdaptationDecisionDocument_And_ParameterChange_HaveNoBehaviorStatePredictionFields`), and a
  real end-to-end pipeline regression test,
  `Stage9ShadowOnlyAdaptationRegressionTests.cs`, which runs the actual, fully-wired
  `RecursorIngestionService.ProcessBatchAsync` twice with identical session/event inputs — once
  with the flag off, once on with a deterministic fake predictor that returns real (non-null)
  predictions — and asserts the adaptation output (parameter changes, hint mode, difficulty,
  time pressure, reasoning summary, hypothesis labels, resulting session difficulty profile, and
  trajectory streak counters) is byte-for-byte identical, while confirming the shadow predictor
  and its ADX persistence were genuinely invoked in the "on" run.
- The feature flag defaults to `false`; even when enabled, the runtime block only logs and
  ingests to a dedicated new ADX table.
- `MultiSignalGuardrailService` (the only pre-existing source of `TargetBehaviorState`) was not
  modified by this phase.

## Criteria before Phase 10F begins

1. Run the audit KQL against production-representative data and record actual `unknown`/blank
   rates per horizon and per simulation.
2. Train at least one real model version and record the metrics this document currently marks
   as unknown, including the per-simulation breakdown for `medical-supply-stocking`.
3. Accumulate enough live `TemporalBehaviorStatePredictions` rows with resolved targets to
   evaluate coverage and out-of-sample accuracy via the model-comparison/coverage endpoints.
4. Human review of at least one full session timeline in the dashboard confirming predictions
   look plausible against the guardrail state shown alongside them.
5. A guardrail-readiness checklist analogous to Phase 10D-1's (minimum row counts, minimum
   macro F1, live coverage rate) — not defined here, since it depends on the real metrics from
   item 2.

Phase 10F (active or shadow guardrail integration) is explicitly out of scope for this phase
and was not started.
