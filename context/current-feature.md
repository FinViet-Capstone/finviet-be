# Current Feature

<!-- Feature name and short description -->

Custom category creation + deletion endpoints. Item 5 of `context/be-revamp.md`'s build order, plus a user-requested follow-up: `POST /api/categories` is Admin-only, but FE's local-custom-icon flow needs customers to create (and later delete) their own categories that real transactions/budgets can reference — which requires a real `Category` row (FK constraints from `Transaction`/`Budget`), which today only an Admin can insert or remove.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- New `POST /api/categories/custom` — `[Authorize(Roles = "Customer")]`, body `{ name, bucket, color }`. Server generates the id as `custom_<uuid>` (never trust a client-supplied id) so it's always distinguishable from seeded `cat_*` categories.
- Always creates an `expense`-type category — the FE flow only ever picks a bucket (needs/wants/savings), which only applies to expense categories; there's no FE path that produces an "income" custom category, so the request doesn't take a `type` field (simplification vs. `be-revamp.md`'s literal `{ name, bucket, color, type }` shape).
- Reuses `CategoryService`'s existing duplicate-name check (`CategoryNameExistsAsync`) rather than duplicating that logic — extends the service with a customer-scoped creation path alongside the existing admin one (`CreateCategoryAsync`), not a parallel implementation.
- Seeds an active `CustomerCategory` override row for the creator at creation time (bucket = whatever they picked), so the category is immediately usable without a follow-up `PUT .../bucket` call.
- **Necessary addition beyond `be-revamp.md`'s literal plan text**: `Category` is a global table — `GetCategoriesAsync`/`GetCategoryByIdAsync` returned every row to every caller, customer or not. Without a visibility fix, every customer's custom category would leak into every *other* customer's category list and detail lookups the moment one was created. Both methods now treat any `custom_`-prefixed category as private to its creator — visible only to a customer who has an active `CustomerCategory` row for it (which creation seeds). This wasn't spelled out in the plan but is required for the feature to be safe to ship at all.

## Notes

<!-- Any extra notes -->

- Full backend audit and decisions: `context/be-revamp.md` (item 5).
- FE's own `customCategories.ts` service module (per `fe-plan-2026-07-revamp.md`) anticipates `createCustomCategory`, `getCustomCategories`, `deleteCustomCategory`. Listing reuses the existing `GET /api/categories` (now correctly scoped) — no new BE work needed for that one. Creation and deletion are both now built (see History).
- The icon file itself never reaches the backend (per FE's plan — local-only, device storage) — only `name`/`bucket`/`color` sync; `Icon` is left null on the created `Category` row.
- Not verified against a live database or integration test suite in this session.

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
- 2026-07-27 — User requested the flagged delete follow-up be built too. **Process slip repeated**: started directly on `khoi` again instead of branching first — caught before committing, same as item 3; branched (`feature/custom-category-delete`) from that state before committing. Implemented `ICategoryService.DeleteCustomCategoryAsync` (mirrors `DeleteCategoryAsync`'s "blocked if referenced by transactions" rule) + `DELETE /api/categories/custom/{id}` (Customer role, 404 for a category you don't own — same framing as the visibility scoping, not a distinct "forbidden" signal). `dotnet build` 0 errors, all 36 unit tests pass (no new ones — this path has no new *pure* logic beyond what's already covered; it reuses `IsVisibleTo`'s ownership concept directly via an `AnyAsync` check).
