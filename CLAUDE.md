# Claude Code Project Instructions

Read and follow these files as the implementation spec for Recursor Engine:

@docs/recursor-architecture.md
@docs/recursor-domain-models.md
@docs/recursor-adx-plan.md

## Working style

- Treat the imported Recursor docs as binding design guidance unless I explicitly override them.
- Do not invent a different architecture when implementing Recursor-related code.
- Prefer small, reviewable edits over broad refactors.
- Before making edits, list:
  1. files you plan to create or modify
  2. why each file needs to change
  3. any assumptions or ambiguities
- If a requested change conflicts with the Recursor docs, explain the conflict before editing.
- Preserve unrelated existing registrations and code paths unless they directly conflict with the requested Recursor implementation.
- When editing `Program.cs`, make the narrowest possible DI/config changes.
- Do not introduce extra abstractions, frameworks, or services unless required by the spec or compilation.
- Keep names stable if they already appear in the Recursor docs.

## Recursor-specific constraints

- Session lifecycle state stays in memory for the basic slice.
- Sim catalog stays in memory for the basic slice.
- Azure Data Explorer is the telemetry persistence/query layer for the basic slice.
- Do not replace ADX with Cosmos DB, SQL, or Blob storage unless explicitly asked.
- Keep feature extraction and interpretation logic in .NET code for the basic slice.
- Use ADX for:
  - raw events
  - feature windows
  - behavior profiles
  - hypothesis sets
  - adaptation decisions

## Implementation expectations

- Show the planned file list before editing.
- After editing, summarize:
  - what changed
  - how it matches the spec
  - any follow-up steps still needed
- If you are unsure, ask for clarification instead of silently redesigning the system.