# Current Feature

<!-- Feature name and short description -->

Docs: document the two undocumented health/status minimal-API endpoints
(`GET /`, `GET /health` in `Program.cs`) in `docs/api-reference.md`, and
remove `docs/10-08-2026-be-todos.md` now that all 3 of its items have shipped
and been folded into `api-reference.md`. Companion work on `finviet-mobile`:
wiring CSV import to the already-working `POST /extract/csv` endpoint, and
correcting stale "AI is mock-only" docs there (the real AI wiring already
exists, no backend change involved).

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- Add a small "Health / Status" section to `docs/api-reference.md` covering
  `GET /` (`{ service, status: "running" }`) and `GET /health`
  (`{ status: "healthy" }`) — these are minimal APIs registered directly in
  `Program.cs` (commit `f28bc51`), not a controller, so they don't fit the
  existing per-controller table format.
- Delete `docs/10-08-2026-be-todos.md` — its 3 items (transaction
  wallet-type-conditional edit fields, savings-goal ledger rework,
  income-allocation arbitrary-month lookup) are all implemented (commits
  `683780e`, `a7529af`, and the income-allocation-month-lookup work logged
  below) and already reflected in `api-reference.md`. User confirmed
  deletion over archiving.
- No other doc changes — every other route across all 12 controllers already
  matches `api-reference.md` exactly (verified by reading each controller).

## Notes

<!-- Any extra notes -->

- Prompted by the user asking to "update backend's api docs" and get a plan
  for new mobile API work, while testing localhost on both sides this
  session.
- AI provider is switching from local Ollama to Gemini (Google AI Studio API
  key) — user will paste the key into the codebase directly. `Ai` section in
  `appsettings.json` still shows the Ollama config as of this feature's
  start; no code change made here for that switch, it's the user's to land.
- No commit or push without explicit user permission.

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-08-13 — Started. Branch `docs/api-reference-health-status` created
  from `dev`.
- 2026-08-13 — Implemented: added a "Health / Status" section to
  `docs/api-reference.md` (between Conventions and Auth) documenting
  `GET /` and `GET /health`; deleted `docs/10-08-2026-be-todos.md`. Doc-only
  change, no `dotnet build` impact. Awaiting commit approval.
