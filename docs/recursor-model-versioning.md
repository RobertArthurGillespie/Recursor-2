# Recursor model versioning and operations

This document is the single shared reference for how trained model artifacts are published,
resolved at runtime, and activated — for all three temporal model families that use the
immutable-version-directory scheme: `temporal-risk` (Phase 10C-2), `temporal-elevated-risk`
(Phase 10D-1/10D-4), and `temporal-behavior-state` (Phase 10E, shadow-only). It replaces the
per-family flat-filename descriptions that used to live in each phase's own doc — those have
been corrected to point here rather than duplicate (and inevitably drift from) this description.

It is referenced by `Server/Recursor/ML/ModelVersionPublisher.cs`'s own doc comment, which
previously pointed at this file before it existed — a corrective-pass fix in itself.

## Why this exists (the bug this document's accuracy protects against)

Before this corrective pass, `ModelVersionPublisher` (the publish side) wrote immutable version
directories, but the runtime resolver used by `Program.cs` to load models at startup computed an
unrelated flat filename that the publisher never wrote to. A freshly trained, successfully
published model version was therefore silently unreachable by the running app — the operator
would follow the documented "train → update config → restart" workflow and the model would
simply never load, with only a startup warning (not an error) to notice by. Both sides now agree
on the same artifact contract described below, and a real integration test
(`Stage3ModelPublisherResolverIntegrationTests.cs`) trains, publishes, resolves, loads, and
infers through the exact same code paths `Program.cs` uses, specifically so this class of bug
cannot silently reappear.

## Canonical artifact contract

```
{ModelRoot}/
  versions/
    {ModelFamily}/
      {ModelVersion}/
        h1.zip
        h2.zip
        h3.zip
        manifest.json
```

- **`{ModelRoot}`** — `{ContentRootPath}/Recursor/TrainingModels` (the ASP.NET Core content
  root; there is no separate `ModelRoot` configuration key today — it is implicit in
  `ModelVersionPublisher.GetVersionsRoot`).
- **`{ModelFamily}`** — a hardcoded constant per trainer, not currently exposed through
  `IConfiguration`:
  - `TemporalRiskModelTrainingService.ModelFamily = "temporal-risk"`
  - `TemporalElevatedRiskModelTrainingService.ModelFamily = "temporal-elevated-risk"`
  - `TemporalBehaviorStateModelTrainingService.ModelFamily = "temporal-behavior-state"`
- **`{ModelVersion}`** — a free-text label (letters, numbers, dash, underscore, dot only,
  enforced by `SanitizeModelVersion`) supplied to the training endpoint, e.g.
  `temporal-behavior-state-v2`. Configured per family via
  `Recursor:Models:{Family}ModelVersion` in `appsettings.json`.
- **`h1.zip` / `h2.zip` / `h3.zip`** — the trained ML.NET model for horizons 1/2/3.
- **`manifest.json`** — written by `ModelVersionPublisher`/`ModelVersionManifest`, described
  below.

Example (Phase 10E):
```
Recursor/TrainingModels/
  versions/
    temporal-behavior-state/
      temporal-behavior-state-v2/
        h1.zip
        h2.zip
        h3.zip
        manifest.json
```

## Publishing (`ModelVersionPublisher`)

