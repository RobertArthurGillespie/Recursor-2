# Phase 9D — Risk-only Conditional Suppression

## Purpose

Phase 9D activates conditional reliability rules so that intervention families can be suppressed
when the learner's current behavioral state matches a configured "risky" condition key.

Phase 9C was shadow-only. Phase 9D adds a safe activation gate:
- `EnableConditionalSuppression = false` (default) → Phase 9C shadow behavior preserved exactly
- `EnableConditionalSuppression = true` AND `Mode = "active"` → risky conditional matches suppress families

Only families whose matched conditional tier is `"risky"` are suppressed.
`"promising"` and `"neutral"` conditional tiers remain observational regardless of configuration.

---

## Relationship to prior phases

| Phase | What it adds |
|-------|-------------|
| 8E | Runtime: global `FamilyReliabilityTiers` suppression in shadow/active mode |
| 9A | Offline: `GenerateRecommendationsAsync()` — global + segmented ADX recommendations |
| 9B | Offline: export artifact for manual `appsettings` review |
| 9C | Runtime shadow: `ConditionalFamilyReliabilityTiers` — matches logged, nothing removed |
| **9D** | **Runtime: conditional risky matches suppress families when explicitly enabled and active** |

---

## New option

`RecursorPolicyReliabilityOptions` gains one field:

```csharp
public bool EnableConditionalSuppression { get; set; } = false;
```

Default is `false`. Phase 9C behavior is unchanged unless `EnableConditionalSuppression = true`
AND `Mode = "active"`.

---

## Config

### Shadow-only (Phase 9C behavior preserved)

```json
"PolicyReliability": {
  "Mode": "shadow",
  "EnableConditionalSuppression": false,
  "FamilyReliabilityTiers": {},
  "ConditionalFamilyReliabilityTiers": {
    "pace-increase": {
      "GuardrailOverallBehaviorState=struggling": "risky",
      "PredictedConfusionRisk=high": "risky"
    }
  }
}
```

Effect: conditional matches are logged to `Phase8EReliabilityNotes` and written to ADX.
No intervention families or parameter changes are removed.

### Active with conditional suppression enabled

```json
"PolicyReliability": {
  "Mode": "active",
  "EnableConditionalSuppression": true,
  "FamilyReliabilityTiers": {},
  "ConditionalFamilyReliabilityTiers": {
    "pace-increase": {
      "GuardrailOverallBehaviorState=struggling": "risky",
      "PredictedConfusionRisk=high": "risky"
    }
  }
}
```

Effect: when the learner state is `struggling` or confusion risk is `high`, `pace-increase` is
removed from `InterventionFamilies` and its owned `timePressure/increase` parameter change is removed.

---

## Condition key format

Keys use `=` as the separator (not `:`, which is a .NET config section separator).

| Key prefix | Source | Example values |
|-----------|--------|----------------|
| `GuardrailOverallBehaviorState=<value>` | `MultiSignalGuardrailSummary.OverallBehaviorState` | `struggling`, `recovering`, `stable`, `mastering`, `conflicted` |
| `PredictedConfusionRisk=<value>` | `BehaviorStatePrediction.PredictedConfusionRisk` | `none`, `low`, `moderate`, `high` |
| `GuardrailHintDependenceRiskLevel=<value>` | `MultiSignalGuardrailSummary.HintDependenceRiskLevel` | `none`, `low`, `moderate`, `high` |

---

## Runtime flow

### When Mode = "shadow" (regardless of EnableConditionalSuppression)

1. Condition keys built from current learner state.
2. Conditional tiers evaluated against `ConditionalFamilyReliabilityTiers`.
3. Matches and risky candidates recorded in `Phase8EReliabilityNotes`.
4. **No families or parameters removed.**

### When Mode = "active" AND EnableConditionalSuppression = false

1. Global `FamilyReliabilityTiers` risky families removed as Phase 8E.
2. Conditional matches noted as candidates.
3. **No conditional removal** — candidates are recorded for audit only.

### When Mode = "active" AND EnableConditionalSuppression = true

1. Global `FamilyReliabilityTiers` risky families identified.
2. Conditional tiers evaluated. Risky families not already globally risky become conditional candidates.
3. Global suppression applied first (Phase 8E behavior preserved).
4. Conditional suppression applied: conditional candidates removed with their owned parameter changes.
5. Ownership rules used for parameter removal (same as global suppression):
   - `difficulty-increase` → `difficulty/increase`
   - `pace-increase` → `timePressure/increase`
   - `difficulty-reduction` → `difficulty/decrease`
   - `pace-support` → `timePressure/decrease`
   - `hint-fade`, `hint-remove`, `hint-reduction` → `hintMode` only if no hint-support family survives
6. Audit notes written with all suppression details.

---

## Composition with global suppression

Global and conditional suppression are evaluated independently and compose safely.

If a family appears in both `FamilyReliabilityTiers` (globally risky) and
`ConditionalFamilyReliabilityTiers` (conditionally risky for the current state):
- The family is removed exactly once by global suppression.
- The conditional match is still noted in `conditional-matches` for audit.
- The family does NOT appear in `conditional-suppressed-families` (it was globally handled).

This prevents double-removal of families and parameter changes.

---

## Audit notes format

### Shadow mode — conditional risky candidate

```
conditional-matches: pace-increase=risky[GuardrailOverallBehaviorState=struggling] | conditional-risky-suppression-candidates: [pace-increase]
```

