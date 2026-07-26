# Current Feature

<!-- Feature name and short description -->

Income/budget-allocation history: snapshot + next-month-effective. Item 2 of `context/be-revamp.md`'s build order. Replaces the single mutable `Customer.MonthlyIncomeExpected/NeedsPct/WantsPct/SavingsPct` row (which today lets an edit retroactively change past months' bucket-adherence numbers) with a versioned history table, resolved per requested month.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- New table `income_allocation_settings`: one row per `(CustomerId, EffectiveMonth)`, `EffectiveMonth` as a `YYYY-MM` string (matching `BudgetService.ResolveMonthWindow`'s existing `MonthWindow.Key` format/ICT convention). Unique on `(CustomerId, EffectiveMonth)`, check constraint `needs_pct + wants_pct + savings_pct = 100` (mirroring the existing `chk_buckets_sum` constraint on `customers`).
- New `IIncomeAllocationService` (Infrastructure) resolving:
  - `GetEffectiveAsync(customerId, month)` — latest history row with `EffectiveMonth <= month`, carry-forward; falls back to the customer's onboarding-time `Customer` columns if no history row exists yet.
  - `GetPendingAsync(customerId)` — the scheduled-for-next-month draft, if any.
  - `ScheduleNextMonthAsync(customerId, monthlyIncome, needsPct, wantsPct, savingsPct)` — always upserts the entry for **next calendar month**; calling it again before rollover revises the same draft rather than creating a new row or touching the current/past entry.
- `BudgetService.GetBudgetBucketsAsync` switches from reading `customer.MonthlyIncomeExpected/NeedsPct/WantsPct/SavingsPct` directly to `IIncomeAllocationService.GetEffectiveAsync(customerId, window.Key)` — this is the actual bug fix (past/current-month bucket summaries stop moving when a future month is scheduled).
- New endpoints on `ProfileController`: `GET /api/profile/income-allocation` (current + pending) and `POST /api/profile/income-allocation` (schedule next month).
- `UpdateProfileCommandHandler`'s direct overwrite of `MonthlyIncomeExpected/NeedsPct/WantsPct/SavingsPct` becomes onboarding-only: once `Customer.OnboardingDone` is true, editing any of those four fields via `PUT /api/profile` throws instead of silently mutating the live row — post-onboarding edits must go through the new schedule endpoint. Pre-onboarding, behavior is unchanged (this is exactly where the "onboarding-time defaults" the resolver falls back to get set).

## Notes

<!-- Any extra notes -->

- Full backend audit and decisions: `context/be-revamp.md` (item 2).
- Two separate scoring paths exist server-side: `BudgetService`'s bucket-adherence score (what this feature fixes) and a separate `AiSpendingScore`/`SpendingScoreService` weekly/monthly score, which does not use these percentages at all and already snapshots per period via its own idempotent-insert idiom (modeled loosely here for the history table's upsert behavior, though the semantics differ: that one is backward-looking/closed-period, this one is forward-scheduling).
- Next migration number is `V22` (last is `V21__sepay_webhook_registration.sql`, from the teammate's merged SePay work).
- Not verified against a live database or FE in this session — build + targeted manual check only.

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-07-26 — Started. Branch `feature/remove-finverse` created.
- 2026-07-26 — Implemented on branch `feature/remove-finverse`: deleted all Finverse-only files (entity, external-service client, wallet-sync service, DTOs, config example, unit test, docs page); removed the 4 Finverse actions from `WalletsController` and its DI registrations; removed the `FinverseLink` nav property/DbSet/entity config; added migration `V20__drop_finverse.sql` (drops `finverse_links` table only — kept `WalletType.FinverseLinked`/`EntryMethod.FinverseSync` CLR enum members per the `V15` precedent, since Postgres can't drop individual enum values); generalized `WalletService`'s withdraw/transfer read-only-linked-wallet checks and `WalletResponse`'s institution/mask/synced-at display fields from Finverse-only to SePay (they were never wired to SePay, which would have silently broken withdrawal and wallet-info display for the sole remaining provider); trimmed the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository`; updated `docs/api-reference.md`. `dotnet build` passed with 0 errors.
- 2026-07-27 — Item 1 superseded: a teammate independently implemented the same removal (plus SePay OAuth/webhook hardening and an AI-provider swap) on `origin/dev` (commits `8c4be9f`, `f95f2ab`) before this branch was committed upstream. Per user decision, `feature/remove-finverse`'s code changes were dropped in favor of the teammate's version — `khoi` was merged with `origin/dev` directly (merge commit `6fdc8ed`) instead. Only the `context/*.md` planning docs were carried over from the abandoned branch. `dotnet build` passes on `khoi` post-merge with 0 errors. Item 1 of `be-revamp.md` is done.
- 2026-07-27 — Started item 2 (income-allocation history). Branch `feature/income-allocation-history` created.
- 2026-07-27 — Implemented: new `income_allocation_settings` table (migration `V22__income_allocation_history.sql`) + `IncomeAllocationSetting` entity/EF mapping; new `IIncomeAllocationService`/`IncomeAllocationService` (`GetEffectiveAsync`, `GetSummaryAsync`, `ScheduleNextMonthAsync`); new `GET`/`POST /api/profile/income-allocation` endpoints (`GetIncomeAllocationQuery`, `ScheduleIncomeAllocationChangeCommand` + validator, per-command-subfolder CQRS style); `BudgetService.GetBudgetBucketsAsync` now resolves allocation via the service instead of reading `Customer` columns live, for the requested month; `UpdateProfileCommandHandler` now throws `BusinessRuleException("allocation_locked_use_schedule_endpoint")` if income/allocation fields are sent after onboarding is done (pre-onboarding behavior unchanged). Extracted the resolver's pure "latest row ≤ month" comparison and the ICT month-key computation as `internal static` methods on `IncomeAllocationService`, added `InternalsVisibleTo` from Infrastructure to `FinViet.Application.UnitTests`, and added 11 unit tests (`IncomeAllocationServiceTests.cs`, `TC-INCALLOC-01..08`) covering carry-forward, future-row exclusion, insertion-order independence, and the ICT year-rollover edge case. `dotnet build` passes with 0 errors; all 32 unit tests pass (11 new + 21 pre-existing). Updated `docs/api-reference.md`. Not verified against a live database or integration test suite in this session.
