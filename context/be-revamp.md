# BE Plan: Category System, Settings, Bank-Linking & Budget Allocation

**Status:** Agreed 2026-07-26 — implementation not started. Each item below will get its own `context/current-feature.md` cycle + feature branch when work begins, per `context/ai-interaction.md`.

## Context

Companion to `fe-plan-2026-07-revamp.md` (FE repo), which triggered this plan. The compliance review and Q&A that produced the decisions below (`be-notes.md`) is no longer kept in this repo; FE confirmed all four open questions and both corrections on 2026-07-26 with no changes needed. This document is the finalized, mutually-agreed backend scope — it's what actually gets implemented, one item at a time, in the build order below.

Two of the five FE items turned out not to be "FE-only" as originally framed: Finverse removal and income-allocation history both require dedicated backend feature cycles. A third (custom categories) needs one new endpoint. The rest are already compliant or need no BE change.

---

## 1. Finverse Removal

**Status: Done (2026-07-27)** — landed via a teammate's independent implementation on `origin/dev` (commits `8c4be9f`, `f95f2ab`, which also hardened SePay OAuth/webhook handling), merged into `khoi`. An initial implementation on branch `feature/remove-finverse` following the plan below was superseded and dropped in favor of the teammate's version. See `context/current-feature.md` history for details.

**Current state (at time of planning):** Finverse is a full, real integration — `FinverseLink` entity, `ExternalServices/Finverse/*` (client, options, token protector, API models), `FinverseWalletService`, dedicated actions in `WalletsController.cs` (`CreateFinverseLinkToken`, `CompleteFinverseLink`, `FinverseCallback`, `SyncFinverseWallet`), DI registrations in `DependencyInjection.cs`, `appsettings.Finverse.example.json`, `docs/finverse-setup.md`, and `FinverseApiModelTests.cs`. Structurally isolated from SePay (mirrors it 1:1) — safe to delete as its own cycle, but two things need care rather than blind deletion.

**Planned changes:**
- Delete: `FinverseLink.cs`, `ExternalServices/Finverse/` (entire folder), `FinverseWalletService.cs`, `IFinverseWalletService.cs`, `FinverseWalletDtos.cs`, `FinverseApiModelTests.cs`, `appsettings.Finverse.example.json`.
- `WalletsController.cs`: remove the four Finverse-specific actions only; leave SePay actions untouched.
- `DependencyInjection.cs`: remove `FinverseOptions`, `IFinverseTokenProtector`, `IFinverseLinkStateProtector`, `IFinverseClient` HttpClient, `IFinverseWalletService` registrations.
- `Wallet.cs`: remove the `FinverseLink?` nav property (keep `SepayLink?`).
- `WalletType` enum (`src/FinViet.Domain/Enums/WalletType.cs`): remove `FinverseLinked`. Before writing the migration, **query production/staging for any wallets with `wallet_type = 'finverse_linked'`** — expected near-zero since Finverse never went live, but confirm rather than assume. If any exist, decide re-type target (likely `basic`) as part of the migration.
- New migration dropping the `finverse_link` table, the `FinverseLinked` enum value (Postgres enum alteration), and any FK/index tied to it. Read `V15__drop_sepay_linked_wallets.sql` first — its name looks backwards relative to its actual effect; understand why before repeating whatever pattern it used.
- `docs/integration-status.md`: drop Finverse rows.

**Key files:** listed above; `WalletsController.cs` and `DependencyInjection.cs` need surgical edits, everything else is outright deletion.

## 2. Income-Allocation History (Snapshot + Next-Month-Effective)

**Status: Done (2026-07-27)** — implemented on branch `feature/income-allocation-history`. See `context/current-feature.md` history for the exact file list; behavior matches the plan below as written (income/allocation edits after onboarding now throw `allocation_locked_use_schedule_endpoint` instead of overwriting `Customer` in place; `BudgetService` resolves per-month via the new service).

**Current state (at time of planning):** `Customer.MonthlyIncomeExpected/NeedsPct/WantsPct/SavingsPct` (`Customer.cs`) are plain mutable columns, no history. `UpdateProfileCommandHandler` (`PUT /api/profile`) overwrites them in place immediately. `BudgetService.GetBudgetBucketsAsync` (`GET /api/budgets/buckets`) reads these live columns for whatever `?month=` is requested — including past months — so editing the allocation today retroactively changes past months' bucket-adherence numbers in production. Confirmed by FE as the score that matters (the Stitch "Target" panel maps to this path, not the separate AI Spending Score, which already snapshots per period via `AiSpendingScore`/`SpendingScoreService` and is unaffected either way).

