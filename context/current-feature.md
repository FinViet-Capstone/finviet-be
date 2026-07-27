# Current Feature

<!-- Feature name and short description -->

`TransactionsController` response envelope fix. Item 1 of a mobile-integration gap-closing plan (see `context/mobile-integration-plan.md` if present, else the session's approved plan): every other controller wraps responses in `ApiResponse<T>` (`{success, message, data}`), but `TransactionsController` returns raw `PagedResult<T>`/DTOs/`bool` directly — the one documented exception in this file's own conventions section. Closing that inconsistency so the mobile client can drop its special-case unwrapping and use the shared `unwrap<T>()` helper like every other domain.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- Wrap all 7 `TransactionsController` actions' return values in `ApiResponse<T>.Ok(result)`: `GetTransactions`, `GetSummary`, `GetTransactionById`, `CreateTransaction`, `UpdateTransaction`, `DeleteTransaction`, `ClassifyTransaction`.
- This is a breaking wire-format change for this one controller — must ship alongside the matching mobile change (`finviet-mobile/src/services/real/transactions.ts`) in the same working session/release, not independently.
- Update `docs/api-reference.md`'s Transactions section to show the now-consistent envelope.
- No business-logic changes — purely a response-shape wrap, per the "minimal, scoped changes" standing rule.

## Notes

<!-- Any extra notes -->

- Companion mobile-side change lives in the `finviet-mobile` repo, tracked separately there (not part of this repo's `context/` docs).
- Part of a larger approved plan covering: this envelope fix, SMS-extraction mobile wiring, Google OAuth mobile wiring, photo-extraction (new OCR endpoint), and a free/premium subscriptions feature — those are separate future entries in this file, done one at a time.

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-07-26 — Started. Branch `feature/remove-finverse` created.
- 2026-07-26 — Implemented on branch `feature/remove-finverse`: deleted all Finverse-only files (entity, external-service client, wallet-sync service, DTOs, config example, unit test, docs page); removed the 4 Finverse actions from `WalletsController` and its DI registrations; removed the `FinverseLink` nav property/DbSet/entity config; added migration `V20__drop_finverse.sql` (drops `finverse_links` table only — kept `WalletType.FinverseLinked`/`EntryMethod.FinverseSync` CLR enum members per the `V15` precedent, since Postgres can't drop individual enum values); generalized `WalletService`'s withdraw/transfer read-only-linked-wallet checks and `WalletResponse`'s institution/mask/synced-at display fields from Finverse-only to SePay (they were never wired to SePay, which would have silently broken withdrawal and wallet-info display for the sole remaining provider); trimmed the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository`; updated `docs/api-reference.md`. `dotnet build` passed with 0 errors.
- 2026-07-27 — Item 1 superseded: a teammate independently implemented the same removal (plus SePay OAuth/webhook hardening and an AI-provider swap) on `origin/dev` (commits `8c4be9f`, `f95f2ab`) before this branch was committed upstream. Per user decision, `feature/remove-finverse`'s code changes were dropped in favor of the teammate's version — `khoi` was merged with `origin/dev` directly (merge commit `6fdc8ed`) instead. Only the `context/*.md` planning docs were carried over from the abandoned branch. `dotnet build` passes on `khoi` post-merge with 0 errors. Item 1 of `be-revamp.md` is done.
- 2026-07-27 — Item 2 (income-allocation history) implemented on branch `feature/income-allocation-history`, committed (`c31c392`), merged into `khoi` (fast-forward), branch deleted. New `income_allocation_settings` table/service/endpoints; `BudgetService` resolves allocation per requested month instead of reading `Customer` live; `UpdateProfileCommandHandler` blocks post-onboarding direct edits. 11 new unit tests (`TC-INCALLOC-01..08`), all 32 unit tests pass, `dotnet build` 0 errors.
- 2026-07-27 — Item 3 (customer settings endpoint) implemented; caught mid-way that it had been started directly on `khoi` instead of a branch — corrected by branching (`feature/customer-settings-endpoint`) from that state before committing. Committed (`216222d`, `f31ecee`), merged into `khoi` (fast-forward), branch deleted. New `PATCH /api/profile/settings`; `BudgetService` reads per-customer alert thresholds; defensive `V23` migration since no script ever created `customer_settings`. `dotnet build` 0 errors, all 32 unit tests pass.
- 2026-07-27 — Item 4 (change-password endpoint) implemented on branch `feature/change-password-endpoint`, committed (`e71a3c3`), merged into `khoi` (fast-forward), branch deleted. New `POST /api/auth/change-password`; revokes other active refresh tokens on success. `dotnet build` 0 errors, all 32 unit tests pass.
- 2026-07-27 — Started item 5 (custom category creation endpoint). Branch `feature/custom-category-endpoint` created.
- 2026-07-27 — Implemented: `ICategoryService.CreateCustomCategoryAsync` + `POST /api/categories/custom` (Customer role); new `CreateCustomCategoryRequest` DTO; `GetCategoriesAsync`/`GetCategoryByIdAsync` fixed to scope `custom_*` categories to their creator via a new `IsVisibleTo` check (necessary addition beyond the original plan — see Goals). 4 new unit tests (`TC-CUSTOMCAT-01..04`) for the visibility logic, all 36 unit tests pass, `dotnet build` 0 errors. Updated `docs/api-reference.md`. Committed (`7429881`), merged into `khoi` (fast-forward), branch deleted.
- 2026-07-27 — User requested the flagged delete follow-up be built too. **Process slip repeated**: started directly on `khoi` again instead of branching first — caught before committing, same as item 3; branched (`feature/custom-category-delete`) from that state before committing. Implemented `ICategoryService.DeleteCustomCategoryAsync` (mirrors `DeleteCategoryAsync`'s "blocked if referenced by transactions" rule) + `DELETE /api/categories/custom/{id}` (Customer role, 404 for a category you don't own — same framing as the visibility scoping, not a distinct "forbidden" signal). `dotnet build` 0 errors, all 36 unit tests pass (no new ones — this path has no new *pure* logic beyond what's already covered; it reuses `IsVisibleTo`'s ownership concept directly via an `AnyAsync` check). Committed (`ada6fb8`), merged into `khoi`.
- 2026-07-27 — Started `TransactionsController` envelope fix (item 1 of the mobile-integration gap plan). Branch `fix/transactions-response-envelope` created.
- 2026-07-27 — Implemented: all 7 `TransactionsController` actions now return `ApiResponse<T>.Ok(result)` instead of the raw DTO/`PagedResult`/`bool`. Updated `docs/api-reference.md` (Conventions section + Transactions table) to drop the "one exception" note. Companion mobile change made in `finviet-mobile` (`src/services/real/transactions.ts`): all `res.data as X` reads replaced with the shared `unwrap<X>(res)` helper from `src/lib/api.ts`; removed the file's now-redundant local `unwrapEnvelope` duplicate (the transfer endpoint already used it, now shares the same helper as everything else). `dotnet build` 0 errors, all 36 unit tests pass; mobile `npx tsc --noEmit` clean.
