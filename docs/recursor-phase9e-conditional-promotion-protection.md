# Phase 9E — Controlled Conditional Promotion Protection

## Purpose

Phase 9E allows conditional "promising" reliability tiers to **protect existing adaptation families
from global reliability suppression**, but only under strict constraints. It is a preservation
mechanism, not a promotion or generation mechanism.

Phase 9E never:
- Creates new intervention families
- Adds new parameter changes
- Boosts or intensifies existing values
- Overrides Phase 8A guardrail vetoes
- Protects families from conditional risky suppression (Phase 9D)
- Forces difficulty increases, pace increases, or hint reductions

---

## Relationship to prior phases

| Phase | What it adds |
|-------|-------------|
| 8E | Runtime: global `FamilyReliabilityTiers` suppression in shadow/active mode |
| 9A | Offline: `GenerateRecommendationsAsync()` — global + segmented ADX recommendations |
| 9B | Offline: export artifact for manual `appsettings` review |
| 9C | Runtime shadow: `ConditionalFamilyReliabilityTiers` — matches logged, nothing removed |
| 9D | Runtime: conditional risky matches suppress families when explicitly enabled and active |
| **9E** | **Runtime: conditional promising matches protect globally-risky families from global suppression** |

---

## New option

`RecursorPolicyReliabilityOptions` gains one field:

```csharp
public bool EnableConditionalPromotionProtection { get; set; } = false;
```

Default is `false`. Protection is entirely inert unless `EnableConditionalPromotionProtection = true`
AND `Mode = "active"`.

---

## Config example

```json
"PolicyReliability": {
  "Mode": "active",
  "EnableConditionalSuppression": true,
  "EnableConditionalPromotionProtection": true,
  "FamilyReliabilityTiers": {
    "difficulty-reduction": "risky"
  },
  "ConditionalFamilyReliabilityTiers": {
    "difficulty-reduction": {
      "GuardrailOverallBehaviorState=struggling": "promising"
    }
  }
}
```

Expected behavior:
- When learner state is `struggling`: `difficulty-reduction` is preserved (protected from global risky suppression).
- When learner state is NOT `struggling`: `difficulty-reduction` is suppressed as globally risky.
- If a conditional risky rule also matches: `difficulty-reduction` is suppressed (risky wins).

---

## "Risky wins" rule

When a family has **both** a matching conditional promising tier and a matching conditional risky tier,
the risky tier always wins and the family is suppressed. This applies regardless of
`EnableConditionalPromotionProtection`.

```json
"ConditionalFamilyReliabilityTiers": {
  "difficulty-reduction": {
    "GuardrailOverallBehaviorState=struggling": "promising",
    "PredictedConfusionRisk=high": "risky"
  }
}
```

If learner is struggling AND confusion is high: `difficulty-reduction` is suppressed.
The audit note will include `risky-overrides-promising: [difficulty-reduction]`.

---

## Phase 8A guardrails are not overridden

Phase 9E operates before Phase 8A in the pipeline (Phase 8E/9D/9E runs on the adaptation decision;
Phase 8A runs as a post-policy safety modifier before the decision is produced). Phase 9E cannot
reintroduce a family that Phase 8A already vetoed.

---

## Runtime flow

### When Mode = "shadow" (regardless of EnableConditionalPromotionProtection)

1. Global risky families and conditional tiers evaluated.
2. Globally-risky families that have a conditional promising match are recorded in notes as
   `conditional-promising-observed`.
3. **No families removed. No families protected.**

### When Mode = "active" AND EnableConditionalPromotionProtection = false

1. Global risky suppression applied as Phase 8E.
2. Conditional risky suppression applied as Phase 9D (if `EnableConditionalSuppression = true`).
3. Globally-risky families with a conditional promising match are recorded as
   `conditional-promising-observed` for operator awareness.
4. **No protection applied.**

### When Mode = "active" AND EnableConditionalPromotionProtection = true

1. Global risky families identified.
2. Conditional tiers evaluated (Phase 9C/9D logic).
3. Protection candidates identified: families that are globally risky AND have a matching
   conditional promising tier.
4. Risky-overrides check: candidates that also have a matching conditional risky tier are
   excluded from protection (risky wins).
5. Remaining candidates become **protected families** — removed from the global suppression set.
6. Global suppression applied to the effective set (protected families excluded).
7. Conditional risky suppression applied normally (Phase 9D — unaffected by protection).
8. Audit notes written.

---

## What protection is and is not

| Protection applies to | Does NOT apply to |
|----------------------|-------------------|
| Global reliability suppression (`FamilyReliabilityTiers: risky`) | Phase 8A guardrail vetoes |
| | Conditional risky suppression (Phase 9D) |
| | Hard safety rules |
| | Families not already in the adaptation |

Protection only preserves — it never generates.

---

## Audit notes format

### Shadow mode — global risky with conditional promising match observed

```
risky-families-detected: [difficulty-reduction] | mode=shadow; no changes applied | conditional-matches: difficulty-reduction=promising[GuardrailOverallBehaviorState=struggling] | conditional-promising-observed: [difficulty-reduction]
```

### Active mode + protection disabled — observed, family still suppressed

