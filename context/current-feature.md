# Current Feature

<!-- Feature name and short description -->

Customer settings endpoint: theme + budget alert thresholds. Item 3 of `context/be-revamp.md`'s build order. `CustomerSetting` (`customer_settings`) already had `Theme`/`NotifBudgetThresholds` columns but nothing in Application/Api ever read or wrote them, and `BudgetService` hardcoded 80%/100% alert thresholds instead of reading the (orphaned) per-customer column.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- New `PATCH /api/profile/settings` endpoint (`UpdateProfileSettingsCommand`) — reads/writes `Theme` and `NotifBudgetThresholds` only (not `Language`/`DefaultCurrency` — FE is removing those Settings rows per `be-notes.md`, decided not needed).
- No code anywhere creates a `customer_settings` row today (grepped — zero hits), so the handler upserts: creates the row on first write if `Customer.Setting` is null.
- `ProfileDto`/`ProfileDtoMapper` extended with `Theme`/`NotifBudgetThresholds`, read from `Customer.Setting` (falls back to `AppTheme.System`/`{80,100}` when no settings row exists yet). Every handler that returns a profile now `.Include(x => x.Setting)` (or `.ThenInclude` via a nav chain) — `GetProfileQueryHandler`, `UpdateProfileCommandHandler`, `UpdateProfileSettingsCommandHandler`, `LoginCommandHandler`, `RefreshTokenCommandHandler`, `GoogleLoginCommandHandler` — matching `ProfileDtoMapper`'s own stated purpose of avoiding a field silently going missing from one response site (exactly what happened to NeedsPct/WantsPct/SavingsPct before).
- `BudgetService.SyncFlatBudgetsAsync` reads the customer's `NotifBudgetThresholds` (falling back to `{80,100}` if unset) instead of the hardcoded `WarningThreshold`/`ExceededThreshold` constants; `CreateFlatBudgetAlert` takes the resolved exceeded-threshold as a parameter instead of reading a class constant.
- New migration `V23__ensure_customer_settings.sql` — defensive `CREATE TABLE IF NOT EXISTS`, since no migration or setup script in this repo actually creates `customer_settings` (it's only ever been mapped in EF, presumably relying on an externally-provisioned baseline schema that may or may not include it).

## Notes

<!-- Any extra notes -->

- Full backend audit and decisions: `context/be-revamp.md` (item 3).
- **Flag for FE, not fixed here**: no `JsonStringEnumConverter` is registered anywhere in `Program.cs`, so `Theme` (and the pre-existing `Gender` field) actually serialize as raw integers over the wire (e.g. `2` for `System`), not the string names the docs show — this was already true for `Gender` before this feature; worth confirming with FE whether their client already expects/parses integers, since the API contract doesn't match the docs' stringly-typed claim either way.
- `DefaultThresholdPct` (used for a single-budget response's `Status` field, a different concern from the two-tier alert-raising thresholds) was left untouched — not part of `be-revamp.md`'s item 3 scope.
- Not verified against a live database in this session; the `customer_settings` table's actual pre-existing state in any real environment is unconfirmed (migration is defensive/idempotent specifically because of that uncertainty).

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-07-26 — Started. Branch `feature/remove-finverse` created.
- 2026-07-26 — Implemented on branch `feature/remove-finverse`: deleted all Finverse-only files (entity, external-service client, wallet-sync service, DTOs, config example, unit test, docs page); removed the 4 Finverse actions from `WalletsController` and its DI registrations; removed the `FinverseLink` nav property/DbSet/entity config; added migration `V20__drop_finverse.sql` (drops `finverse_links` table only — kept `WalletType.FinverseLinked`/`EntryMethod.FinverseSync` CLR enum members per the `V15` precedent, since Postgres can't drop individual enum values); generalized `WalletService`'s withdraw/transfer read-only-linked-wallet checks and `WalletResponse`'s institution/mask/synced-at display fields from Finverse-only to SePay (they were never wired to SePay, which would have silently broken withdrawal and wallet-info display for the sole remaining provider); trimmed the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository`; updated `docs/api-reference.md`. `dotnet build` passed with 0 errors.
- 2026-07-27 — Item 1 superseded: a teammate independently implemented the same removal (plus SePay OAuth/webhook hardening and an AI-provider swap) on `origin/dev` (commits `8c4be9f`, `f95f2ab`) before this branch was committed upstream. Per user decision, `feature/remove-finverse`'s code changes were dropped in favor of the teammate's version — `khoi` was merged with `origin/dev` directly (merge commit `6fdc8ed`) instead. Only the `context/*.md` planning docs were carried over from the abandoned branch. `dotnet build` passes on `khoi` post-merge with 0 errors. Item 1 of `be-revamp.md` is done.
- 2026-07-27 — Item 2 (income-allocation history) implemented on branch `feature/income-allocation-history`, committed (`c31c392`), merged into `khoi` (fast-forward), branch deleted. New `income_allocation_settings` table/service/endpoints; `BudgetService` resolves allocation per requested month instead of reading `Customer` live; `UpdateProfileCommandHandler` blocks post-onboarding direct edits. 11 new unit tests (`TC-INCALLOC-01..08`), all 32 unit tests pass, `dotnet build` 0 errors.
- 2026-07-27 — Started item 3 (customer settings endpoint). Branch `feature/customer-settings-endpoint` created, implemented as described above. `dotnet build` passes with 0 errors; all 32 unit tests still pass. Awaiting commit approval.
