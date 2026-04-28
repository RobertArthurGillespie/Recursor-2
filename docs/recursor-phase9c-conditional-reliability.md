# Phase 9C — State-Conditional Policy Reliability

## Purpose

Phase 9C extends Phase 8E's global reliability weighting with state-conditional evaluation.
Instead of a single reliability tier per intervention family, families can have different tiers
depending on the learner's current behavioral state (confusion risk, overall behavior state,
hint-dependence risk).

Phase 9C is shadow-first. Conditional matches populate audit notes and logs only. No adaptation
changes result from conditional rules until they are explicitly activated in a future phase.

---

## Relationship to prior phases

| Phase | What it adds |
|-------|-------------|
| 8D | Offline KQL: PolicyFamilyReliability() → global + segmented reliability tiers |
| 8E | Runtime: global FamilyReliabilityTiers applied in shadow/active mode |
| 9A | Offline: GenerateRecommendationsAsync() → global + segmented ADX recommendations |
| 9B | Offline: export artifact (export-config, export-config-text) for manual appsettings review |
| **9C** | **Runtime shadow observation: ConditionalFamilyReliabilityTiers; offline: conditional export** |

---

## Config structure

Phase 9C adds one new option to `Recursor:PolicyReliability` in appsettings:

```json
{
  "Recursor": {
    "PolicyReliability": {
      "Mode": "shadow",
      "FamilyReliabilityTiers": {
        "pace-increase": "risky"
      },
      "ConditionalFamilyReliabilityTiers": {
        "difficulty-reduction": {
          "GuardrailOverallBehaviorState:struggling": "promising",
          "GuardrailOverallBehaviorState:conflicted": "risky"
        },
        "pace-increase": {
          "PredictedConfusionRisk:high": "risky",
          "PredictedConfusionRisk:none": "neutral"
        }
      }
    }
  }
}
```

`FamilyReliabilityTiers` and `ConditionalFamilyReliabilityTiers` are independent.
A family can appear in one, both, or neither.

---

## Condition key format

Condition keys are built at runtime from the current learner state snapshot.
Three dimensions are supported:

| Condition key prefix | Source field | Example values |
|----------------------|-------------|----------------|
| `GuardrailOverallBehaviorState:<value>` | `MultiSignalGuardrailSummary.OverallBehaviorState` | `struggling`, `recovering`, `stable`, `mastering`, `conflicted`, `unknown` |
| `PredictedConfusionRisk:<value>` | `BehaviorStatePrediction.PredictedConfusionRisk` | `none`, `low`, `moderate`, `high` |
| `GuardrailHintDependenceRiskLevel:<value>` | `MultiSignalGuardrailSummary.HintDependenceRiskLevel` | `none`, `low`, `moderate`, `high` |

If `guardrailSummary` or `shadowPrediction` is null (e.g., the model did not fire),
the corresponding keys are omitted and no conditional match is attempted.

---

## Runtime behavior (shadow mode)

When `Mode = "shadow"`:

1. The ingestion pipeline calls `PolicyReliabilityWeightingService.Apply(decision, guardrailSummary, shadowPrediction)`.
2. Active condition keys are built from the current learner state.
3. Each family in the adaptation's `InterventionFamilies` is checked against `ConditionalFamilyReliabilityTiers`.
4. For each matching key, a note is appended to `Phase8EReliabilityNotes` and a log entry is written.
5. **No intervention families or parameter changes are removed due to conditional matches.**
6. Global risky suppression from `FamilyReliabilityTiers` still applies exactly as before.

Example `Phase8EReliabilityNotes` when a conditional match occurs alongside a global risky detection:

```
risky-families-detected: [pace-increase] | mode=shadow; no changes applied | conditional-matches: difficulty-reduction=promising[GuardrailOverallBehaviorState:struggling], pace-increase=risky[PredictedConfusionRisk:high]
```

When only conditional matches occur (no global risky families):

```
conditional-matches: difficulty-reduction=promising[GuardrailOverallBehaviorState:struggling]
```

---

## Export endpoints (Phase 9B + 9C)

### GET /api/recursor/policy-recommendations/export-config

Returns a `PolicyReliabilityConfigExport` as indented JSON.

