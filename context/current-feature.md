# Current Feature

<!-- Feature name and short description -->

Render Docker deployment support for the FinViet ASP.NET Core API.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- Add a multi-stage .NET 8 `Dockerfile` that restores, publishes, and runs `FinViet.Api` on Render.
- Bind ASP.NET Core to Render's public web-service port on `0.0.0.0`.
- Add a `.dockerignore` that excludes local build output, tests, editor metadata, local configuration, and credentials from the Docker build context.
- Keep runtime configuration outside the image and provide it through Render environment variables.

## Notes

<!-- Any extra notes -->

- Render will deploy the repository root using the Docker runtime; no Render build/start commands are needed.
- `src/FinViet.Api/appsettings.json` remains local-only and must not enter the Docker build context.
- No commit or push without explicit user permission.

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
- 2026-07-27 — Implemented: all 7 `TransactionsController` actions now return `ApiResponse<T>.Ok(result)` instead of the raw DTO/`PagedResult`/`bool`. Updated `docs/api-reference.md` (Conventions section + Transactions table) to drop the "one exception" note. Companion mobile change made in `finviet-mobile` (`src/services/real/transactions.ts`): all `res.data as X` reads replaced with the shared `unwrap<X>(res)` helper from `src/lib/api.ts`; removed the file's now-redundant local `unwrapEnvelope` duplicate (the transfer endpoint already used it, now shares the same helper as everything else). `dotnet build` 0 errors, all 36 unit tests pass; mobile `npx tsc --noEmit` clean. Committed (`5ae26ce`), merged into `khoi` (fast-forward), branch deleted. Mobile side committed separately (`2885d84` on `finviet-mobile`'s `dev`).
- 2026-07-27 — Checked item 2 (SMS-extraction mobile wiring) before starting it: already fully implemented (`finviet-mobile/src/services/real/extraction.ts` calls the real `POST /extract/sms` and maps its first row to the UI's `PhotoExtractionResult` shape). No work needed — closed as already-done.
- 2026-07-27 — Asked user about remaining items 3–5: Google OAuth (item 3) skipped for now — no Firebase project configured yet. Subscriptions (item 5) skipped for now — a teammate is building a separate payment endpoint with a different provider; SePay is confirmed transaction-sync only. Photo extraction (item 4): user chose a provider-agnostic scaffold now, real OCR provider credentials to follow later. Started item 4. Branch `feature/photo-extraction-ocr-scaffold` created.
- 2026-07-27 — Implemented: `IReceiptOcrService` (`Application/Interfaces`), `OcrOptions` + placeholder `UnconfiguredReceiptOcrService` (`Infrastructure/ExternalServices/Ocr/`, throws `IntegrationUnavailableException("ocr_not_configured")` — same pattern as `SepayWalletService`'s "not configured" checks), DI registration in `DependencyInjection.cs`, and `POST /api/extract/photo` on `ExtractController` (8 MB / jpg-jpeg-png-heic validation, returns `ApiResponse<ExtractResponse>` reusing the SMS/CSV shape). Updated `docs/api-reference.md`. Mobile's `extractFromPhoto` intentionally left on the mock (see Goals) since the endpoint currently always 503s. `dotnet build` 0 errors, all 36 unit tests pass. No new unit tests added — the only branch worth testing (config-empty → throw) is already exercised implicitly by every call until a provider is configured; revisit when a real provider implementation lands.
- 2026-07-31 — Implemented isolated unit coverage for core Auth, Profile/Account, Category, and Wallet logic on `feature/core-api-unit-tests`. Added EF Core InMemory/Moq test infrastructure with no API or PostgreSQL connection, extracted existing deterministic category/wallet/avatar rules without changing contracts, and added handler/service/validator/state tests. Full Application unit suite: 136 passed, 0 failed, 0 skipped; solution build passes with 0 errors (2 pre-existing nullable warnings). Added dedicated unit-test catalog/gap report and generated Excel/Word artifacts: 4 groups, 20 functions, 40 passed catalog cases, 19 deferred integration/provider cases, and 18 code gaps.
- 2026-08-09 — Started Render Docker deployment support; documenting the container build/runtime contract before implementation.
- 2026-08-09 — Added a multi-stage .NET 8 `Dockerfile` for Render and a `.dockerignore` excluding local settings, credentials, build output, tests, and editor metadata. Release solution build passed with 0 errors and 6 existing nullable warnings. Docker image build could not be executed because the local Docker daemon is not running.
