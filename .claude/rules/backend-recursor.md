---
paths:
  - "Server/**/*.cs"
  - "Server/**/*.json"
---

# Recursor backend rules

- Follow the Recursor docs imported by the project `CLAUDE.md`.
- For Recursor work, preserve the existing architecture:
  - in-memory session state
  - in-memory sim catalog
  - ADX telemetry persistence
  - .NET feature extraction
  - .NET interpretation
  - .NET adaptation policy
- Do not substitute Cosmos DB, SQL, or Blob for ADX unless explicitly instructed.
- Keep controllers thin.
- Put orchestration in services.
- Put mapping logic in dedicated mapper/helper classes, not controllers.
- Prefer explicit constructor injection.
- Prefer readable C# over abstract/generic-heavy designs.
- Keep edits narrow and reviewable.