Fields:
- `RecommendedMode` — always `"shadow"`
- `FamilyReliabilityTiers` — global promising/risky families (omits neutral and insufficient-data)
- `ConditionalFamilyReliabilityTiers` — segmented promising/risky entries (omits neutral and insufficient-data)
- `Metadata` — counts, source, generation time, review-required list

### GET /api/recursor/policy-recommendations/export-config-text

Returns a human-readable text report including:
- Paste-ready JSON snippet for `Recursor:PolicyReliability`
- Per-family conditional tier breakdown
- Guidance on shadow-only vs active behavior
- How-to-apply steps

---

## Global vs conditional tiers — precedence

Phase 9C does **not** change the precedence of global vs conditional tiers in shadow mode.
Both are observational. In a future active phase:
- Global `FamilyReliabilityTiers` with `Mode = "active"` suppresses families unconditionally.
- Conditional tiers would suppress families only when the matching condition key is active.
- The two mechanisms would be evaluated independently.

---

## Safe manual review workflow

1. Run `GET /api/recursor/policy-recommendations/export-config-text`.
2. Read the generated snippet carefully:
   - Global `FamilyReliabilityTiers`: families reliably risky or promising across all learner states.
   - `ConditionalFamilyReliabilityTiers`: families whose reliability varies by learner state.
3. Paste the snippet under `"Recursor" → "PolicyReliability"` in `appsettings.json`.
4. Keep `Mode = "shadow"`.
5. Redeploy.
6. After 1–2 sessions, query ADX:
   ```kql
   AdaptationDecisions_v2
   | where Phase8EReliabilityNotes contains "conditional-matches"
   | project SessionId, Phase8EReliabilityNotes, ingestion_time()
   | take 50
   ```
7. Verify conditional matches appear correctly and no parameter changes were removed.
8. Do **not** change `Mode` to `"active"` until global tiers have been validated independently.

---

## Verification KQL

### Confirm conditional match notes are being written

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-matches"
| extend ConditionalPart = extract(@"conditional-matches: (.+)$", 1, Phase8EReliabilityNotes)
| project SessionId, Phase8EReliabilityNotes, ConditionalPart, ingestion_time()
| take 50
```

### Count conditional matches per family

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-matches"
| extend Matches = extract_all(@"(\w[\w-]*)=(\w+)\[([^\]]+)\]", Phase8EReliabilityNotes)
| mv-expand Matches
| extend Family = tostring(Matches[0]), Tier = tostring(Matches[1]), CondKey = tostring(Matches[2])
| summarize Count = count() by Family, Tier, CondKey
| order by Count desc
```

### Confirm no parameter changes removed by conditional rules in shadow mode

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-matches"
| where not(Phase8EReliabilityNotes contains "mode=active")
| summarize Count = count()
// All rows should have parameter changes present — none suppressed by conditional rules alone.
```

### Confirm global risky suppression still works when both are configured

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "risky-families-detected"
| where Phase8EReliabilityMode == "shadow"
| project SessionId, Phase8EReliabilityNotes
| take 20
```

---

## Manual test plan

1. Add `ConditionalFamilyReliabilityTiers` only (no `FamilyReliabilityTiers`) with `Mode = "shadow"`.
2. Run a session that produces a `struggling` overall behavior state.
3. Observe `Phase8EReliabilityNotes` in ADX — expect `conditional-matches: ...` present.
4. Verify `InterventionFamilies` and `ParameterChanges` on the same record are unchanged.
5. Add `FamilyReliabilityTiers: { "pace-increase": "risky" }` alongside the conditional config.
6. Run a session with `pace-increase` in the adaptation.
7. Observe `Phase8EReliabilityNotes` — expect both `risky-families-detected` and `conditional-matches`.
8. Verify `pace-increase` is **not** removed in shadow mode.
9. Change `Mode` to `"active"` and re-run.
10. Verify `pace-increase` **is** removed from `InterventionFamilies` (global active suppression).
11. Verify `difficulty-reduction` (conditional match only) is **not** removed.
12. Verify `Phase8EReliabilityNotes` contains both global suppression and conditional notes.

---

## ADX schema impact

None. `Phase8EReliabilityNotes` (existing string column in `AdaptationDecisions_v2`) is reused
to carry conditional match observations. No new ADX table or column is required.