**Planned changes:**
- New table `income_allocation_settings` (or similar): `Id`, `CustomerId`, `EffectiveMonth` (`YYYY-MM` or first-of-month date), `MonthlyIncome`, `NeedsPct`, `WantsPct`, `SavingsPct`, `CreatedAt`. Unique constraint on `(CustomerId, EffectiveMonth)`. Model the insert/keep-immutable-once-period-starts idiom after `SpendingScoreService.SnapshotAsync`'s "keep existing snapshot for a closed period" pattern, adapted for a forward-looking schedule rather than a backward-looking snapshot.
- New command `ScheduleIncomeAllocationChangeCommand` — always upserts the entry for **next calendar month** (`DateTime.UtcNow` month + 1); calling it again before rollover revises the same pending draft rather than creating a new one or touching the current/past entry.
- New query/resolver `GetEffectiveIncomeAllocation(customerId, month)` — latest entry with `EffectiveMonth <= month`, carry-forward; falls back to the customer's onboarding-time defaults if no entry exists yet for that customer.
- `BudgetService.GetBudgetBucketsAsync` switches from reading `customer.NeedsPct/WantsPct/SavingsPct/MonthlyIncomeExpected` directly to calling the resolver for the requested month.
- `UpdateProfileCommand`/`UpdateProfileCommandHandler`: remove the direct-overwrite path for `NeedsPct/WantsPct/SavingsPct/MonthlyIncomeExpected` (or keep the command for other profile fields and route allocation changes exclusively through the new schedule command — the request DTO should no longer allow an immediate in-place edit of these four fields).
- New endpoint, e.g. `POST /api/profile/income-allocation` (schedule next month) and the existing `GET /api/budgets/buckets` (and anywhere else allocation is surfaced) reads via the resolver.

**Key files:** new entity + migration + repository, `BudgetService.cs`, `UpdateProfileCommand.cs`/`UpdateProfileCommandHandler.cs`, new `Features/Profile/Commands/ScheduleIncomeAllocationChange/` (following the existing per-command-subfolder CQRS style — see `coding-standards.md`).

## 3. Customer Settings Endpoint (Theme + Budget Alert Thresholds)

**Status: Done (2026-07-27)** — implemented on branch `feature/customer-settings-endpoint`. The `customer_settings` table's existence was uncertain (no migration created it, confirmed), so a defensive `V23__ensure_customer_settings.sql` was added rather than just assuming. Also found: no code anywhere ever created a `customer_settings` row for any customer — the new endpoint upserts on first write. See `context/current-feature.md` history for the full file list.

**Current state (at time of planning):** `CustomerSetting` entity already has `Theme` (enum Light/Dark/System), `Language`, `DefaultCurrency`, `NotifBudgetThresholds` (`int[]`, default `{80,100}`) columns — but nothing in the Application/Api layers reads or writes any of them. `BudgetService.cs` hardcodes `WarningThreshold = 80m`/`ExceededThreshold = 100m` for real `budget_alert` notifications (with `Budget.LastAlertThreshold` for dedup), ignoring the orphaned `NotifBudgetThresholds` column entirely.

**Planned changes:**
- Confirm the `customer_settings` table actually exists in the current DB before building against it (no migration text matches it in the numbered `V*` scripts — may be in a baseline schema not yet audited).
- New endpoint, e.g. `PATCH /api/profile/settings` — reads/writes `Theme` and `NotifBudgetThresholds` (not `Language`/`DefaultCurrency` — FE is removing those Settings rows, so no BE work needed for them).
- `BudgetService.cs`: replace hardcoded `80m`/`100m` with the customer's `NotifBudgetThresholds` values (falling back to `{80,100}` if unset).
- `GetProfileQuery`/`ProfileDto`: include `Theme` and `NotifBudgetThresholds` in the response so FE can read current values.

**Key files:** `ProfileController.cs`, new command/query pair under `Features/Profile/Commands/UpdateSettings/` (or extend `UpdateProfileCommand`), `BudgetService.cs`, `ProfileDto.cs`/`GetProfileQuery`.

## 4. Change-Password Endpoint

**Status: Done (2026-07-27)** — implemented on branch `feature/change-password-endpoint`. See `context/current-feature.md` history for the file list.

**Current state (at time of planning):** Only unauthenticated `POST /api/auth/forgot-password` + `/reset-password` (email-token based) exist. No authenticated old-password/new-password flow. FE confirmed `ChangePasswordSheet` already calls `useChangePassword({currentPassword, newPassword})` and is currently force-routed to a mock (`real/auth.ts:27-28`) for lack of a real endpoint.