### Active mode — conditional suppression applied

```
conditional-matches: pace-increase=risky[GuardrailOverallBehaviorState=struggling] | conditional-risky-suppression-candidates: [pace-increase] | conditional-suppressed-families: [pace-increase] | conditional-suppressed-params: [timePressure/increase] | mode=active
```

### Active mode — global + conditional suppression (different families)

```
risky-families-detected: [difficulty-increase] | suppressed-params: [difficulty/increase] | conditional-matches: pace-increase=risky[PredictedConfusionRisk=high] | conditional-risky-suppression-candidates: [pace-increase] | conditional-suppressed-families: [pace-increase] | conditional-suppressed-params: [timePressure/increase] | mode=active
```

### Active mode — same family globally and conditionally risky (no double-removal)

```
risky-families-detected: [pace-increase] | suppressed-params: [timePressure/increase] | conditional-matches: pace-increase=risky[GuardrailOverallBehaviorState=struggling] | mode=active
```

---

## ADX schema impact

None. `Phase8EReliabilityNotes` (existing string column in `AdaptationDecisions_v2`) carries all
Phase 9D audit information. No new tables or columns are required.

---

## Verification KQL

### Confirm conditional suppression notes are being written

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-suppressed-families"
| project SessionId, Phase8EReliabilityNotes, ingestion_time()
| take 50
```

### Count conditional suppressions per family

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-suppressed-families"
| extend Suppressed = extract(@"conditional-suppressed-families: \[([^\]]+)\]", 1, Phase8EReliabilityNotes)
| mv-expand todynamic(split(Suppressed, ", ")) to typeof(string)
| summarize Count = count() by Family = tostring(Column1)
| order by Count desc
```

### Confirm shadow candidates are observed but not removed

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-risky-suppression-candidates"
| where not(Phase8EReliabilityNotes contains "conditional-suppressed-families")
| project SessionId, Phase8EReliabilityNotes, ingestion_time()
| take 50
```

### Confirm global and conditional suppression do not overlap on the same family

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "risky-families-detected"
| where Phase8EReliabilityNotes contains "conditional-suppressed-families"
| extend GlobalFamilies = extract(@"risky-families-detected: \[([^\]]+)\]", 1, Phase8EReliabilityNotes)
| extend CondFamilies   = extract(@"conditional-suppressed-families: \[([^\]]+)\]", 1, Phase8EReliabilityNotes)
// Review manually — families in GlobalFamilies should not appear in CondFamilies.
| project SessionId, GlobalFamilies, CondFamilies
| take 50
```

### Confirm promising tiers never suppress

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "=promising"
| where Phase8EReliabilityNotes contains "conditional-suppressed-families"
// This should return 0 rows.
| count
```

---

## Manual test plan

### Shadow candidate observation

1. Set `Mode = "shadow"`, `EnableConditionalSuppression = false`.
2. Configure a `ConditionalFamilyReliabilityTiers` entry with `"risky"` for a state like `struggling`.
3. Run a session that produces a `struggling` overall behavior state.
4. Query ADX:
   ```kql
   AdaptationDecisions_v2
   | where Phase8EReliabilityNotes contains "conditional-risky-suppression-candidates"
   | take 5
   ```
5. Verify:
   - `Phase8EReliabilityNotes` contains `conditional-risky-suppression-candidates: [<family>]`.
   - The family is still present in the adaptation record (not removed in shadow mode).

### Conditional suppression disabled in active mode

1. Set `Mode = "active"`, `EnableConditionalSuppression = false`.
2. Keep the same conditional config.
3. Run a matching session.
4. Verify the family is NOT removed (flag is false).
5. Verify `Phase8EReliabilityNotes` contains `conditional-risky-suppression-candidates` but not
   `conditional-suppressed-families`.

### Conditional suppression enabled in active mode

1. Set `Mode = "active"`, `EnableConditionalSuppression = true`.
2. Keep the same conditional config.
3. Run a matching session.
4. Verify:
   - The family is removed from `InterventionFamilies`.
   - Its owned parameter change is removed.
   - `Phase8EReliabilityNotes` contains `conditional-suppressed-families`, `conditional-suppressed-params`,
     and `mode=active`.

### Global + conditional composition

1. Configure `FamilyReliabilityTiers: { "difficulty-increase": "risky" }` AND
   `ConditionalFamilyReliabilityTiers: { "pace-increase": { "GuardrailOverallBehaviorState=struggling": "risky" } }`.
2. Run a session that produces both families in an adaptation with `struggling` state.
3. Verify:
   - `difficulty-increase` removed by global suppression.
   - `pace-increase` removed by conditional suppression.
   - Notes contain both suppression entries with no overlap.
4. Now configure both `FamilyReliabilityTiers` and `ConditionalFamilyReliabilityTiers` to include
   `pace-increase` as risky.
5. Run a matching session.
6. Verify `pace-increase` appears in `risky-families-detected` but NOT in `conditional-suppressed-families`
   (global suppression takes precedence; no double-removal).

---

## What Phase 9D does NOT do

- Does not boost or promote families whose conditional tier is "promising".
- Does not act on "neutral" or "insufficient-data" conditional tiers.
- Does not create new adaptations.
- Does not change the Unity/client response shape.
- Does not add new ADX tables or columns.
- Does not change Phase 8E global suppression behavior.
