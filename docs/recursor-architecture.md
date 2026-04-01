# Recursor Engine Architecture Spec

## Purpose

Recursor Engine is a sim-agnostic adaptive training engine.

A user completes a WebGL training simulation.
The sim emits behavioral telemetry.
The backend derives features from telemetry.
The engine interprets behavioral dimensions and hypotheses.
The system returns bounded adaptive parameter changes to the sim.

## Basic slice architecture

For the first implementation:

- Front end: Unity WebGL sim running through a Blazor WebAssembly application
- Backend: .NET 8 API in the Server project
- Session lifecycle state: in memory
- Sim catalog/capability registry: in memory
- Telemetry and derived records: Azure Data Explorer
- Feature extraction: .NET
- Behavioral interpretation: .NET
- Adaptation policy: .NET

## Basic slice pipeline

1. Sim starts session through API
2. API creates in-memory session state
3. Sim sends batched event data to API
4. API validates active session
5. API ingests raw events into ADX
6. API updates in-memory session counters/state
7. API attempts to build feature window
8. If a feature window is produced, API ingests it into ADX
9. API builds behavior profile
10. API ingests behavior profile into ADX
11. API builds hypothesis set
12. API ingests hypothesis set into ADX
13. API applies adaptation policy
14. If an adaptation is produced, API ingests adaptation decision into ADX
15. API returns bounded parameter changes to the sim

## Rules

- Sims emit facts, not high-level interpretations.
- Recursor interprets behavior centrally.
- Adaptations must be bounded to allowed sim parameters.
- Do not let the model or policy layer invent arbitrary sim commands.
- Keep the first slice simple and explicit.

## Current storage decision

Use Azure Data Explorer for these data classes:

- RawEvents
- FeatureWindows
- BehaviorProfiles
- HypothesisSets
- AdaptationDecisions

Keep these out of ADX for the first slice:

- live session state
- sim catalog metadata used by API at startup/runtime

## Session state decision

The first slice keeps session state in memory.
Do not replace it with Cosmos DB, SQL, or ADX unless explicitly requested.

## Sim catalog decision

The first slice keeps sim catalog data in memory and seeds one or more known sims.
Do not replace it with a database unless explicitly requested.

## Controller/service design

### Controllers
- `RecursorController`
  - `POST /api/recursor/sessions/start`
  - `POST /api/recursor/events/batch`
  - `POST /api/recursor/sessions/{sessionId}/end`

### Services
- `IRecursorSessionService`
- `IRecursorIngestionService`
- `IAdxIngestionService`
- `IAdxRecursorQueryService`
- `IFeatureExtractionService`
- `IBehaviorInterpreter`
- `IAdaptationPolicyService`

### In-memory repositories
- `ISessionRepository`
- `ISimCatalogRepository`

## Design constraints

- Preserve the current Recursor terminology.
- Prefer explicit code over clever abstractions.
- Prefer vertical-slice clarity over generalized infrastructure.
- Do not refactor unrelated code when implementing this architecture.