**Planned changes:**
- New endpoint `POST /api/auth/change-password` — `[Authorize]`, body `{ currentPassword, newPassword }`. Verify current password against the stored hash before updating; return `400`/`BadRequestException` on mismatch (per the existing exception-to-status-code convention in `coding-standards.md`).
- New command `ChangePasswordCommand`/handler under `Features/Auth/Commands/ChangePassword/`, following the existing per-command-subfolder pattern (see `LoginCommand`/`RegisterCommand` for the shape).

**Key files:** `AuthController.cs`, new `Features/Auth/Commands/ChangePassword/ChangePasswordCommand.cs` + handler + validator.

## 5. Custom Category Creation Endpoint

**Current state:** `Category.CategoryId` is a free-form string PK (no collision risk with a `custom_` prefix), but `POST /api/categories` is `[Authorize(Roles = "Admin")]`-only, and both `Transaction.CategoryId` and `Budget.CategoryId` have real FK constraints into `categories`. FE confirmed custom categories must attach to real transactions — a locally-created category with no server-side `Category` row would fail those FKs the moment a customer tries to use it.

**Planned changes:**
- New endpoint, e.g. `POST /api/categories/custom` — `[Authorize(Roles = "Customer")]`, body `{ name, bucket, color, type }`. Server generates the `custom_<uuid>` id (never trust a client-supplied id, to guarantee the prefix distinguishes custom from seeded `cat_*` categories). Inserts a real `Category` row so `Transaction`/`Budget` FKs resolve normally.
- The icon file itself never reaches the backend (per FE's plan — local-only, device storage) — only this metadata syncs, consistent with the existing bucket-override endpoint's no-admin-review shape.
- Reuse existing uniqueness checks from `CategoryService.CreateCategoryAsync` (id collision, duplicate name) rather than duplicating that logic — extend the service with a customer-scoped creation path instead of writing a parallel implementation.

**Key files:** `CategoriesController.cs`, `CategoryService.cs` (new method, e.g. `CreateCustomCategoryAsync`), possibly a new DTO for the customer-facing request shape (vs. the admin `CreateCategoryRequest`).

## 6. Category Bucket Move (Drag-and-Drop Support)

**Current state:** Fully compliant, no BE changes needed. `PUT /api/categories/{id}/bucket` / `DELETE /api/categories/{id}/bucket` already support all three buckets (needs/wants/savings) with no savings restriction — the `buckets.is_locked` flag exists in schema but is never read by `CategoryService.SetCustomerBucketAsync`. FE's drag-and-drop (their item 5) builds against this endpoint as-is.

**Key files:** none — reference only, confirming the existing endpoint is sufficient.

---

## Suggested Build Order

Matches FE's own sequencing where it lines up, so both repos' branches land in a compatible order:

1. ~~**Finverse removal (item 1)**~~ — **Done** (see item 1 above).
2. ~~**Income-allocation history (item 2)**~~ — **Done** (see item 2 above).
3. ~~**Customer settings endpoint (item 3)**~~ — **Done** (see item 3 above).
4. ~~**Change-password endpoint (item 4)**~~ — **Done** (see item 4 above).
5. **Custom category creation endpoint (item 5)** — needed before FE's item 1 (custom-icon flow) can fully land, since custom categories aren't usable in real transactions without it.
6. **Category bucket move (item 6)** — no work; just confirm still-correct when FE's drag-and-drop (their item 5) starts hitting it.

## Verification

Per item, once implemented:
- `dotnet build` and `dotnet test` (existing integration suite in `tests/FinViet.Api.IntegrationTests` plus any new unit tests for pure logic — see the income-allocation resolver in item 2 as the primary unit-test candidate).
- Manual pass via Swagger UI or a REST client: new endpoints return correct status codes for the happy path and the documented error cases (per the exception-to-status-code table in `coding-standards.md`).
- For item 1 (Finverse removal): confirm no dangling references (`dotnet build` will catch compile-time ones; grep for `Finverse` in `appsettings.json`/config for runtime ones).
- For item 2 (income-allocation history): confirm a scheduled next-month change does **not** alter `GET /api/budgets/buckets?month=<past or current month>` — this is the core bug being fixed, so it's the one behavior worth testing explicitly end-to-end.

## History

- 2026-07-26 — Plan finalized after `be-notes.md` review and FE's confirmation of all four open questions. Implementation not started.
