# Recursor Sim Explorer Dashboard

## Purpose

The Sim Explorer is a read-only internal dashboard for browsing Recursor data by simulation, user, and session. It complements the existing `/recursor-dashboard` page by adding a hierarchical drill-down from sim → user → session rather than a flat chronological session list.

The Sim Explorer does not affect the adaptive pipeline. No ingestion, model, or adaptation behavior is changed.

## Routes

| Route | Page | Description |
|-------|------|-------------|
| `/recursor-sims` | RecursorSims | All sims with aggregate counts |
| `/recursor-sims/{simId}` | RecursorSimUsers | Users for a selected sim |
| `/recursor-sims/{simId}/users/{userId}/sessions` | RecursorSimUserSessions | Sessions for a user in a sim |
| `/recursor-sims/{simId}/users/{userId}/sessions/{sessionId}` | RecursorSimSessionDetail | Full detail: timeline + raw telemetry |

Route parameters are URL-encoded with `Uri.EscapeDataString`. ASP.NET decodes them automatically before they reach the controller.

## API Endpoints

All under `GET /api/recursor/sim-explorer/`:

| Endpoint | Returns |
|----------|---------|
| `sims` | `List<SimExplorerSimSummaryDto>` |
| `sims/{simId}/users` | `List<SimExplorerUserSummaryDto>` |
| `sims/{simId}/users/{userId}/sessions` | `List<SimExplorerSessionSummaryDto>` |
| `sims/{simId}/users/{userId}/sessions/{sessionId}` | `SimExplorerSessionDetailDto` or 404 |

## What Each Page Shows

### /recursor-sims

- Table of all sims in ADX
- Per-sim: user count, scenario count, session count, window count, raw event count, adaptation count, first/last active
- Link to browse each sim's users

### /recursor-sims/{simId}

- Table of all users with sessions in the selected sim
- Per-user: session count, window count, raw event count, adaptation count, avg behavior scores, first/last active
- Link to browse each user's sessions

### /recursor-sims/{simId}/users/{userId}/sessions

- Summary cards: total sessions, windows, raw events, adaptations, safety errors
- Table of sessions with per-session analytics
- Per-session: scenario, window count, raw event count, adaptation count, category event counts (safety errors, errors, successes), avg behavior scores
- Link to view full session detail

### /recursor-sims/{simId}/users/{userId}/sessions/{sessionId}

Two sections:

**Section A — Recursor Timeline**

Identical in content to the timeline in `/recursor-dashboard`. Per behavior window:
- Window index, behavior state (badge), behavior scores
- Near-term risk level
- Sequence features (count, volatility, momentum)
- Adaptation decision (intervention families, reasoning summary, policy notes)
- Coach / Explanation (expandable — detected patterns, GPT message)
- Temporal prediction horizons H1/H2/H3 vs observed targets with correctness indicators

**Section B — Raw Sim Telemetry**

- Event count badges by category and event type
- Client-side filters: category dropdown + event type text search
- Table of raw events (up to 2000; truncation warning if exceeded)
- Columns: Seq, Time, EventType, Category, Actor, Target, Metrics (expandable JSON), Context (expandable JSON), Payload (expandable JSON)
- Generic JSON rendering — no hardcoded sim-specific fields
- Structured so sim-specific renderers can be added in a later phase

## How It Differs from /recursor-dashboard

| Feature | /recursor-dashboard | /recursor-sims |
|---------|--------------------|--------------------|
| Entry point | Flat list of 20 recent sessions | Hierarchical: sim → user → sessions |
| Scope | Global, recent | Filtered by sim and user |
| Raw telemetry | Not shown | Shown in session detail |
| Session limit | 20 recent | All sessions for the selected user |
| Model comparison | Elevated-risk comparison panel | Not shown |
| Use case | Quick overview of recent activity | Deep-dive into a specific sim/user/session |

## How It Helps Second-Sim Integration

Before running the medical-supply stocking sim:

1. Open `/recursor-sims` to confirm the sim appears once data is ingested.
2. Select the medical-supply sim to see which users have run sessions.
3. Select a user to review their session list and check for safety errors.
4. Open a session to inspect the raw telemetry and verify the event schema matches the sim contract.
5. Check the Recursor timeline to verify behavior windows are being populated correctly.

## How to Use for the Medical-Supply Sim

1. Run a test session through the medical-supply Unity WebGL sim.
2. Navigate to `/recursor-sims` — the new sim ID should appear within seconds of the first batch.
3. Drill into the sim, user, and session to inspect:
   - Raw events: verify EventType and Category values match the sim contract.
   - Recursor timeline: confirm behavior windows and scores are reasonable.
   - Adaptation decisions: check intervention families and parameter changes.
4. If raw event counts look wrong, use the category filter to isolate specific event types.

## Known Limitations

- Raw telemetry is rendered as generic JSON. No sim-specific field labels or structured display yet.
- No graphs or time-series charts — tables only.
- Requires ADX data to exist. If ADX is unconfigured or no sessions have been ingested, all pages show empty state.
- Raw events are capped at 2000 rows per session. A truncation warning is shown when the limit is reached.
- First/last seen timestamps come from BehaviorStateTrainingRows (behavior window timestamps), not raw event timestamps. Sessions with events but no completed windows may not appear.
- All data is read-only. Nothing on these pages affects the adaptive engine.
- Session detail raw events are filtered by SessionId only (not SimId/UserId) since SessionId is the natural partition key in RawEvents.