Each trainer's `TrainAsync` calls `ModelVersionPublisher.TryBeginPublish(contentRoot, family,
version)`, which:

1. Fails immediately (no ADX query, no training work) if a manifest already exists for that
   `(family, version)` — **a published version is immutable and never retrained/overwritten**;
   choose a new version label to train again.
2. Stages each horizon's `.zip` into a temporary `{finalDirectory}.staging-{guid}` directory as
   it trains, and load-verifies it (`ModelVersionPublishSession.TryVerifyLoadable`) before it is
   eligible to publish.
3. Only if **all three horizons** train, save, and load-verify successfully does
   `Complete(manifest)` run — this writes `manifest.json` into the staging directory and then
   performs a single atomic `Directory.Move` from the staging directory to the final version
   directory. **All-or-nothing**: a version is never left partially activated with only some
   horizons present.

Publishing a version **never** modifies any other version's directory and never touches a
legacy flat-file path (see "Legacy/explicit-override mode" below) — activating a newly
published version is a separate, explicit configuration step (see "Activation" below).

## Runtime resolution (`ResolveVersionedModelPath`)

Each trainer exposes a `public static string ResolveVersionedModelPath(int horizon, string
modelVersion, string contentRootPath, string? explicitConfigPath)`, called from `Program.cs` at
startup for both temporal-elevated-risk and temporal-behavior-state (temporal-risk's runtime
wiring only ever uses explicit configured paths — see "Legacy/explicit-override mode"):

- **If `explicitConfigPath` is set** (from `Recursor:Models:{Family}H{n}ModelPath`), it is
  **always honored verbatim** (resolved against the content root if relative) — no basename or
  filename-suffix matching is performed. (A prior version of this resolver silently discarded an
  explicit path whose filename didn't match an expected flat-file naming convention and fell
  back to an auto-generated path instead — that silent-discard behavior was itself a bug, fixed
  in this corrective pass.)
- **Otherwise**, the path is derived from `ModelVersionPublisher.GetVersionDirectory(contentRoot,
  family, version)` + `h{horizon}.zip` — the exact directory the publisher writes to. This is
  the canonical, recommended mode: set only `{Family}ModelVersion`, and the H1/H2/H3 paths never
  need to be configured at all.

## Manifest validation (`ModelVersionManifestValidator`)

Before a resolved version is handed to the prediction service, `Program.cs` calls
`ValidateResolvedModelVersion`, which:

- **Canonical mode** (no explicit override configured): calls
  `ModelVersionManifestValidator.ValidateVersionDirectory(contentRoot, family, version)`.
  - If the version directory does not exist at all, this is a no-op — "nothing trained/published
    yet for this version" is a normal, tolerated state (the prediction service simply has no
    model loaded for that horizon, same as always).
  - If the directory **does** exist, its `manifest.json` must be present and consistent:
    `ModelFamily`/`ModelVersion` match what was requested, `CompletionStatus == "Complete"`, all
    three horizons are listed, each horizon's `FileName` is `h{n}.zip` (never a name implying a
    different version), and each referenced artifact file actually exists in that same
    directory. **Any inconsistency throws `InvalidOperationException` at startup** — a
    corrupted or partially-published-looking directory must fail loudly, not load silently or
    fall back to some other version.
- **Explicit-override mode**: calls `ValidateExplicitOverridePath` per configured horizon. If the
  override path doesn't exist yet, that's tolerated (model not deployed yet). If it exists and a
  `manifest.json` sits alongside it, that manifest's family/version must match what's
  configured — this catches an override that accidentally points into a different version's
  published directory. If no `manifest.json` is present next to the override, that's a true
  legacy flat file with nothing to cross-check, and is tolerated silently.

**No silent version fallback ever occurs** — a requested version that fails validation is
reported (thrown, in canonical mode) rather than the app quietly loading a different version.

## Legacy/explicit-override mode

`Recursor:Models:{Family}H1ModelPath` / `H2ModelPath` / `H3ModelPath` remain supported as
explicit per-horizon path overrides — this is the only mode `temporal-risk`'s runtime wiring
uses today (it never derives a version-directory path automatically). Use this mode only when
you have a specific reason to point at a model file outside the standard `versions/` layout
(e.g. a manually placed file, or a path outside `ModelRoot` entirely). Prefer the canonical
version-directory mode (configure only `{Family}ModelVersion`) for anything trained via the
`POST /api/recursor/train-*` endpoints, since that is what they actually publish to.

## Deterministic prediction identity (Phase 10E specific)

`TemporalBehaviorStatePredictions` additionally carries a `PredictionId` column — a deterministic
hash of `SessionId+WindowIndex+Horizon+ModelVersion`
(`BehaviorStatePredictionKeys.ComputePredictionId`). The same logical prediction always produces
the same ID regardless of which process/instance/replay computes it, which is what lets ADX
queries deduplicate defensively across app restarts and multiple App Service instances — the
in-memory `CompletedBehaviorStatePredictionKeys` guard on `SessionDocument` is process-local and
does not survive either. Physical duplicate rows are tolerated (ADX is append-only); only the
logical prediction identity is authoritative. This is specific to the behavior-state predictions
table; the temporal-risk and elevated-risk prediction tables use their existing composite-key
`arg_max` dedup, which does not have the same restart/idempotency requirement Phase 10E's
partial-per-horizon retry design introduced.

**Legacy blank `PredictionId` (Stage 6/9 corrective pass):** `PredictionId` was added to an
already-populated table via an additive `.create-merge`, so rows ingested before that migration
have `PredictionId == ""` and can never retroactively get a real one without a destructive
backfill (not performed). Every dashboard/evaluation query therefore dedups by a
`LogicalPredictionId` (`AdxDashboardQueryService.BehaviorStatePredictionDedupKql`), which equals
`PredictionId` when it is present and falls back to the same composite identity
`ComputePredictionId` hashes (`legacy|SessionId|WindowIndex|Horizon|ModelVersion`) when it is
blank — so pre-migration rows still dedup per (session, window, horizon, version) instead of
every blank-ID row collapsing into one logical prediction. `PredictionId` remains canonical for
all rows created after this migration; no backfill is required now, though one could be added
later to retire the compatibility path.

## Activation procedure

1. **Choose a new immutable version label** for the family you're training (e.g.
   `temporal-behavior-state-v2`). It must not already exist for that family — training will be
   rejected if it does.
2. **Train**: `POST /api/recursor/train-{family}?modelVersion={version}` (admin-authorized,
   environment-gated — see `Recursor:Policies:EnableModelTraining` /
   `ModelTrainingAllowedEnvironments`).
3. **Publish**: happens automatically as part of the same training call, only if all three
   horizons trained/saved/load-verified successfully.
4. **Validate the manifest**: inspect `versions/{family}/{version}/manifest.json` — confirm
   `CompletionStatus == "Complete"`, all three horizons present, and (Phase 10E) review
   `CrossSimulationValidationStatus`/`ClassesAbsentFromValidation`/`SimulationsAbsentFromTraining`
   before treating the version as representative of more than one simulation.
5. **Review metrics** in the training report / manifest (`ValidationMicroAccuracy`,
   `ValidationMacroF1`, per-simulation breakdown) — do not activate a version whose metrics you
   have not looked at.
6. **Update the configured version**: set `Recursor:Models:{Family}ModelVersion` to the new
   version label (no per-horizon path keys needed in canonical mode).
7. **Restart/redeploy** — models load once at startup; changing the configured version has no
   effect on an already-running process.
8. **Verify the runtime loaded the requested version**: check startup logs for
   `[TemporalXPredictionService] Model status — H1=... H2=... H3=... Version=...` and confirm
   `Version` matches what you configured, and that `H1`/`H2`/`H3` all report `True`. If manifest
   validation failed, the app will have thrown at startup instead — check the exception message,
   which names the specific inconsistency.
9. **Generate test sessions** through the normal ingestion flow (or, for Phase 10E specifically,
   confirm `Recursor:Policies:EnableTemporalBehaviorStatePrediction` is `true` in the
   environment you're testing in — it defaults to `false`).
10. **Verify ADX prediction rows**: run the relevant table's verification query (e.g.
    `docs/kql/phase10e-temporal-behavior-state-predictions-setup.kql`'s verification query for
    Phase 10E) and confirm rows are arriving with the expected `ModelVersion`.

## Rollback

Point `{Family}ModelVersion` back at the previous version's label and restart/redeploy. No
files need to be deleted, moved, or overwritten — every published version's artifacts coexist
permanently under `versions/{family}/`, since publishing is additive and per-version-immutable.

## Manual cleanup (not automated)

There is no automated deletion or garbage collection of old version directories — this pass does
not add one, and none should be added without explicit instruction (destructive artifact
deletion is out of scope for a corrective pass). If disk usage becomes a concern, removing an old
`versions/{family}/{version}/` directory is safe as long as no running process is configured to
use that version.
