# Recursor Engine — KQL Scripts

KQL scripts for setting up and managing the Recursor Engine ADX database.

## Files

| File | Purpose |
|---|---|
| `recursor-adx-setup.kql` | Creates all Recursor telemetry tables and their named CSV ingestion mappings; includes migration alter-merge statements for existing databases |
| `phase8c-policy-effectiveness-analytics.kql` | Phase 8C read-only analytics: 15 KQL queries evaluating intervention family effectiveness against `AdaptationEffectiveness` and `BehaviorStateTrainingRows` |
| `phase8d-policy-reliability-scoring.kql` | Phase 8D read-only reliability scoring: stored function `PolicyFamilyReliability()` + 14 queries that classify families into tiers ("promising", "risky", "neutral", "insufficient-data") and produce a `Phase8ERecommendation` per family |

## Running the setup script

### Prerequisites

- An ADX cluster provisioned in Azure
- A database named `RecursorDb` (or the value set in `Adx:Database`)
- Database Admin or Table Admin role on that database

### Option 1 — ADX Web UI

1. Open the [Azure Data Explorer Web UI](https://dataexplorer.azure.com)
2. Connect to your cluster
3. Select the `RecursorDb` database
4. Open `recursor-adx-setup.kql` and paste its contents into the query pane
5. Run each `.create table` block and each `.create-or-alter ... mapping` block individually using **Shift+Enter** or the **Run** button

### Option 2 — Kusto CLI

```bash
kusto -cluster "https://<cluster>.<region>.kusto.windows.net" \
      -database "RecursorDb" \
      -script "docs/kql/recursor-adx-setup.kql"
```

### Option 3 — Azure CLI

```bash
az kusto script create \
  --cluster-name <cluster> \
  --database-name RecursorDb \
  --resource-group <rg> \
  --name recursor-setup \
  --script-content "$(cat docs/kql/recursor-adx-setup.kql)"
```

## Fresh install vs. upgrade

| Scenario | What to run |
|---|---|
| **Fresh install** | Run all `.create table` blocks, then all `.create-or-alter … mapping` blocks. Skip the `.alter-merge` blocks. |
| **Upgrading an existing database** | Run only the `.alter-merge` blocks that correspond to phases added after your initial setup. Then re-run the relevant `.create-or-alter … mapping` blocks to update stale mappings. |

## Tables

| Table | Columns | Description |
|---|---|---|
| `RawEvents` | 17 | One row per event in each batch received from the sim |
| `FeatureWindows` | 12 | One row per feature window derived from a batch |
| `BehaviorProfiles` | 8 | One row per behavior profile built from a feature window |
| `HypothesisSets` | 8 | One row per hypothesis set derived from a behavior profile |
| `AdaptationDecisions_v2` | 15 | One row per adaptation decision produced by the policy layer |
| `BehaviorStateTrainingRows` | 53 | One row per feature window; shadow ML labels, predictions, and guardrail summary |
| `UserBehaviorProfiles` | 27 | Append-only per-user profile snapshot; query with `arg_max(UpdatedAtUtc, *)` |
| `UserBehaviorProfileUpdates` | 15 | Per-window behavioral observation for profile history and audit |
| `AdaptationEffectiveness` | 19 | Phase 8B observability; one row per evaluated window pair after an adaptation fires |

## CSV ingestion mappings

Every table has a named CSV mapping referenced by `AdxIngestionService`.
Column ordinals in each mapping match the exact column-add order in the
corresponding `Build*Table` method in `AdxIngestionService.cs`.

| Mapping | Table | Columns |
|---|---|---|
| `RawEventsCsvMapping` | `RawEvents` | 17 |
| `FeatureWindowsCsvMapping` | `FeatureWindows` | 12 |
| `HypothesisSetsCsvMapping` | `HypothesisSets` | 8 |
| `AdaptationDecisionsV2CsvMapping` | `AdaptationDecisions_v2` | 15 |
| `BehaviorProfilesCsvMapping` | `BehaviorProfiles` | 8 |
| `BehaviorStateTrainingRowsCsvMapping` | `BehaviorStateTrainingRows` | 53 |
| `UserBehaviorProfilesCsvMapping` | `UserBehaviorProfiles` | 27 |
| `UserBehaviorProfileUpdatesCsvMapping` | `UserBehaviorProfileUpdates` | 15 |
| `AdaptationEffectivenessCsvMapping` | `AdaptationEffectiveness` | 19 |

## Re-running safely

The `.create table` command fails if the table already exists. Options:

```kql
// Create only if absent (fresh install guard):
.create table RawEvents ifnotexists ( ... )

// Drop and recreate (destroys all data — use with caution):
.drop table RawEvents ifexists
.create table RawEvents ( ... )
```

To update a stale ingestion mapping without touching table data:

```kql
.create-or-alter table RawEvents ingestion csv mapping 'RawEventsCsvMapping' '[ ... ]'
```

## What is NOT included

- **Update policies** — not used in the current slice
- **Retention policies** — use ADX cluster defaults until explicitly configured
- **Row-level security** — out of scope for the current slice
