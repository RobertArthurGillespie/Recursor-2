# Recursor Engine — KQL Scripts

KQL scripts for setting up and managing the Recursor Engine ADX database.

## Files

| File | Purpose |
|---|---|
| `recursor-adx-setup.kql` | Creates the five Recursor telemetry tables |

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
5. Run each `.create table` block individually using **Shift+Enter** or the **Run** button

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

## Tables created

| Table | Description |
|---|---|
| `RawEvents` | One row per event in each batch received from the sim |
| `FeatureWindows` | One row per feature window derived from a batch |
| `BehaviorProfiles` | One row per behavior profile built from a feature window |
| `HypothesisSets` | One row per hypothesis set derived from a behavior profile |
| `AdaptationDecisions` | One row per adaptation decision produced by the policy layer |

## Re-running safely

The `.create table` command will fail if the table already exists. To recreate a table cleanly:

```kql
.drop table RawEvents ifexists
.create table RawEvents ( ... )
```

Or to add the table only if it does not yet exist:

```kql
.create table RawEvents ifnotexists ( ... )
```

## What is NOT included here

- **Ingestion mappings** — not needed for the basic slice; the Kusto SDK maps columns by name
- **Update policies** — not used in the basic slice
- **Retention policies** — use ADX cluster defaults until explicitly configured
- **Row-level security** — out of scope for the basic slice