```
risky-families-detected: [difficulty-reduction] | suppressed-params: [difficulty/decrease] | conditional-matches: difficulty-reduction=promising[GuardrailOverallBehaviorState=struggling] | conditional-promising-observed: [difficulty-reduction] | mode=active
```

### Active mode + protection enabled — family preserved

```
risky-families-detected: [difficulty-reduction] | conditional-matches: difficulty-reduction=promising[GuardrailOverallBehaviorState=struggling] | conditional-promising-protection-candidates: [difficulty-reduction] | conditional-promising-protected-families: [difficulty-reduction] | mode=active
```

Note: `suppressed-params` is absent because no params were removed.

### Active mode + protection enabled — risky wins (both conditions match)

```
risky-families-detected: [difficulty-reduction] | suppressed-params: [difficulty/decrease] | conditional-matches: difficulty-reduction=promising[...], difficulty-reduction=risky[...] | conditional-promising-protection-candidates: [difficulty-reduction] | risky-overrides-promising: [difficulty-reduction] | mode=active
```

### Active mode + protection enabled — partial: one protected, one suppressed

```
risky-families-detected: [difficulty-reduction, pace-increase] | suppressed-params: [timePressure/increase] | conditional-matches: difficulty-reduction=promising[...] | conditional-promising-protection-candidates: [difficulty-reduction] | conditional-promising-protected-families: [difficulty-reduction] | mode=active
```

---

## ADX schema impact

None. `Phase8EReliabilityNotes` (existing string column in `AdaptationDecisions_v2`) carries all
Phase 9E audit information. No new tables or columns are required.

---

## Verification KQL

### Confirm protection is being applied

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-promising-protected-families"
| project SessionId, Phase8EReliabilityNotes, ingestion_time()
| take 50
```

### Count protected families by family name

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-promising-protected-families"
| extend Protected = extract(@"conditional-promising-protected-families: \[([^\]]+)\]", 1, Phase8EReliabilityNotes)
| mv-expand todynamic(split(Protected, ", ")) to typeof(string)
| summarize Count = count() by Family = tostring(Column1)
| order by Count desc
```

### Confirm risky-overrides-promising is working

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "risky-overrides-promising"
| project SessionId, Phase8EReliabilityNotes, ingestion_time()
| take 50
```

### Confirm protection never creates new families

```kql
// This should return 0 rows: a protected family should always appear in risky-families-detected
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-promising-protected-families"
| where not(Phase8EReliabilityNotes contains "risky-families-detected")
| count
```

### Confirm shadow and flag=false never protect

```kql
AdaptationDecisions_v2
| where Phase8EReliabilityNotes contains "conditional-promising-observed"
| where Phase8EReliabilityNotes contains "conditional-promising-protected-families"
// This should return 0 rows: observed notes and protected notes should not coexist.
| count
```

---

## Manual test plan

### Shadow observation

1. Set `Mode = "shadow"`, `EnableConditionalPromotionProtection = true`.
2. Configure a globally-risky family with a conditional promising tier for a state like `struggling`.
3. Run a session that produces a `struggling` overall behavior state.
4. Query ADX:
   ```kql
   AdaptationDecisions_v2
   | where Phase8EReliabilityNotes contains "conditional-promising-observed"
   | take 5
   ```
5. Verify:
   - `Phase8EReliabilityNotes` contains `conditional-promising-observed: [<family>]`.
   - The family is still present in the adaptation record.
   - No `conditional-promising-protected-families` appears.

### Active mode — protection disabled

1. Set `Mode = "active"`, `EnableConditionalPromotionProtection = false`.
2. Keep the same config.
3. Run a matching session.
4. Verify:
   - The globally-risky family IS removed.
   - `Phase8EReliabilityNotes` contains `conditional-promising-observed: [<family>]`.
   - No `conditional-promising-protected-families` appears.

### Active mode — protection enabled, state matches

1. Set `Mode = "active"`, `EnableConditionalPromotionProtection = true`.
2. Keep the same config.
3. Run a session with the matching learner state.
4. Verify:
   - The family is NOT removed.
   - Its owned parameter change is NOT removed.
   - `Phase8EReliabilityNotes` contains `conditional-promising-protection-candidates`,
     `conditional-promising-protected-families`, and `mode=active`.
   - No `suppressed-params` for the protected family.

### Active mode — protection enabled, state does not match

1. Same config but run a session with a non-matching state.
2. Verify:
   - The family IS removed (no conditional promising match → no protection).
   - Notes contain `risky-families-detected` and `suppressed-params`.
   - No protection notes.

### Risky wins scenario

1. Configure a family as globally risky, conditionally promising for state A, conditionally risky for state B.
2. Run a session where both A and B are active.
3. Verify:
   - Family IS removed.
   - Notes contain `conditional-promising-protection-candidates` and `risky-overrides-promising`.
   - No `conditional-promising-protected-families`.

---

## What Phase 9E does NOT do

- Does not create new intervention families.
- Does not add new parameter changes to the adaptation.
- Does not boost or intensify any parameter values.
- Does not override Phase 8A safety guardrails.
- Does not prevent conditional risky suppression (Phase 9D).
- Does not force difficulty increases, pace increases, or hint reductions.
- Does not change the Unity/client response shape.
- Does not add new ADX tables or columns.
- Does not change Phase 8E global suppression behavior for unprotected families.
- Does not change `AdaptationPolicyService`.
