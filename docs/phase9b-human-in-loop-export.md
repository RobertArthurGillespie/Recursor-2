# Phase 9B — Human-in-the-Loop Policy Recommendation Export

## Purpose

Phase 9B adds a controlled, review-gated workflow that converts Phase 9A recommendations
into a config artifact. No runtime behavior changes occur automatically. A human must
review the export, manually copy it into `appsettings.json`, and redeploy the application
before any live adaptation behavior is affected.

## Endpoints

### `GET /api/recursor/policy-recommendations/export-config`

Returns a `PolicyReliabilityConfigExport` as `application/json`.

Includes:
- `recommendedMode` — always `"shadow"`. Change to `"active"` only after human review.
- `familyReliabilityTiers` — only families with global tier `"risky"` or `"promising"`.
  `"conditional"`, `"neutral"`, and `"insufficient-data"` families are excluded.
- `metadata` — provenance, row counts, source tag, warning, and a `reviewRequired` array
  listing any `"conditional"` families with an explanation.

Example response:

```json
{
  "recommendedMode": "shadow",
  "familyReliabilityTiers": {
    "difficulty-increase": "promising",
    "hint-fade": "risky"
  },
  "metadata": {
    "generatedAtUtc": "2026-04-27T14:30:00.000Z",
    "totalEffectivenessRowsAnalyzed": 480,
    "aggregatedRowsAnalyzed": 64,
    "familiesEvaluated": 5,
    "source": "Phase9A",
    "warning": "Human review required before applying to appsettings.",
    "reviewRequired": [
      {
        "family": "scaffold-hints",
        "globalTier": "conditional",
        "reason": "Promising in some learner-state segments, risky in others. State-specific guardrail conditions required before active use."
      }
    ]
  }
}
```

### `GET /api/recursor/policy-recommendations/export-config-text`

Returns `text/plain`. Shows the exact JSON snippet to paste into `appsettings.json`,
counts of exported / omitted / conditional families, and step-by-step review instructions.

Useful for copy-paste into a text editor or CI pipeline artifact.

---

## How to apply the export safely

1. **Fetch the export.**
   Call `GET /api/recursor/policy-recommendations/export-config` or the `-text` variant.

2. **Review the full segmented data.**
   Call `GET /api/recursor/policy-recommendations` to inspect per-segment tiers
   before accepting any family recommendation. Pay attention to `"risky"` segments
   even for globally `"promising"` families.

3. **Handle conditional families separately.**
   Families in `metadata.reviewRequired` have conflicting tiers across learner-state
   segments. Do **not** add them to `FamilyReliabilityTiers` without a state-specific
   guardrail condition already in place (Phase 8A / Phase 7C).

4. **Copy the config block into `appsettings.json`.**
   Locate the `"Recursor"` section and insert or update `"PolicyReliability"`:

   ```json
   "Recursor": {
     "PolicyReliability": {
       "Mode": "shadow",
       "FamilyReliabilityTiers": {
         "difficulty-increase": "promising",
         "hint-fade": "risky"
       }
     }
   }
   ```

5. **Start in `"shadow"` mode.**
   Keep `Mode` as `"shadow"`. The system will log what it would suppress without
   modifying any adaptation output. Verify the logs look correct.

6. **Promote to `"active"` only after shadow validation.**
   Change `Mode` to `"active"` in `appsettings.json` only after shadow mode confirms
   the tiers are having the intended effect on the logged decisions.

7. **Redeploy.**
   No behavior change takes effect until the updated `appsettings.json` is deployed.
   The export itself has zero side effects.

---

## What is NOT changed by this phase

- `AdaptationPolicyService` — unchanged
- `Phase8AGuardrailModifierService` — unchanged
- `PolicyReliabilityWeightingService` (Phase 8E) — unchanged; it reads from
  `RecursorPolicyReliabilityOptions` which is only updated via `appsettings.json`
- ADX schema — no new tables or columns
- Unity / client response shape — unchanged
- `appsettings.json` — **not modified automatically**; human must copy the snippet

---

## Tier inclusion rules

| Global tier          | Included in `FamilyReliabilityTiers`? | Notes                                      |
|----------------------|----------------------------------------|--------------------------------------------|
| `"promising"`        | Yes                                    | Safe to include; review segments first     |
| `"risky"`            | Yes                                    | Include to allow Phase 8E suppression      |
| `"conditional"`      | No                                     | Appears in `metadata.reviewRequired`       |
| `"neutral"`          | No                                     | No action needed                           |
| `"insufficient-data"`| No                                     | Gather more data before deciding           |

---

## Relationship to Phase 8E

Phase 8E (`PolicyReliabilityWeightingService`) reads `FamilyReliabilityTiers` from
`RecursorPolicyReliabilityOptions` at startup. In `"active"` mode it removes
`"risky"` families from adaptation decisions and logs `"promising"` families for
audit. The Phase 9B export produces exactly the shape that Phase 8E expects — so the
copy-paste workflow is the intentional integration point between offline recommendation
and runtime policy modification.

Phase 9B does not write to Phase 8E's config. The human is the write path.
