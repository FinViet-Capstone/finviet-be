# Current Feature

<!-- Feature name and short description -->

**Feature: Wire up real receipt-photo OCR via Gemini (`IReceiptOcrService`)** — the mobile team
asked why photo-import in the app always shows "Tính năng đang phát triển" (503
`ocr_not_configured`). Traced to `UnconfiguredReceiptOcrService`, a deliberate placeholder added
2026-07-27 (`feature/photo-extraction-ocr-scaffold`) pending a real OCR vendor decision — no
provider had been chosen or paid for since. User compared Azure AI Document Intelligence
(prebuilt-receipt model, ~$10/1,000 pages, F0 free tier 500 pages/month, but requires
provisioning a separate Azure resource + credentials) against reusing the Gemini API key/model
this backend already has configured for score/report/chat (`GeminiOptions`/`IGeminiSdkClient`,
`Infrastructure/ExternalServices/Gemini/`) — Gemini's flash models are multimodal and accept an
image directly. Chose Gemini: zero new vendor account, zero new credentials, generous standing
free tier (no card required, no expiry), and Vietnamese-receipt support out of the box.

## Status

Implemented on branch `feature/gemini-receipt-ocr` (branched from `dev`, after the fact —
implementation was started directly on `dev` and caught before committing, same slip as the
2026-07-27 custom-category and customer-settings entries below; corrected by branching before
any commit). `dotnet build FinViet.sln` 0 errors / 0 warnings.
`FinViet.Application.UnitTests` 249/249 pass (6 new — `GeminiReceiptOcrServiceTests` covering the
not-a-receipt / zero-amount / valid-mapping / missing-date-fallback / missing-description-fallback
/ markdown-code-fence-stripped cases of the pure `ParseReceipt` parser). No live Gemini call
verified against a real receipt photo in this environment (no device/Swagger session run this
pass) — worth a live Swagger check with an actual photo before trusting field-extraction accuracy
end to end. Not committed/pushed.

## Goals

- `IGeminiSdkClient` gained a multimodal `GenerateContentAsync(model, Content, config, ct)`
  overload alongside the existing string-prompt one (`GeminiSdkClient.cs`) — purely additive, no
  existing call site changed.
- New `GeminiReceiptOcrService : IReceiptOcrService`
  (`Infrastructure/ExternalServices/Ocr/GeminiReceiptOcrService.cs`): builds a `Content` with an
  inline-bytes image `Part` (`Part.FromBytes`) plus a Vietnamese instruction `Part`, requests
  strict JSON via `ResponseSchema`/`ResponseMimeType` (same pattern as
  `GeminiAiModelClient.ClassifyAsync`'s `ClassificationSchema`), and retries across
  `Gemini:GenerationFallbackModels` on HTTP 429 the same way `GeminiAiModelClient` does. Failures
  surface as `ExternalServiceException` (502, code `ocr_provider_error`) rather than the old 503
  `ocr_not_configured` — Gemini genuinely is configured (`GeminiOptions` is
  `ValidateOnStart()`-checked at app boot), so "not configured" no longer applies; a real
  transient/quota failure is now an upstream-provider failure, matching the documented exception
  table in this repo's `CLAUDE.md`.
- The schema intentionally only asks Gemini for `isReceipt`/`amount`/`merchant`/`description`/
  `transactionDate` — no category. `IReceiptOcrService.ExtractAsync` doesn't receive `customerId`
  (unlike `TransactionExtractService`'s SMS/CSV path, which applies per-customer merchant rules +
  `IAiCategorizationService`), and changing that signature would be a breaking interface change
  the user explicitly didn't want. A photo-extracted row is returned uncategorized, same as an
  SMS/CSV row with no rule match and a failed AI categorization call — the existing review-list UX
  already handles that case.
- `DependencyInjection.cs`: swapped the `IReceiptOcrService` registration from
  `UnconfiguredReceiptOcrService` to a factory-lambda `GeminiReceiptOcrService` (its constructor is
  `internal`, matching `GeminiAiModelClient`'s convention, so it can't use the generic
  `AddScoped<TService,TImplementation>()` overload). `UnconfiguredReceiptOcrService`/`OcrOptions`
  are left in place, unregistered, as the documented fallback if a dedicated OCR vendor is wired in
  later instead.
- No controller, `IReceiptOcrService` interface, or mobile-side change needed — matches the
  scaffold's original "no controller or interface changes needed" design.

## Notes

- No `.env`, API key, commit, push, or production change made. The mobile app will keep showing
  the 503 "coming soon" message until this branch is merged and deployed — no behavior changes
  until then.
- Considered but did not add: OCR-specific usage telemetry (`IAiTelemetryRecorder`, used by
  `GeminiAiModelClient` for score/report/chat) — kept out of scope to keep this a minimal,
  focused wiring change; flagged as a reasonable follow-up if OCR usage/cost needs to be tracked
  separately from the other AI features sharing the same API key.

---

**Feature: System-wide default budget allocation ratio (UC-15, `Bucket.DefaultPct`)** — requested
by the `finviet-web` frontend team. Unlike the two previous asks, this described a concept that
didn't exist anywhere in the backend yet, not even in the schema: `Bucket` (`GET`/`PATCH
/api/buckets`) had no percentage column, and the real Needs/Wants/Savings ratio lives per-customer
on `Customer.NeedsPct/WantsPct/SavingsPct`, defaulted only by a hard-coded C# property initializer
(`= 50`/`= 30`/`= 20`) that nothing ever read back from — there was no admin-editable "system
default" to read from and no code path that consulted one when a new customer was created.

## Status

Completed (code) — `dotnet build FinViet.sln` clean (same 6 pre-existing warnings, none new).
`FinViet.Application.UnitTests` 243/243 pass, no regressions. Live Swagger verification blocked by
the same pre-existing local Postgres schema drift noted in the two entries below (`type
"app_language" already exists`) — not this session's change. Not committed/pushed.

## Goals

- New migration `V0009__bucket_default_pct.sql`: `buckets.default_pct numeric(5,2)` (nullable, per
  the frontend spec's own proposal), seeded to `needs=50, wants=30, savings=20` to match the
  previous hard-coded defaults exactly (no behavior change on day one).
- `PATCH /api/buckets/{id}` gains `defaultPct` (optional); `GET /api/buckets` returns it. Range
  validated `[0, 100]` inline in the handler (`BadRequestException` on violation) — the exact same
  pattern as `UpdateScoringCriterionCommandHandler`, no FluentValidation validator file (matching
  how this controller already had zero request validation before this change).
- **Locked down the frontend spec's open question #1 using its own recommendation**: changing the
  system default does **not** retroactively touch existing customers (they may have already
  customized their own ratio via `POST /api/profile/income-allocation` — silently overwriting that
  would be unwanted-surprise behavior). Only applies to customers created *after* the change.
- Both `RegisterCommandHandler` **and** `GoogleLoginCommandHandler` (the two places that construct
  a brand-new `Customer` row) now read `Buckets.DefaultPct` for `needs`/`wants`/`savings` and set
  them explicitly, falling back to `50`/`30`/`20` only if a row's `DefaultPct` is null (pre-V0009
  database). Google sign-up wasn't named in the frontend spec but creates new customers through
  the identical code path — leaving it on the old hard-coded default would have silently defeated
  the feature for anyone who signs up via Google instead of email/password.
- Declined the spec's open question #2 (audit log) — no `AdminAuditLog` mechanism exists anywhere
  in this backend yet (the "Feature A" reference in the spec is to a not-yet-built frontend concept,
  not existing backend infrastructure), so adding one is out of scope for this pass; flagged as a
  follow-up rather than built speculatively.
- No atomic sum-to-100 enforcement across the 3 buckets (3 independent PATCHes give the server no
  single point to check the total against) — same as `POST /api/profile/income-allocation` and
  `PATCH /api/scoring-criteria/{code}`; the frontend validates the merged set before sending.

## Notes

- `docs/api-reference.md` updated: `Buckets` section extended with `defaultPct`.
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

**Feature: Admin Announcement Broadcast (`POST/GET /api/admin/announcements`) + Category
Corrections join** — requested by the
`finviet-web` frontend team (P1 blocker for the admin "Thông báo" screen, which was running 100%
mock). `NotificationsController` only had `[Authorize(Roles = "Customer")]` inbox actions
(`GET`/`PATCH {id}/read`/`POST read-all`/device registration) — no admin-facing endpoint existed to
create a notification, and `INotificationService.NotifyAsync` requires one specific `customerId`,
with no fan-out mechanism. This adds a separate `AdminAnnouncementsController`
(`api/admin/announcements`, `[Authorize(Roles = "Admin")]`) rather than touching
`NotificationsController`, matching the existing `AdminAiController`/`api/ai` precedent of a
dedicated admin controller over combined Customer+Admin authorization on one controller.

## Status

Completed (code) — `dotnet build FinViet.sln` clean (same 6 pre-existing nullable warnings as
baseline, no new ones). **Live Swagger verification blocked**: same pre-existing local Postgres
(`FinViet_update`) schema-versions drift as the previous entry in this file (`V0001` tries to
`CREATE TYPE app_language` which already exists in that database outside `schema_versions`'s
tracking) — `dotnet run` throws `DbInitializer.RunMigrations` → `42710: type "app_language" already
exists` before the app finishes starting, for every migration including the new `V0008` added here.
Not this session's change, not touched (see `docs/database-bootstrap.md`'s adoption flow, which
requires explicit confirmed flags + a backup step, deliberately not run here). Not committed/pushed.

## Goals

- New migration `V0008__announcement_broadcasts.sql`: `announcement_broadcasts` table (`admin_id`
  FK → `admins`, `title`, `message`, `target_segment` — check-constrained to `'all'` only for now,
  `target_label`, `recipient_count`, `sent_at`). No change needed to `notifications`/`notification_type`
  — `'announcement'` was already a valid enum member, just never used from an admin-triggered path.
- `POST /api/admin/announcements`: fans out one `Notification` row (`type="announcement"`) to every
  `Customer.IsActive == true` row, batched (`AddRange`, 1000 rows/`SaveChanges`) inside one DB
  transaction together with the `announcement_broadcasts` history insert, so a broadcast is
  all-or-nothing. Returns `{ id, title, targetLabel, recipientCount, sentAt }`.
- `GET /api/admin/announcements`: paginated broadcast history, newest first, same
  `PagedResult<T>`/page-pageSize-clamping convention as `GET /api/category-corrections`.
- Open questions from the frontend spec, decided for this pass: `targetSegment` only supports
  `"all"` (validator rejects anything else — no segment-filter criteria has been chosen yet);
  `"all"` excludes deactivated (`IsActive = false`) customers; no server-side duplicate-send
  guard — left to the frontend disabling its send button while a request is pending, per the
  spec's own suggestion.
- Deliberately did not send a push notification as part of a broadcast (existing per-customer push
  goes through `NotificationService.NotifyAsync`/`INotificationPushSender` and is unrelated to
  this fan-out path).

**Also done in this same pass (frontend spec's P2 item):** `GetCategoryCorrectionsQueryHandler`
now `.Include()`s `Customer`/`Transaction`/`CorrectedCategory` and `CategoryCorrectionResponseDto`
gained `customerEmail`, `transactionDescription`, `amount`, `correctedCategoryName` — no migration,
no new endpoint, the navigation properties already existed on `CategoryCorrectionLog`. All four new
fields are null-safe (ternary on the included nav, not `!.`) since `customerId`/`transactionId`/
`correctedCategoryId` are nullable FKs on old/incomplete rows. `correctedCategoryName` prefers
`Category.NameVi`, falls back to `Category.CategoryName`.

## Notes

- `docs/api-reference.md` updated: new `Announcements — api/admin/announcements` section, and the
  `CategoryCorrectionResponseDto` line under `Category Corrections` extended with the four joined
  fields.
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

**Fix: `GET /api/users` missing transaction/wallet counts and subscription plan** — reported from
the `finviet-web` admin Users screen (`Tổng GD`/`Tổng ví` columns always showing 0), traced to
`UserResponseDto` never having carried these fields — `real/users.ts` on the frontend side has
always hardcoded `totalTransactions: 0, totalWallets: 0, plan: "free"` for exactly this reason
(see `backend-gaps.md`'s "User list is missing transaction/wallet counts and subscription plan"
entry). No schema change needed — `Transaction`/`Wallet` both already carry a direct `CustomerId`
FK, and `Customer.CustomerSubscriptions` navigation already exists.

## Status

Completed (code) — `dotnet build FinViet.sln` clean (same 6 pre-existing nullable warnings as
baseline, no new ones). Full `FinViet.Application.UnitTests` suite passes 243/243, no regressions
(no existing tests specifically covered `GetUsersQueryHandler` — same as when the endpoint was
originally built, per that entry in this file's History). **Live Swagger verification blocked**:
the local Postgres (`FinViet_update` on `localhost:5432`, credentials already in user-secrets) has
a schema-versions/DbUp drift unrelated to this change — `V0001` tries to `CREATE TYPE app_language`
which already exists in that database outside `schema_versions`'s tracking, so `dotnet run` throws
`DbInitializer.RunMigrations` → `42710: type "app_language" already exists` before the app finishes
starting. This is a pre-existing local-environment issue (not touched, not caused by this change —
see `docs/database-bootstrap.md`'s adoption flow, which this session deliberately did not run since
it requires explicit confirmed flags and a backup step). Not committed/pushed — built in an isolated
worktree (`finviet-be-users-fix`, branch `feature/admin-users-counts`, off `origin/dev`) per user
request, reported back for review before any commit.

## Goals

- `UserResponseDto` gains `totalTransactions` (int), `totalWallets` (int), `subscriptionPlanCode`
  (string, defaults `"free"`).
- `GetUsersQueryHandler` populates them via subquery counts in the existing `Select` projection:
  `_db.Transactions.Count(t => t.CustomerId == c.CustomerId)`,
  `_db.Wallets.Count(w => w.CustomerId == c.CustomerId && !w.IsDeleted)`, and the customer's most
  recent `CustomerSubscription` with `Status == "active"`, joined to `SubscriptionPlan.Code`
  (falls back to `"free"` when none).
- No new endpoint, no migration, no route/response-envelope change — same
  `GET /api/users` shape, three additive fields.
- `finviet-web`'s `src/services/real/users.ts` needs a follow-up change once this ships (stop
  hardcoding the three fields, read them from the response instead) — not done in this pass, out
  of scope for a backend-only fix.

## Notes

- Triggered directly from a `finviet-web` admin screenshot showing `Tổng GD`/`Tổng ví` stuck at 0
  for every row — traced to the frontend's own documented stub rather than a frontend bug.
- Deliberately did not attempt to fix or adopt the local database's schema drift to unblock live
  verification — that requires an explicit backup + confirmed adoption flow per this repo's own
  `docs/database-bootstrap.md`, out of scope for this fix and not something to do without asking
  first.
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

**CI/CD: GitHub Actions deploy workflow to Render** — the previous CSV-parser fix
(`fix/csv-flexible-parser`) merged into `dev`; this follow-up adds `.github/workflows/
deploy-render.yml` so pushes to `main` build, run the Domain/Application unit test suites (no DB
needed — integration tests are intentionally excluded since they require a live server/DB CI
doesn't have), and only then trigger a deploy on Render via its REST API
(`POST /v1/services/{id}/deploys`), polling the deploy status until `live`/failed/timeout so a
build failure surfaces as a red Actions run instead of silently failing on Render's side. Requires
two GitHub Actions repo secrets not yet configured by this session: `RENDER_API_KEY` (an API key
from the Render account settings) and `RENDER_SERVICE_ID` (the `srv-...` id of the
`finviet-be-7t8w` web service, visible in its Render dashboard URL). Per explicit user instruction,
Render's own native "auto-deploy on push" toggle should be turned off on the Render dashboard for
this service once this workflow is merged, to avoid double-deploys — this workflow becomes the sole
deploy trigger for `main`.

## Status

In Progress — workflow file written, not yet committed/pushed. Cannot be verified end-to-end in
this environment: no GitHub Actions run can be triggered without pushing, and the two Render
secrets aren't set. **Needs, before/after merge**: (1) add `RENDER_API_KEY`/`RENDER_SERVICE_ID` as
repo secrets (Settings → Secrets and variables → Actions), (2) turn off Render's native auto-deploy
toggle for this service, (3) watch the first real Actions run on a push to `main` to confirm the
Render API calls actually succeed (this session cannot do this itself — no Render credentials are
available here).

## Goals

- Deploy triggers only on push to `main` (plus manual `workflow_dispatch`), matching this repo's
  existing main-is-production branch convention.
- Build + unit tests (`FinViet.Domain.UnitTests`, `FinViet.Application.UnitTests`) must pass before
  the deploy step runs; a failure in either stops the deploy.
- Deploy uses the Render REST API (not a Deploy Hook URL) so the workflow can poll deploy status
  and fail the Actions run if the Render-side build/deploy itself fails, rather than declaring
  success as soon as the trigger request is accepted.
- No new secrets/credentials were entered or guessed in this session — `RENDER_API_KEY`/
  `RENDER_SERVICE_ID` must be added as GitHub Actions repo secrets by the user.

## Notes

- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission beyond what's already been asked for.

---

**Fix: flexible CSV bank-statement import (`POST /api/extract/csv`)** — triggered by a mobile
screenshot showing the "Nhập từ file CSV" screen failing with "Không tìm thấy giao dịch hợp lệ nào
trong file" (no valid transactions found) even though the file matched the screen's own advertised
requirement (a date column, a description column, and an amount column). Root cause:
`BankStatementRowParser` only ever recognized one fixed 14-column full bank-statement export layout
by hardcoded positional index (`cells[1]`/`2`/`5`/`6`/`11`/`13`), gated entirely on `cells[1]`
parsing as an integer STT. Any CSV not shaped like that — including the simple 3-column format the
app advertises — has every row silently skipped with zero telemetry (`TotalRowsScanned` stays 0, no
`ParseErrors`), so the client always falls back to a generic "nothing found" message with no way to
tell the user why. Confirmed reproducible via the existing (but stale-commented)
`ExtractAndAiTests.ExtractCsv_PlainCsv_ParsesRows` integration test, branch `fix/csv-flexible-parser`.

## Status

Completed — `dotnet build FinViet.sln` clean (6 pre-existing nullable warnings, unchanged). Full
`FinViet.Application.UnitTests` suite passes 243/243 (232 pre-existing + 11 new/extended
`BankStatementExcelParserTests`, no regressions — all 6 original tests pass byte-for-byte unchanged,
confirming the fixed-position fallback preserves prior behavior exactly). Committed (`1fcaa09`),
merged via PR #54 into `dev`.

## Goals

- `BankStatementRowParser` resolves columns by header name (Vietnamese + English aliases) when a
  recognizable header row is present, instead of only fixed positional indices; falls back to the
  existing fixed-index/STT-gated behavior when no header is recognized, so existing full-export
  behavior and all 6 existing unit tests are unaffected.
- Support a single signed `Amount` column (sign determines income/expense) in addition to the
  existing separate Debit/Credit columns, since that's the shape of the app's advertised format.
- Every non-blank candidate data row increments `TotalRowsScanned` (fixes the silent-zero-telemetry
  gap), with `ParseErrors` entries for rows that still fail to parse.
- Locale-aware amount parsing (`đ`/`VNĐ`/`₫` suffixes, Vietnamese `.`-thousands/`,`-decimal
  formatting) and an added `yyyy-MM-dd`/`yyyy/MM/dd` date format.
- Delimiter sniffing (`,`/`;`/tab) for the CSV path instead of a hardcoded comma.
- `POST /api/extract/csv` returns an explanatory message (mirroring `/sms`'s
  `BuildSmsResultMessage`) instead of a generic "success" message when `Rows` comes back empty.
- No DTO/response shape change, no new endpoint, no schema/migration change.

## Notes

- `docs/api-reference.md`'s `/csv` section is stale (still describes `ExcelDataReader`-only parsing
  from before the `fix/csv-extract-parser` branch moved CSV to `CsvHelper`) — update alongside this
  fix.
- Out of scope, flagged not fixed: non-UTF-8/non-BOM encoding fallback (Windows-1258/ANSI), and
  deduplicating the near-identical `ParseMoney`/date-parsing logic duplicated in
  `SmsTransactionParser.cs`.
- Implemented: `BankStatementRowParser.ParseRows` now runs a two-phase pass over all rows (header
  lookup by normalized/diacritic-stripped alias matching, then per-row parse using either the
  resolved named-column layout or the original fixed-position fallback); `BankStatementExcelParser`
  collects rows into a list first (needed since header detection must see the whole file, not one
  row at a time) and sniffs the CSV delimiter from the first non-blank line before constructing
  `CsvReader`. `ExtractController.ExtractCsv` gained `BuildCsvResultMessage` (mirrors the existing
  `BuildSmsResultMessage`) so a zero-row result explains the expected format instead of returning
  the old generic "File extracted successfully". Fixed the stale
  `ExtractAndAiTests.ExtractCsv_PlainCsv_ParsesRows` integration test (removed its outdated "500
  Invalid file signature" `Skip.If`, now asserts 200 + 2 rows directly) and updated
  `docs/api-reference.md`'s `/csv` section to describe the new header/fallback behavior.
- **Live end-to-end verification (Swagger / integration suite against a running server) was not
  performed** — no `ConnectionStrings:DefaultConnection` is configured in this environment (checked
  `dotnet user-secrets list`; a Postgres process is listening on 5432 but no credentials are on
  file), so the API can't be booted here. Verified instead via the unit suite: all 6 pre-existing
  `BankStatementExcelParserTests` pass unchanged (proves the fixed-position fallback is
  byte-for-byte identical to prior behavior, including that the existing tests' own header row
  `"STT","Ngay","No","Co","Dien giai","Doi ung"` now also resolves by name to the *same* column
  indices as the fallback would have used), plus 5 new tests covering the simple 3-column format in
  Vietnamese and English headers, semicolon delimiting, Vietnamese-locale amount formatting, and the
  no-header fallback path explicitly.
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

**Fix: Savings-bucket goal netting (`GET /budgets/buckets`)** — cross-repo work with
`finviet-mobile` (branch `fix/savings-goal-budget-score-integration` there; this repo's branch is
`fix/savings-bucket-goal-netting`), triggered by a mobile-side inspection of how Saving Goals,
Budgets bucket pacing, and the AI Spending Score relate to each other. Found that
`BudgetService.ComputeBucketSpentAsync` excluded `cat_savings_goal` transactions from the Savings
bucket's `Spent` figure entirely (contributions and withdrawals both), which is a real product gap
rather than a considered design line: a saving-goal contribution *is* the customer fulfilling
their Savings allocation, so excluding it made the Savings bucket effectively unfillable for any
customer who actually uses Goals — the only alternative would be logging a redundant manual
"Tiết kiệm" transaction with no connection to any goal. Confirmed safe to change now specifically
because nothing in either app currently renders the affected `Spent`/`PaceDeviation`/`PaceStatus`/
bucket-level `BudgetAdherenceScore` fields — no live behavior regresses, only a wrong number gets
corrected before anything reads it.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — `dotnet build` clean (6 pre-existing nullable warnings, unchanged), full
`FinViet.Application.UnitTests` suite passes 238/238 (235 pre-existing + 3 new
`BudgetServiceTests`, no regressions). Not committed/pushed yet.

## Goals

<!-- Goals and requirements -->

- `ComputeBucketSpentAsync`'s Savings bucket nets `cat_savings_goal` transactions —
  contribution-expense minus withdrawal-income, floored at 0 — into `Spent`, instead of excluding
  the category outright. Every other bucket's exclusion of `cat_savings_goal` is unchanged.
- Do **not** touch `CalculateFlatBudgetAdherenceScore`'s needs/wants-only exclusion of the savings
  bucket — that's a separate, correct design choice (spend-pacing framing doesn't apply
  symmetrically to savings; more saved is better, not an overspend risk) and out of scope here.
- No schema/migration change — this is a query-logic change inside an existing service method.
- Companion `finviet-mobile` work (branch `fix/savings-goal-budget-score-integration`) mirrors this
  exact formula client-side in `useBucketSpend` and the `getBudgetBuckets` mock, so mock, real, and
  the rendered UI all agree on what counts as Savings-bucket progress.

## Notes

<!-- Any extra notes -->

- Prompted by a mobile-side cross-repo inspection of Saving Goals ↔ Budget Adherence ↔ AI Spending
  Score, which found the Savings bucket's `Spent` figure was blind to goal money entirely at the
  backend level, while the mobile client's `useBucketSpend` had separately invented its own netting
  convention to compensate — three independent implementations (mock, real backend, mobile
  client-side convenience) that only coincidentally agreed most of the time. This fix makes the
  backend the single authoritative source; mobile fixes its own bug (clamping the *whole* Savings
  accumulator to 0 instead of just the goal-net component, which had been silently erasing real
  non-goal savings in months with a large goal withdrawal) and mirrors this formula rather than
  inventing its own.
- Also confirmed, not touched: `allocationPct`/`AllocationCap` are already correct server-side
  (`AllocationCap = income × pct / 100m`, `AllocationPct` returned as a raw 0–100 percent by
  design, matching `Customer.NeedsPct` storage) — the "100× cap bug" reported from the mobile side
  turned out to be a client-side passthrough bug in `finviet-mobile`'s real-mode service, not a
  backend defect. Nothing changed here as a result.
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

**Fix: notification device registration race (`PUT /api/notifications/devices`)** — surfaced by a
live Sentry issue (`Microsoft.EntityFrameworkCore.DbUpdateException`, last seen Aug 15 11:43 AM),
found while triaging a batch of production Sentry errors from the `finviet-web` side.
`NotificationService.RegisterDeviceAsync` does a non-atomic check-then-insert on
`(CustomerId, InstallationId)`: two near-simultaneous registration calls for the same
customer+installation (e.g. an app-foreground retry) both see "not found" and both try to insert,
so the second `SaveChangesAsync` throws on `uq_notification_devices_customer_installation`,
unhandled, surfacing as a 500.

## Status

Completed — `dotnet build` clean, full `FinViet.Application.UnitTests` suite passes 229/229
(including all pre-existing `NotificationServiceTests`, no regressions). Not committed/pushed yet.

## Goals

- Keep the existing stale-token-owner removal block (the separate `uq_notification_devices_token`
  constraint) untouched — out of scope, not what Sentry captured.
- No schema change — reuses the existing constraint from `V0004__notification_devices.sql`.

## Notes

- Investigation prompted from the `finviet-web` side while triaging 3 Sentry issues shared by the
  user; two other issues from that same batch: `SubscriptionRenewalScheduler`'s enum/text cast bug
  (already fixed separately, see History) and a CSV-extract parser gap (`fix/csv-extract-parser`,
  separate branch/session).
- **Implementation approach changed from the original plan**: originally planned as an atomic
  `INSERT ... ON CONFLICT ... DO UPDATE` raw SQL statement (matching `IdempotencyStore.cs`'s
  idiom). Discovered mid-implementation this breaks `FinViet.Application.UnitTests` —
  `ExecuteSqlInterpolatedAsync`/raw SQL throws `InvalidOperationException` against the EF Core
  InMemory provider `TestDbContextFactory` uses for all unit tests (`Relational-specific methods
  can only be used when the context is using a relational database provider`). Switched to
  catch-`DbUpdateException`-and-retry-as-update instead, matching the exact existing
  `IsUniqueViolation` idiom already used in `RegisterCommandHandler.cs` (and mirrored in
  `CreateAdminCommandHandler.cs` per an earlier History entry) — checks
  `ex.InnerException?.Message` for `"23505"`, scoped to
  `"uq_notification_devices_customer_installation"` specifically since the *other* unique
  constraint on this table (`uq_notification_devices_token`) can also fire here and is already
  handled separately by the pre-existing stale-token-owner removal block above it. This keeps the
  fix provider-agnostic and consistent with established codebase convention, at the cost of one
  extra DB round trip only on the rare actual-conflict path (the common case — no conflict — is
  unchanged: one query, one insert).
- No synthetic-concurrency unit test was added to force the catch-block path itself — the two
  precedent handlers in this codebase with the identical pattern
  (`RegisterCommandHandler`/`CreateAdminCommandHandler`) also ship without one, since forcing a
  true race against the InMemory provider deterministically would need test-only refactoring
  beyond this fix's scope. Correctness of the retry path was verified by code review against the
  exact Sentry stack trace (same `DbUpdateException` → `PostgresException 23505` shape) rather than
  a live Postgres in this environment (no local Postgres connection string configured here).
- No `.env`, API key, production cutover, production-data change, commit, or push without explicit
  permission.

---

Two independent features landed around the same time: (1) **Admin account management** — let an
already-logged-in admin create other admin accounts and change their own password, instead of
every `Admins` row requiring a raw SQL insert. Companion work in `finviet-web` on branch
`feature/admin-account-management` (same branch name, different repo) covers the frontend
(create/list-admins screen, real login + 2FA enrollment, real change-password UI). (2) **VNPay
auto-renewing premium subscriptions** + admin `SubscriptionPlan` CRUD, built independently from the
existing unmerged `origin/dunglt` branch (a different, manual SePay-QR payment flow) per explicit
user decision. Core guarantee: `CustomerSubscription.LockedPrice` is snapshotted at subscribe time
and used for every renewal charge, so admins can edit `SubscriptionPlan.Price` in place at any time
without silently repricing existing subscribers.

## Status

<!-- Not Started | In Progress | Completed -->

Both implemented. VNPay subscriptions: verified via `dotnet build`/`dotnet test` (real
Postgres/VNPay sandbox not available in this environment — see Notes), branch
`feature/vnpay-subscriptions`, already merged into `dev`. Admin account management: verified via
`dotnet build` plus real end-to-end browser testing against the deployed
`https://finviet-be-7t8w.onrender.com` instance (see `finviet-web`'s `context/current-feature.md`
for the full verification trail, including two real bugs found and fixed there), branch
`feature/admin-account-management`.

## Goals

<!-- Goals and requirements -->

**Admin account management:**
- `POST /api/auth/admin-change-password` (Admin role): lets an authenticated admin change their
  own password given the current one, mirroring the existing Customer `/change-password` flow.
- New `AdminsController` (`api/admins`, Admin role): `GET /` lists all admins; `POST /` creates a
  new admin with a password the creating ("master") admin types in directly — no auto-generated
  temp password, per explicit product decision (a new admin can change it themselves once
  `admin-change-password` exists).
- No `Admins` table schema change needed — `Username`/`PasswordHash`/`Email`/`CreatedAt` already
  cover both features, so no new SQL migration.

**VNPay subscriptions:**
- Migration `V0006__vnpay_subscriptions_payments.sql`: `subscription_plans.is_active`/
  `billing_interval_months`, `customer_subscriptions.locked_price`/`auto_renew`/
  `next_billing_date`/retry fields, new `payments` table + `payment_status` enum.
- `ExternalServices/VNPay/`: options, HMAC-SHA512 sign/verify helper, client.
- CQRS: customer subscribe (returns VNPay redirect URL), VNPay return-URL handler (informational
  only), VNPay IPN handler (authoritative, idempotent), admin `SubscriptionPlan` CRUD.
- `SubscriptionRenewalScheduler` background job: claim/lease pattern (`FOR UPDATE SKIP LOCKED`),
  1/3/7-day dunning retry schedule, always charges `LockedPrice`, never live `SubscriptionPlan.Price`.
- `finviet-web` companion: JWT propagation (blocking prerequisite), numeric price type change,
  `real/plans.ts` wired to the new admin endpoints.

## Notes

<!-- Any extra notes -->

- This started as a question about how 2FA setup works for FinViet Admin; wiring real 2FA/login
  itself stayed out of scope at first. What got greenlit for implementation was the admin
  account-management gap it surfaced — and later, real 2FA/login wiring too (see `finviet-web`'s
  `context/current-feature.md` for the full chain of reasoning).
- Built in an isolated git worktree (`finviet-be-admin-mgmt`, branch `feature/admin-account-management`
  off `origin/dev`) rather than the primary checkout, since the primary `finviet-be` working
  directory was mid-flight on unrelated concurrent work (`feature/admin-list-endpoints`, building
  `GET /api/users` and `GET /api/category-corrections` — confirmed non-overlapping: that's the
  `Customers` table, this is the separate `Admins` table).
- 2026-08-15 — Implemented: `ChangeAdminPasswordCommand`/`Validator`/`Handler` (mirrors the
  existing Customer `ChangePasswordCommandHandler`) + `POST /api/auth/admin-change-password` on
  `AuthController`, reading the caller's `AdminId` via the existing `User.GetCustomerId()` claim
  extension (admin JWTs already stamp `AdminId` into that same `"customerId"` claim, a pre-existing
  quirk from `AdminLoginCommandHandler`). New `CreateAdminCommand`/`Validator`/`Handler`
  (`Features/Admins/Commands/CreateAdmin/`) + new `AdminsController` (`GET /api/admins` — flat
  unpaginated list, `POST /api/admins` — create, uniqueness pre-check on username/email plus a
  `DbUpdateException` unique-violation backstop mirroring `RegisterCommandHandler`'s pattern).
  `docs/api-reference.md` updated (new Admins section + admin-change-password row/detail in Auth).
  `dotnet build` passed with 0 errors (6 pre-existing nullable warnings, unchanged). No unit tests
  added — both handlers are thin CRUD mirrors of already-tested siblings (`ChangePasswordCommandHandler`,
  `RegisterCommandHandler`) with no new pure logic branches. Verified end-to-end for real: a
  `master` admin was seeded directly on the deployed Render Postgres (real credentials, not a
  fixture) and used to exercise the full real login → first-login 2FA enrollment → dashboard flow
  from the `finviet-web` side, computing real RFC 6238 TOTP codes — confirming these endpoints work
  correctly against production-shaped data, not just a mock. Committed (`4f11952`).
- Migration renumbered `V0004` → `V0006` (VNPay branch): `origin/dev` claimed `V0004` for
  `notification_devices` and `V0005` for the scoring-criteria seed while that branch was in
  progress — resolved by renumbering to the next free slot when merging `origin/dev` in, rather
  than contesting either already-landed number.
- VNPay sandbox/merchant credentials for recurring billing are not available in this environment;
  `ChargeByTokenAsync`'s exact request/response shape is provisional pending real VNPay docs. Code
  and unit tests (hash sign/verify, dunning schedule, state transitions) can be completed and
  verified now; live end-to-end payment verification is blocked until real credentials exist.
- No production database action without explicit permission.

- 2026-08-14 — Started after confirming current DELETE reverses every contribution/withdrawal,
  removes generated transactions and ledger rows, and physically deletes the goal. Approved
  replacement is locked zero-balance soft archive with preserved read-only history.
- 2026-08-15 — Extended `SavingGoal_Lifecycle_Works` to prove archive preserves both linked
  transaction directions through the paged collection endpoint and leaves monthly gross income and
  expense unchanged; cleanup now removes those isolated transaction fixtures before the test wallet.
  Application tests pass 200/200, the solution and API integration-test project compile, and
  `git diff --check` is clean. The live integration test was not executed because no prepared
  non-production API/database was explicitly approved; no commit, push, deployment, or database
  operation run.

- The reported response was a formatting meta-instruction rather than a financial answer. The exact
  text does not exist in repository prompts; Google.GenAI 1.17.0 documents that `response.Text`
  concatenates every text part from the first candidate, while each part exposes a `Thought` marker.
- No automatic cleanup of historical chat rows is included; this change protects new responses.
- User selected the stable local database dump as the schema source of truth, DbUp as the future
  migration engine, and reference data plus configured admin as the production bootstrap policy.
- The baseline must include the current V25/Gemini tables, all mapped PostgreSQL enums, `pgcrypto`,
  `vector`, `rag_chunk.embedding = vector(768)`, and the HNSW cosine index.
- Full database dumps, schema-diff artifacts, `.env`, passwords, and provider credentials must remain
  outside Git and the Docker build context.
- Once released, baseline scripts are immutable; future migrations continue after the current
  `V0003` and use zero-padded names.
- Existing/restored databases require an explicit confirmed adoption command after schema fingerprint
  validation; normal startup never marks an unknown schema current.
- 2026-08-13: two other branches merged into this one — `feature/sentry-backend-setup` (Sentry
  error tracking: exception middleware, csproj package, Program.cs wiring) and, riding along on
  that branch, `docs/api-reference-health-status` (documented the `GET /`/`GET /health` endpoints,
  removed the now-resolved `docs/10-08-2026-be-todos.md`). No overlap with the database-baseline
  work itself — different files, clean auto-merge apart from this Notes/History section.
- Gemini Flash safe-copilot context (from `feature/gemini-safe-copilot`, already on `dev` before
  this branch started): official `Google.GenAI` provider, 768-dim embeddings, per-customer AI
  preferences, owner-scoped categorization, customer-owned chat sessions, durable rate limits. Solution
  build and 183 Application unit tests passed as of 2026-08-11; live Gemini-key verification and RAG
  re-index remained outstanding at that time. Gemini API keys are supplied only via .NET user-secrets
  or environment variables, never committed.
- No `.env`, API key, live quota exhaustion, production cutover, production-data change,
  or RAG re-index without separate explicit permission.
- 2026-08-15 — Started. Full design plan (migration, entities, VNPay client, CQRS features,
  renewal job, frontend wiring) approved by the user after a multi-turn scoping discussion:
  VNPay chosen over SePay/Momo/Stripe, true auto-renewal chosen over manual pay-per-period,
  explicitly independent of `origin/dunglt`.
- 2026-08-15 — Implemented, in an isolated `finviet-be-vnpay` worktree (branch
  `feature/vnpay-subscriptions`) to avoid disturbing another agent's uncommitted
  `fix/scoring-weights` work in the main `finviet-be` checkout. `V0004__vnpay_subscriptions_payments.sql`
  adds `subscription_plans.is_active`/`billing_interval_months`, `customer_subscriptions.locked_price`/
  `auto_renew`/`next_billing_date`/`next_retry_at`/`retry_count`/`renewal_claimed_at`/
  `vnpay_card_token`/`canceled_at`, a new `payments` table (audit trail of every VNPay charge
  attempt, `payment_status` enum, `uq_payments_one_pending_per_subscription` double-charge
  backstop). New `Payment` entity + `PaymentStatus` CLR enum registered via the existing
  `MapEnum`/`PgEnumStringConverter` convention. New `ExternalServices/VNPay/`
  (`VNPayOptions`, `VNPayHashHelper` implementing VNPay's documented HMAC-SHA512 sign/verify
  algorithm exactly — sorted, URL-encoded `vnp_*` params, `FixedTimeEquals` comparison —
  `IVNPayClient`/`VNPayClient`, empty-credentials-disables-at-point-of-use like SePay's
  `WebhookApiKey`). New `Features/Subscriptions/` CQRS: `SubscribeToPlanCommand` (idempotent,
  snapshots `plan.Price` onto the pending `Payment`), `GetVNPayReturnStatusQuery`
  (informational-only browser-return handler), `ProcessVNPayIpnCommand` (authoritative, never
  throws, row-locks the `Payment` under an explicit transaction, delegates state transitions to a
  new shared `ISubscriptionPaymentResultService` so the IPN handler and the renewal job can't
  drift apart — on an `initial` success this is where `CustomerSubscription.LockedPrice` gets
  snapshotted from `payment.Amount`, never re-read from `SubscriptionPlan.Price`). New
  `Features/SubscriptionPlans/` CQRS for admin CRUD (`Update` deliberately excludes `Code` and
  freely edits `Price` in place — safe specifically because of the `LockedPrice` guarantee;
  `Discontinue` only flips `IsActive`, never cascades to subscriptions). New
  `SubscriptionRenewalScheduler` background job: hourly poll, `FOR UPDATE SKIP LOCKED` claim/lease
  (15-minute staleness) so a slow VNPay call never blocks other workers, 1/3/7-day dunning retry
  schedule (`active` → `past_due` from the 2nd failure → `canceled`/`AutoRenew=false` on the 4th,
  ~11-day window), reused `WeeklyReportScheduler`'s VN-timezone resolution pattern. New
  `SubscriptionsController` (customer-facing subscribe/return/IPN) and
  `AdminSubscriptionPlansController` (`[Authorize(Roles = "Admin")]` CRUD). Also fixed
  `AdminLoginCommandHandler` to read a new `Jwt:AdminAccessTokenExpiryMinutes` (default 480 = 8h)
  instead of the shared 15-minute customer expiry — needed by `finviet-web`'s JWT-propagation
  companion change. 18 new unit tests (`VNPayHashHelperTests`, `SubscriptionRenewalDunningTests`,
  `SubscriptionPaymentResultServiceTests` — the last explicitly proves a payment resolved after
  the catalog price already changed still locks in the amount actually charged, not the live
  price) all pass; full Application suite 218/218, Domain suite 1/1, `dotnet build` 0 errors.
  **Known, called-out gaps, not silently papered over**: no VNPay sandbox/merchant credentials
  exist in this environment, so `ChargeByTokenAsync`'s exact recurring-charge request/response
  field names are provisional pending real VNPay docs, and live end-to-end payment verification
  (QR/redirect → pay → IPN → activation → simulated renewal) has not run — this feature should not
  be considered fully done until that happens. No commit, push, or merge performed yet at this
  point.
- 2026-08-15 — Committed locally (`f57c7f2`) at the user's request, then merged the latest
  `origin/dev` in to prepare for opening a PR. `origin/dev` had moved forward by 8 merged branches
  since this branch started (Gemini free-tier model order, and 5 completed `backend-gaps.md`
  items: scoring weights, bucket admin CRUD, category icon upload, RAG document preview, admin
  list endpoints, plus notification-delivery). Two conflicts: `FinVietDbContext.cs` (both this
  branch and `origin/dev` added a new `DbSet`/entity-config block in the same region — resolved by
  keeping both, `NotificationDevice` and `Payment`, as separate blocks) and this file (resolved
  per this repo's own established convention — see the entries below — keeping the active feature
  as the header and preserving every branch's History entries). `DependencyInjection.cs` merged
  automatically with no conflict. Migration renumbered `V0004` → `V0006` (see Notes above).
  Rebuilt clean after merging, all 218 Application tests still pass.
- 2026-08-15 — Started free-tier model-order fix after provider telemetry proved HTTP 429 failover was
  working but stopped on the next model's non-429 error. Approved scope: prioritize stable
  `gemini-3.1-flash-lite`, retain 429-only failover, add privacy-safe HTTP-status telemetry, and leave
  embedding/RAG unchanged.
- 2026-08-15 — Implemented `gemini-3.1-flash-lite` as the primary generation model with the ordered
  free-tier fallback chain `gemini-3-flash-preview` → `gemini-3.6-flash` → `gemini-2.5-flash` →
  `gemini-2.5-flash-lite`; removed `gemini-2.5-pro`, retained 429-only failover, and added numeric
  non-429 status metadata without provider messages. Focused Gemini tests passed 29/29, all Application
  tests passed 201/201, solution build passed with 0 warnings/errors, and `git diff --check` is clean.
  No live Gemini call, deploy, provider configuration change, billing change, RAG re-index, commit, or
  push was performed.
- 2026-08-15 — Merged the latest `origin/dev` into the Gemini fix branch after GitHub reported it
  could not merge automatically. The only conflict was this living feature document; resolved by
  retaining the complete upstream notification/backend-gap history and making the Gemini fix the
  current header at the time.
- 2026-08-15 — Completed item 1 (scoring weights) on branch `fix/scoring-weights`: new migration
  `V0004__seed_scoring_criteria.sql` (briefly renumbered to `V0005` and back — see below) seeds
  `scoring_criteria` (previously empty since `V0002` deliberately excluded it) with the weights
  that were hardcoded in `SpendingScoreService.ComputeAsync`; that method now reads
  `WeightWeekly`/`WeightMonthly` from the table instead. New `ScoringCriteriaController`
  (`GET`/`PATCH /api/scoring-criteria`, Admin role) backed by
  `GetScoringCriteriaQuery`/`UpdateScoringCriterionCommand`. `dotnet build` 0 errors, all 200
  Application unit tests pass. Live-verified against a local PostgreSQL instance: migration
  applied cleanly, `GET` returns seeded rows, `PATCH` persists and increments `Version`,
  out-of-range weight returns 400, unknown `code` returns 404, unauthenticated returns 401; test
  change reverted to defaults afterward. `docs/api-reference.md` updated (score-weights note +
  new Scoring Criteria section).
- 2026-08-15 — Renamed the seed migration from `V0004` to `V0005__seed_scoring_criteria.sql` per
  user instruction, after the other agent's VNPay subscription work claimed `V0004`. Updated the
  two doc references (`docs/api-reference.md`, this file) accordingly; no re-verification needed
  since only the filename changed, not the SQL content.
- 2026-08-15 — Reverted the rename: user confirmed this work finished first, so it keeps `V0004`
  and the VNPay work renumbers instead when it lands. Renamed back to
  `V0004__seed_scoring_criteria.sql`, restored the two doc references, rebuilt, and re-verified the
  migration applies cleanly against the local database. Then merged all 5 branches into `dev`
  locally per explicit user instruction (not pushed) — see the summary entry below.
- 2026-08-15 — Completed item 2 (bucket admin CRUD) on branch `fix/bucket-admin-crud`: new
  `GetBucketsQuery`/`UpdateBucketCommand` + `BucketsController` (`GET`/`PATCH /api/buckets`, Admin
  role). `UpdateBucketCommandHandler` deliberately does not check `Bucket.IsLocked` — admin can
  edit every bucket including the locked `savings` row, per the product decision recorded in
  `backend-gaps.md` item 2. No migration needed (table and rows already existed). `dotnet build` 0
  errors, all 200 Application unit tests pass. Live-verified: `GET` lists all 3 buckets, `PATCH` on
  the locked `savings` bucket succeeds and persists, unknown id returns 404, unauthenticated
  returns 401; test change reverted afterward. `docs/api-reference.md` updated (new Buckets
  section after Categories).
- 2026-08-15 — Completed item 3 (category icon upload) on branch `feature/category-icon-upload`:
  new `ICategoryIconService`/`CategoryIconService` mirroring `AvatarService`'s pattern (writes to
  `wwwroot/category-icons/`, served via the already-wired `UseStaticFiles()`); new
  `CategoryIconValidationRules` (SVG-only, 1 byte–200 KB, must start with `<svg`/`<?xml`, rejects
  `<script`/`on*=` as a defense-in-depth XSS guard); new `POST /api/categories/icons` (Customer) on
  `CategoriesController`. `CreateCustomCategoryRequest` gained `Icon`;
  `CategoryService.CreateCustomCategoryAsync` now persists it (was previously hardcoded to `null`
  with a "stays device-local" comment — that decision is reversed) and rejects any value not
  prefixed `/category-icons/` so a client can't smuggle an arbitrary external URL into a
  frontend-rendered field.
  **Bug found and fixed in this new code**: `AppSettings:WebRootPath` is configured as `""` (empty
  string, not absent) in `appsettings.json`, so the copied `?? fallback` pattern from
  `AvatarService` never triggered — files wrote relative to the process's current directory
  instead of `wwwroot`. Fixed with an explicit `string.IsNullOrWhiteSpace` check in
  `CategoryIconService`; `AvatarService` itself has the same latent bug but was left untouched
  (out of scope) — flagged separately.
  **Pre-existing unrelated bug found while verifying, not fixed (out of scope)**: every
  `POST /api/categories/custom` call fails with a 500 regardless of this change — the generated id
  (`"custom_" + Guid.NewGuid()`, 43 chars) exceeds `categories.id`'s `varchar(40)` column. Flagged
  as a separate task (likely fix: `Guid.NewGuid().ToString("N")`, 32 chars, fits).
  `dotnet build` 0 errors, all 200 Application unit tests pass. Live-verified everything in this
  feature's own scope: icon upload accepts a valid SVG and serves it back at the returned URL
  (200), rejects wrong content-type and `<script>`-bearing SVGs (400), and the external-URL
  rejection on `POST /custom` fires correctly (400) — full category creation with the icon
  attached couldn't be end-to-end verified because of the unrelated id-length bug above.
  `docs/api-reference.md` updated (Categories table + new `POST /icons` section).
- 2026-08-15 — Completed item 4 (RAG document preview) on branch `feature/rag-document-preview`:
  `PdfDocumentIngestionService.IngestPdfAsync` now buffers the upload into memory once, validates
  the `%PDF` magic number (400 otherwise — no format check existed before), writes the raw bytes
  to `wwwroot/documents/{id}.pdf` (served via the already-wired `UseStaticFiles()`), and sets
  `RagDocument.Uri` accordingly (previously always null for PDFs — the file was discarded after
  text extraction). New `IRagDocumentQueryService`/`RagDocumentQueryService` (direct DbContext
  query, matching the AI feature area's existing non-MediatR convention) backs a new
  `GET /api/ai/documents` on `AdminAiController`, returning
  `{ id, title, sourceType, uri?, createdAt, chunkCount }` newest first, no pagination (low,
  admin-curated volume). `dotnet build` 0 errors, all 200 Application unit tests pass.
  Live-verified: non-PDF upload rejected (400 wrong magic bytes); a hand-crafted real PDF passed
  magic-byte validation, text extraction, and disk-write (confirmed the file landed at
  `wwwroot/documents/{guid}.pdf`) — ingestion then failed at the Gemini embedding call itself
  (`ai_provider_unavailable`), a pre-existing external dependency unreachable in this sandbox, not
  related to this change. Verified the list endpoint and static serving independently by
  inserting a test `rag_document` row directly: `GET /api/ai/documents` returned it with the
  correct shape and the file served at its `uri` with 200; test row and file removed afterward.
  `docs/api-reference.md` updated (`POST /documents` validation note + new `GET /documents`
  section).
- 2026-08-15 — Completed item 5 (admin list endpoints), the last of the five, on branch
  `feature/admin-list-endpoints`: new `GET /api/category-corrections` (`CategoryCorrectionQueryDto`
  with `categoryId?`/`createdAtFrom?`/`createdAtTo?`/`page`/`pageSize`, backed by
  `GetCategoryCorrectionsQuery` reading `CategoryCorrectionLog` directly via `FinVietDbContext`,
  matching the CategoryService/SpendingScoreService direct-DbContext convention rather than adding
  a one-off repository interface) and new `GET /api/users` (`UserQueryDto` with `search?` +
  paging, backed by `GetUsersQuery` reading `Customer`, excluding soft-deleted rows, no sensitive
  fields in the response). Both follow `TransactionRepository.GetPagedAsync`'s exact pattern:
  `page`/`pageSize` clamped to `[1,100]`/default 20, UTC start-of-day/exclusive-next-day date
  range, `PagedResult<T>`. `dotnet build` 0 errors, all 200 Application unit tests pass.
  Live-verified: users list returns all 4 seeded accounts with correct paging metadata; `search`
  filters correctly; unauthenticated returns 401; category-corrections returns empty against a
  clean table, then correctly filters by `categoryId` and `createdAtFrom` once two test rows were
  inserted directly (no real correction rows existed to exercise otherwise); all test rows removed
  afterward. `docs/api-reference.md` updated (new Category Corrections + Users sections).
  **All 5 backend-gaps.md items (excluding subscription/payment) are now complete**, each
  committed on its own branch.
- 2026-08-15 — Merged all 5 branches into `dev` locally, per explicit user instruction, in order
  1→5 (`fix/scoring-weights`, `fix/bucket-admin-crud`, `feature/category-icon-upload`,
  `feature/rag-document-preview`, `feature/admin-list-endpoints`). Every merge after the first
  conflicted in this file (`context/current-feature.md`) since each branch independently rewrote
  the same Status/Goals/Notes/History header off the original `dev` baseline — resolved by hand
  each time, keeping every branch's unique History entry and consolidating Status/Goals/Notes into
  one accurate final state. `docs/api-reference.md` merged cleanly every time (each branch's
  documentation additions landed in different, non-overlapping sections).
  `src/FinViet.Infrastructure/DependencyInjection.cs` merged cleanly once (items 3 and 4 both added
  a registration line, in different parts of the file). `dotnet build` passed with 0 errors after
  every merge commit. Not pushed to `origin/dev`.
- 2026-08-14 — Started Gemini thought-response filtering after a current-month budget question returned a formatting meta-instruction. Approved scope: filter `Part.Thought` at the SDK boundary, request no thought output, treat thought-only output as provider unavailable, preserve HTTP 429-only model fallback, and add provider/persistence regressions without cleaning historical rows.
  text does not exist in repository prompts; Google.GenAI 1.17.0 documents that `response.Text`
  concatenates every text part from the first candidate, while each part exposes a `Thought` marker.
- No automatic cleanup of historical chat rows is included; this change protects new responses.
- User selected the stable local database dump as the schema source of truth, DbUp as the future
  migration engine, and reference data plus configured admin as the production bootstrap policy.
- The baseline must include the current V25/Gemini tables, all mapped PostgreSQL enums, `pgcrypto`,
  `vector`, `rag_chunk.embedding = vector(768)`, and the HNSW cosine index.
- Full database dumps, schema-diff artifacts, `.env`, passwords, and provider credentials must remain
  outside Git and the Docker build context.
- Once released, baseline scripts are immutable; future migrations continue after the current
  `V0003` and use zero-padded names.
- Existing/restored databases require an explicit confirmed adoption command after schema fingerprint
  validation; normal startup never marks an unknown schema current.
- 2026-08-13: two other branches merged into this one — `feature/sentry-backend-setup` (Sentry
  error tracking: exception middleware, csproj package, Program.cs wiring) and, riding along on
  that branch, `docs/api-reference-health-status` (documented the `GET /`/`GET /health` endpoints,
  removed the now-resolved `docs/10-08-2026-be-todos.md`). No overlap with the database-baseline
  work itself — different files, clean auto-merge apart from this Notes/History section.
- Gemini Flash safe-copilot context (from `feature/gemini-safe-copilot`, already on `dev` before
  this branch started): official `Google.GenAI` provider, 768-dim embeddings, per-customer AI
  preferences, owner-scoped categorization, customer-owned chat sessions, durable rate limits. Solution
  build and 183 Application unit tests passed as of 2026-08-11; live Gemini-key verification and RAG
  re-index remained outstanding at that time. Gemini API keys are supplied only via .NET user-secrets
  or environment variables, never committed.
- No `.env`, API key, commit, push, live quota exhaustion, production cutover, production-data change,
  or RAG re-index without separate explicit permission.

## History

<!-- Keep this updated. Earliest to latest -->
- 2026-08-15 — Fixed on branch `fix/subscription-renewal-status-enum-cast`: one-line change in
  `SubscriptionRenewalScheduler.cs`'s claim query, casting the `Active`/`PastDue` string
  parameters to `::subscription_status` so the `status IN (...)` comparison against the Postgres
  enum column no longer throws. `dotnet build` 0 errors (6 pre-existing nullable warnings,
  unchanged). **Live-verified two ways**: (1) booted the API against the real local Postgres —
  before the fix this logged `fail: ... SubscriptionRenewalScheduler batch failed` /
  `operator does not exist: subscription_status = text` on every poll (reproduced earlier in this
  session); after the fix, the same poll runs clean with no error (0 due subscriptions exist in
  this local DB, so nothing to claim, but the query itself no longer throws). (2) Not satisfied
  with "doesn't error on an empty set" alone — inserted a real `subscription_plans` row and a
  `customer_subscriptions` row (`status='active'`, `auto_renew=true`,
  `next_billing_date = CURRENT_DATE - 1`, genuinely due) inside a transaction, ran the exact fixed
  query pattern directly against Postgres, confirmed it matched exactly 1 row, then `ROLLBACK` —
  no test data persisted. No production database action; local dev-only Postgres only. Not
  committed/pushed yet.
- 2026-08-15 — Implemented the admin analytics endpoint on branch `feature/admin-analytics-endpoint`:
  new `AnalyticsController` (`api/analytics`, Admin role) with `GET /summary`
  (`AdminAnalyticsSummaryDto`) and `GET /trend?metric=&days=` (`DailyMetricDto[]`), backed by
  `GetAnalyticsSummaryQueryHandler`/`GetAnalyticsTrendQueryHandler` querying `FinVietDbContext`
  directly (`AsNoTracking()`, no repository interface, matching `GetUsersQueryHandler`'s style).
  `dotnet build` 0 errors. New `AnalyticsTests.cs` integration test class (4 tests, follows
  `BudgetTests.cs`'s `[SkippableFact]`/`RequireServer()` pattern) — these self-skip when run via
  `dotnet test` because the shared `ApiTestFixture`'s hardcoded seed-customer login
  (`tkv2003@gmail.com`) doesn't match this local database's actual seeded customer, an existing
  fixture-credential gap unrelated to this change. **Live-verified manually instead**: booted the
  API against the real local Postgres, logged in as the real seeded admin
  (`admin`/`Admin@123456`, this environment's dev-seed default), and called both endpoints for
  real over HTTP: `GET /summary` returned real counts (4 customers, 394 transactions, 3 wallets, 0
  budgets, 0 premium subscriptions — correctly 0 since `subscription_plans` has no rows in this
  DB, not an error); `GET /trend?metric=signups&days=7` returned exactly 7 zero-filled points
  matching the real admin-account creation dates; `GET /trend?metric=transactions&days=9999`
  correctly clamped to exactly 30 points; an unrecognized `metric` value correctly fell back to
  signups counts; a request with no token correctly returned 401. Resolved the matching
  `KnownGapsTests.Admin_SystemAnalytics_Endpoint` skip placeholder (partial — analytics now exist,
  AI call-volume/cost still has no admin-facing read endpoint, noted explicitly). Updated
  `docs/api-reference.md` (new Analytics section after Users). **Noted, not this feature's
  concern**: `SubscriptionRenewalScheduler`'s background job logged a pre-existing, unrelated
  error while the server was up (`operator does not exist: subscription_status = text` — a Postgres
  enum/text comparison needing an explicit cast somewhere in that scheduler's query) — flagged for
  a separate fix, does not affect the analytics endpoints. Not committed/pushed yet.
- 2026-08-15 — Saving-goal archive follow-up (branch `fix/saving-goal-archive`, already merged
  into `dev` before this feature started): changed `DELETE /api/saving-goals/{id}` from physical
  deletion to a zero-balance-only soft archive (422 `goal_balance_must_be_withdrawn` otherwise),
  preserving all linked transactions/contributions and wallet balances; added active/archived list
  filtering and read-only archived detail/ledger access; fixed truthful goal field reporting and
  explicit PATCH deadline semantics. Extended `SavingGoal_Lifecycle_Works` to prove the paged
  transaction list and monthly summary are unaffected by archiving. Application tests 200/200,
  solution and integration-test project compiled. Live integration-test execution against a
  non-production DB was not run (no environment explicitly approved for it).
- 2026-08-15 — Started free-tier model-order fix after provider telemetry proved HTTP 429 failover was
  working but stopped on the next model's non-429 error. Approved scope: prioritize stable
  `gemini-3.1-flash-lite`, retain 429-only failover, add privacy-safe HTTP-status telemetry, and leave
  embedding/RAG unchanged.
- 2026-08-15 — Implemented `gemini-3.1-flash-lite` as the primary generation model with the ordered
  free-tier fallback chain `gemini-3-flash-preview` → `gemini-3.6-flash` → `gemini-2.5-flash` →
  `gemini-2.5-flash-lite`; removed `gemini-2.5-pro`, retained 429-only failover, and added numeric
  non-429 status metadata without provider messages. Focused Gemini tests passed 29/29, all Application
  tests passed 201/201, solution build passed with 0 warnings/errors, and `git diff --check` is clean.
  No live Gemini call, deploy, provider configuration change, billing change, RAG re-index, commit, or
  push was performed.
- 2026-08-15 — Merged the latest `origin/dev` into this fix branch after GitHub reported it could not
  merge automatically. The only conflict was this living feature document; resolved by retaining the
  complete upstream notification/backend-gap history and making the Gemini fix the current header.
- 2026-08-15 — Completed item 1 (scoring weights) on branch `fix/scoring-weights`: new migration
  `V0004__seed_scoring_criteria.sql` (briefly renumbered to `V0005` and back — see below) seeds
  `scoring_criteria` (previously empty since `V0002` deliberately excluded it) with the weights
  that were hardcoded in `SpendingScoreService.ComputeAsync`; that method now reads
  `WeightWeekly`/`WeightMonthly` from the table instead. New `ScoringCriteriaController`
  (`GET`/`PATCH /api/scoring-criteria`, Admin role) backed by
  `GetScoringCriteriaQuery`/`UpdateScoringCriterionCommand`. `dotnet build` 0 errors, all 200
  Application unit tests pass. Live-verified against a local PostgreSQL instance: migration
  applied cleanly, `GET` returns seeded rows, `PATCH` persists and increments `Version`,
  out-of-range weight returns 400, unknown `code` returns 404, unauthenticated returns 401; test
  change reverted to defaults afterward. `docs/api-reference.md` updated (score-weights note +
  new Scoring Criteria section).
- 2026-08-15 — Renamed the seed migration from `V0004` to `V0005__seed_scoring_criteria.sql` per
  user instruction, after the other agent's VNPay subscription work claimed `V0004`. Updated the
  two doc references (`docs/api-reference.md`, this file) accordingly; no re-verification needed
  since only the filename changed, not the SQL content.
- 2026-08-15 — Reverted the rename: user confirmed this work finished first, so it keeps `V0004`
  and the VNPay work renumbers instead when it lands. Renamed back to
  `V0004__seed_scoring_criteria.sql`, restored the two doc references, rebuilt, and re-verified the
  migration applies cleanly against the local database. Then merged all 5 branches into `dev`
  locally per explicit user instruction (not pushed) — see the summary entry below.
- 2026-08-15 — Completed item 2 (bucket admin CRUD) on branch `fix/bucket-admin-crud`: new
  `GetBucketsQuery`/`UpdateBucketCommand` + `BucketsController` (`GET`/`PATCH /api/buckets`, Admin
  role). `UpdateBucketCommandHandler` deliberately does not check `Bucket.IsLocked` — admin can
  edit every bucket including the locked `savings` row, per the product decision recorded in
  `backend-gaps.md` item 2. No migration needed (table and rows already existed). `dotnet build` 0
  errors, all 200 Application unit tests pass. Live-verified: `GET` lists all 3 buckets, `PATCH` on
  the locked `savings` bucket succeeds and persists, unknown id returns 404, unauthenticated
  returns 401; test change reverted afterward. `docs/api-reference.md` updated (new Buckets
  section after Categories).
- 2026-08-15 — Completed item 3 (category icon upload) on branch `feature/category-icon-upload`:
  new `ICategoryIconService`/`CategoryIconService` mirroring `AvatarService`'s pattern (writes to
  `wwwroot/category-icons/`, served via the already-wired `UseStaticFiles()`); new
  `CategoryIconValidationRules` (SVG-only, 1 byte–200 KB, must start with `<svg`/`<?xml`, rejects
  `<script`/`on*=` as a defense-in-depth XSS guard); new `POST /api/categories/icons` (Customer) on
  `CategoriesController`. `CreateCustomCategoryRequest` gained `Icon`;
  `CategoryService.CreateCustomCategoryAsync` now persists it (was previously hardcoded to `null`
  with a "stays device-local" comment — that decision is reversed) and rejects any value not
  prefixed `/category-icons/` so a client can't smuggle an arbitrary external URL into a
  frontend-rendered field.
  **Bug found and fixed in this new code**: `AppSettings:WebRootPath` is configured as `""` (empty
  string, not absent) in `appsettings.json`, so the copied `?? fallback` pattern from
  `AvatarService` never triggered — files wrote relative to the process's current directory
  instead of `wwwroot`. Fixed with an explicit `string.IsNullOrWhiteSpace` check in
  `CategoryIconService`; `AvatarService` itself has the same latent bug but was left untouched
  (out of scope) — flagged separately.
  **Pre-existing unrelated bug found while verifying, not fixed (out of scope)**: every
  `POST /api/categories/custom` call fails with a 500 regardless of this change — the generated id
  (`"custom_" + Guid.NewGuid()`, 43 chars) exceeds `categories.id`'s `varchar(40)` column. Flagged
  as a separate task (likely fix: `Guid.NewGuid().ToString("N")`, 32 chars, fits).
  `dotnet build` 0 errors, all 200 Application unit tests pass. Live-verified everything in this
  feature's own scope: icon upload accepts a valid SVG and serves it back at the returned URL
  (200), rejects wrong content-type and `<script>`-bearing SVGs (400), and the external-URL
  rejection on `POST /custom` fires correctly (400) — full category creation with the icon
  attached couldn't be end-to-end verified because of the unrelated id-length bug above.
  `docs/api-reference.md` updated (Categories table + new `POST /icons` section).
- 2026-08-15 — Completed item 4 (RAG document preview) on branch `feature/rag-document-preview`:
  `PdfDocumentIngestionService.IngestPdfAsync` now buffers the upload into memory once, validates
  the `%PDF` magic number (400 otherwise — no format check existed before), writes the raw bytes
  to `wwwroot/documents/{id}.pdf` (served via the already-wired `UseStaticFiles()`), and sets
  `RagDocument.Uri` accordingly (previously always null for PDFs — the file was discarded after
  text extraction). New `IRagDocumentQueryService`/`RagDocumentQueryService` (direct DbContext
  query, matching the AI feature area's existing non-MediatR convention) backs a new
  `GET /api/ai/documents` on `AdminAiController`, returning
  `{ id, title, sourceType, uri?, createdAt, chunkCount }` newest first, no pagination (low,
  admin-curated volume). `dotnet build` 0 errors, all 200 Application unit tests pass.
  Live-verified: non-PDF upload rejected (400 wrong magic bytes); a hand-crafted real PDF passed
  magic-byte validation, text extraction, and disk-write (confirmed the file landed at
  `wwwroot/documents/{guid}.pdf`) — ingestion then failed at the Gemini embedding call itself
  (`ai_provider_unavailable`), a pre-existing external dependency unreachable in this sandbox, not
  related to this change. Verified the list endpoint and static serving independently by
  inserting a test `rag_document` row directly: `GET /api/ai/documents` returned it with the
  correct shape and the file served at its `uri` with 200; test row and file removed afterward.
  `docs/api-reference.md` updated (`POST /documents` validation note + new `GET /documents`
  section).
- 2026-08-15 — Completed item 5 (admin list endpoints), the last of the five, on branch
  `feature/admin-list-endpoints`: new `GET /api/category-corrections` (`CategoryCorrectionQueryDto`
  with `categoryId?`/`createdAtFrom?`/`createdAtTo?`/`page`/`pageSize`, backed by
  `GetCategoryCorrectionsQuery` reading `CategoryCorrectionLog` directly via `FinVietDbContext`,
  matching the CategoryService/SpendingScoreService direct-DbContext convention rather than adding
  a one-off repository interface) and new `GET /api/users` (`UserQueryDto` with `search?` +
  paging, backed by `GetUsersQuery` reading `Customer`, excluding soft-deleted rows, no sensitive
  fields in the response). Both follow `TransactionRepository.GetPagedAsync`'s exact pattern:
  `page`/`pageSize` clamped to `[1,100]`/default 20, UTC start-of-day/exclusive-next-day date
  range, `PagedResult<T>`. `dotnet build` 0 errors, all 200 Application unit tests pass.
  Live-verified: users list returns all 4 seeded accounts with correct paging metadata; `search`
  filters correctly; unauthenticated returns 401; category-corrections returns empty against a
  clean table, then correctly filters by `categoryId` and `createdAtFrom` once two test rows were
  inserted directly (no real correction rows existed to exercise otherwise); all test rows removed
  afterward. `docs/api-reference.md` updated (new Category Corrections + Users sections).
  **All 5 backend-gaps.md items (excluding subscription/payment) are now complete**, each
  committed on its own branch.
- 2026-08-15 — Merged all 5 branches into `dev` locally, per explicit user instruction, in order
  1→5 (`fix/scoring-weights`, `fix/bucket-admin-crud`, `feature/category-icon-upload`,
  `feature/rag-document-preview`, `feature/admin-list-endpoints`). Every merge after the first
  conflicted in this file (`context/current-feature.md`) since each branch independently rewrote
  the same Status/Goals/Notes/History header off the original `dev` baseline — resolved by hand
  each time, keeping every branch's unique History entry and consolidating Status/Goals/Notes into
  one accurate final state. `docs/api-reference.md` merged cleanly every time (each branch's
  documentation additions landed in different, non-overlapping sections).
  `src/FinViet.Infrastructure/DependencyInjection.cs` merged cleanly once (items 3 and 4 both added
  a registration line, in different parts of the file). `dotnet build` passed with 0 errors after
  every merge commit. Not pushed to `origin/dev`.
- 2026-08-14 — Started Gemini thought-response filtering after a current-month budget question returned a formatting meta-instruction. Approved scope: filter `Part.Thought` at the SDK boundary, request no thought output, treat thought-only output as provider unavailable, preserve HTTP 429-only model fallback, and add provider/persistence regressions without cleaning historical rows.
- 2026-08-14 — Completed Gemini thought-response filtering: the SDK boundary now returns only non-thought text parts, all generation configs request `IncludeThoughts=false`, and thought-only output follows the existing provider-unavailable path without model failover. Added mixed/thought-only/split-JSON extraction tests and a history-enabled chat regression proving only the friendly fallback is persisted. Focused Gemini tests passed 28/28, focused chat tests passed 6/6, all Application tests passed 200/200, solution build passed with 0 warnings/errors, and the API reached `Now listening on http://0.0.0.0:5122`. No live Gemini call, historical-row cleanup, RAG re-index, commit, or push was performed.
- 2026-08-14 — Started Gemini quota-aware model fallback. Approved scope: primary plus four Flash-first generation fallbacks, HTTP 429-only failover, per-attempt privacy-safe telemetry, no embedding changes or RAG re-index.
- 2026-08-14 — Completed on branch `feature/gemini-model-fallback`: added validated primary/fallback configuration, HTTP 429-only failover through `gemini-2.5-flash-lite`, stable `gemini-3.1-flash-lite`, `gemini-3-flash-preview`, and `gemini-2.5-pro`, plus per-attempt `rate_limited`/`success`/`error` telemetry. Embedding remained `gemini-embedding-001` at 768 dimensions. Focused Gemini tests passed 24/24, all Application tests passed 195/195, and solution build passed with 0 warnings/errors. No live quota exhaustion, RAG re-index, commit, or push was performed.
- 2026-08-14 — Fixed startup validation after real API smoke testing exposed .NET's array binder appending configured fallback values to property-initializer defaults. Changed the options property to start empty and apply defaults only in `PostConfigure` when no list is configured, added binder regression coverage, and verified the API reached `Now listening on http://0.0.0.0:5122` without an options-validation failure.

- 2026-08-13 — Started database baseline reset on branch `feature/database-baseline-dbup`. Approved direction: dump the stable local PostgreSQL schema, replace legacy V2–V25/additive startup DDL with `V0001`/`V0002`, and manage future changes through DbUp with a real journal and advisory lock.
- 2026-08-13 — Captured verified full/schema-only backups outside the repository; generated exact `V0001` schema and `V0002` reference data; added `V0003` for active V22/V24 schema that the former initializer skipped. Replaced schema guessing/additive DDL with embedded DbUp migrations, `public.schema_versions`, per-script transactions, and the advisory lock. Added confirmed restored-database adoption, non-Development admin-secret enforcement, Development-only demo seeds, and canonical `category_source` alignment.
- 2026-08-13 — Verified actual API bootstrap on disposable PostgreSQL: three scripts journaled, 3 buckets/18 categories/1 admin created, no demo business rows in Production, `/health` succeeded, and second startup was a no-op. Verified missing admin secret fails closed, confirmed adoption marks V0001/V0002 and applies V0003 while preserving seven customers, and a drifted HNSW index is rejected. Downgraded DbUp to `6.0.0-beta.13` because stable 6.x resolves incompatible Npgsql 9; the selected package resolves Npgsql 8.0.6 alongside EF provider 8.0.11. Removed legacy V2–V25 scripts and documented bootstrap/adoption/Render/rollback procedures.
- 2026-08-13 — Added a dedicated PostgreSQL integration project with four disposable-database tests covering clean/repeat bootstrap, concurrent initialization, production admin-secret enforcement, and Development demo gating; all four passed against local PostgreSQL 18. Fixed demo wallet seeding to persist newly-added customers before querying them. Solution build passed with 0 errors; Domain tests passed 1/1; Application tests passed 184/184 after aligning stale `request` fixtures with canonical `persona`.
- 2026-08-13 — Completed the database baseline reset. Final Debug solution build passed with 0 warnings/errors and Release API publish passed with only six pre-existing nullable warnings. Local API and PostgreSQL verification passed; Render staging rehearsal and production cutover remain operator deployment steps and were not run. No RAG re-index, commit, push, or production change was performed.

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
- 2026-08-10 — `docs/api-reference.md` rewritten with a full validation-rules + business-logic pass across every controller (8 parallel research agents, one per feature area), at the user's request ahead of wiring the mobile client. Uncommitted at the time; user then directed `dev`→`khoi` sync (see below) before any commit happened.
- 2026-08-10 — User merged `origin/dev` (2 new commits: Render Docker deployment) into local `dev`, then merged `dev` into `khoi` (clean auto-merge, one shared file `WalletService.cs`); the uncommitted `docs/api-reference.md` rewrite carried over onto `khoi` via stash. `khoi` now 6 commits ahead of `origin/khoi`, uncommitted. User confirmed future work happens on `khoi`.
- 2026-08-10 — User shared `docs/10-08-2026-be-todos.md` (FE↔BE reconciliation output from the `finviet-mobile` team) as the next task; two independent items, sequenced as two branches per user's choice. Started item 1 on `feature/transaction-conditional-edit` (branched from `khoi`).
- 2026-08-10 — Implemented item 1: `UpdateTransactionDto`/`UpdateTransactionCommand` extended from `{ categoryId? }` to `{ categoryId?, amount?, merchant?, transactionDate? }` with partial-update semantics (null = unchanged) on all four fields, including `categoryId` (a small compatible tightening from the old always-overwrite-with-null behavior — see Notes). New `TransactionRules.EnsureEditableFieldsAllowed` rejects `amount`/`merchant`/`transactionDate` on a `sepay_linked`-wallet transaction with 422 `synced_transaction_fields_locked` (checked unlocked in the handler via `IWalletRepository.GetByIdAsync`, since wallet type is immutable post-creation — no lock/race needed). New `ITransactionRepository.EditForCustomerAsync` mirrors the create/delete row-lock pattern: locks the wallet only when a synced field is actually being edited, reverses the old balance delta and applies the new one on amount change (422 `insufficient_balance`, reused code), writes merchant/date/category directly otherwise. `PATCH /classify` left untouched (still single-purpose set/clear, no lock, no field restriction). Added `InternalsVisibleTo` on `FinViet.Application` (matching the existing `FinViet.Infrastructure` pattern) so `TransactionRules` could be unit-tested directly; 7 new tests (`TC-TXN-U01..05`) cover `EnsureEditableFieldsAllowed`'s branches. All 143 unit tests pass, `dotnet build` 0 errors (2 pre-existing nullable warnings, unchanged). Balance-math/lock behavior in `EditForCustomerAsync` itself is not unit-testable (raw `FOR UPDATE` SQL needs real Postgres, same gap already accepted for `CreateManualForCustomerAsync`/`DeleteForCustomerAsync`) — verified instead by booting the API to confirm DI resolves cleanly (no local Postgres DB available in this environment to exercise the full path). `docs/api-reference.md` updated (Transactions DTO/PUT/PATCH sections split apart, new error code added to the table). Committed (`683780e`), merged into `khoi` (fast-forward), branch deleted, per explicit user instruction.
- 2026-08-10 — User said "go ahead, item 2." Branch `feature/saving-goal-ledger-rework` created from `khoi`. Discovered while reading `SavingGoalService.cs` that 2c (per-action wallet choice on contribute) was **already implemented** on `khoi` — `ContributeSavingGoalRequest.FundingWalletId` and `ApplyContributionAsync`'s request-wallet-then-goal-wallet fallback already existed; the only real gap against 2c's own validation bullet was that the resolved funding wallet was never checked for `sepay_linked`. Implemented the rest: 2a `GET /saving-goals/{id}/contributions` (`SavingGoalContributionResponse[]`, newest first, 404 via null for a goal not owned by the caller); 2b `POST /saving-goals/{id}/withdraw` (`WithdrawSavingGoalRequest{amount,walletId,note?}`, required `Idempotency-Key`, 422 `goal_withdraw_exceeds_saved`/`goal_withdraw_target_sepay_unsupported`, books an `income` transaction crediting the wallet and a `SavingGoalContribution` row with `type="withdrawal"`); 2c's sepay gap closed with new 422 `goal_funding_wallet_sepay_unsupported` on both create's and contribute's funding-wallet resolution; 2d `note?` added to `ContributeSavingGoalRequest`/`WithdrawSavingGoalRequest`, persisted via a new `internal static SavingGoalService.ValidateNote` (255-char cap, matching the existing `savings_goal_contributions.note` column — no migration needed for that column, it already existed and was already EF-mapped, just never written to). New migration `V24__saving_goal_contribution_type.sql` adds the `type` column (`'contribution'` default, backfills existing rows, CHECK constraint) — same "run manually before starting the API" caveat as `V22`/`V23` (v3-schema skip in `DbInitializer`). **Necessary fix beyond the spec**: `DeleteGoalAsync` previously assumed every ledger entry was an `expense`/contribution and both rejected (`goal_ledger_invalid`) anything else and always reversed by adding the amount back — both wrong once `income`/withdrawal entries exist. Fixed to accept both types and reverse each with the correct sign via new `internal static SavingGoalService.ReversalDelta`, guarded by a new 422 `goal_ledger_reversal_insufficient_balance` if undoing a withdrawal would drive its wallet negative (the withdrawn cash already spent). 10 new unit tests (`TC-GOAL-U01..08`, one `[Theory]` with 3 cases) cover `GetContributionsAsync` (InMemory-testable, no DB locking involved) plus the two extracted pure helpers; the locked/transactional paths (`WithdrawAsync`, updated `ApplyContributionAsync`, updated `DeleteGoalAsync`) remain integration-only (raw `FOR UPDATE` SQL, same accepted gap as `CreateManualForCustomerAsync`). All 153 unit tests pass, `dotnet build` 0 errors (2 pre-existing nullable warnings, unchanged); API boots cleanly (DI resolves; DB init itself fails locally, no Postgres DB in this environment — same known gap as item 1). `docs/api-reference.md` updated (Saving Goals section: two new endpoints, two new DTOs, all four existing endpoints' validation/business-logic notes revised, 6 new error codes, migration note). Committed (`a7529af`), merged into `khoi` (fast-forward), branch deleted, per explicit user instruction.
- 2026-08-10 — While staging item 2's commit, found `docs/10-08-2026-be-todos.md` had picked up a new §3 (income allocation arbitrary-month lookup) that hadn't been there when first read. Flagged it to the user; user confirmed they added it themselves and said to implement it. Branch `feature/income-allocation-month-lookup` created from `khoi`. Implemented: `GetIncomeAllocationQuery` gained an optional `Month` positional param; `ProfileController.GetIncomeAllocation` takes `[FromQuery] string? month`; `IIncomeAllocationService.GetSummaryAsync`/`IncomeAllocationService.GetSummaryAsync` gained an optional `string? month = null` — when given, resolves `Current` via the existing `GetEffectiveAsync(customerId, month, ct)` against that month instead of today's, and always returns `Pending = null`; when omitted, behavior is byte-for-byte the same as before. New `internal static IncomeAllocationService.NormalizeMonth` validates/zero-pads `yyyy-MM`, deliberately copying `BudgetService.ResolveMonthWindow`'s exact parsing rule and error message ("Month must use yyyy-MM format.") rather than inventing a second month-validation style. 10 new unit tests (`TC-INCALLOC-09..12`): pure `NormalizeMonth` cases (no DB) plus two InMemory-DB `GetSummaryAsync` tests proving the carried-forward-month resolution and that a real "next month" draft row never leaks into `Pending` when an explicit historical month is queried. All 163 unit tests pass, `dotnet build` 0 errors/0 warnings. `docs/api-reference.md` updated (Profile section: `GET /income-allocation` table row + endpoint doc revised for the new query param).
- 2026-08-11 — Implemented Gemini Flash safe-copilot scope on `feature/gemini-safe-copilot`: official `Google.GenAI` provider and 768-dimensional embeddings; V25 plus v3 additive startup schema; durable PostgreSQL quotas; customer AI preferences; owner-scoped categorization with manual/rule precedence and off/suggest/threshold modes; customer-owned chat sessions with history-off privacy; deterministic scoped financial context, citations/limitations and RAG threshold; weekly-report preference/quota/true-overrun/notification handling; Admin-only document route separation. Replaced the Ollama runbook with `docs/gemini-setup.md` and updated API/test documentation. Added privacy-safe provider usage records (model, outcome, latency, SDK token counts/response ID where available) and best-effort audits for preferences, categorization, report fallbacks, RAG skips/failures, and session lifecycle; no prompts, answers, balances, or document content are copied into telemetry. Production-hardening review added caller-cancellation passthrough, classification/customer-embedding quotas, broader audit tests, serialized database initialization, fail-fast startup, partial-schema reconciliation, and explicit operator intervention instead of deleting duplicate reports/scores. Solution build and 183 Application unit tests pass; Domain test passes. The real-server integration suite still self-skips all 66 tests because no API is reachable. Live Gemini, disposable PostgreSQL V25, Swagger verification and RAG re-index remain pending; no re-index, commit or push performed.
- 2026-08-13 — Side task started on branch `docs/api-reference-health-status`
  (created from `dev`): document the two undocumented health/status
  minimal-API endpoints (`GET /`, `GET /health` in `Program.cs`) and remove
  `docs/10-08-2026-be-todos.md` now that all 3 of its items have shipped and
  been folded into `api-reference.md`. Companion work on `finviet-mobile`:
  wired CSV import to the already-working `POST /extract/csv` endpoint, and
  corrected stale "AI is mock-only" docs there.
- 2026-08-13 — Implemented: added a "Health / Status" section to
  `docs/api-reference.md` (between Conventions and Auth) documenting
  `GET /` and `GET /health`; deleted `docs/10-08-2026-be-todos.md`. Doc-only
  change, no `dotnet build` impact. Committed (`69a478f`) on
  `docs/api-reference-health-status`, then merged into
  `feature/sentry-backend-setup` (which also carries the separately-committed
  `af6c948` "feat: add Sentry error tracking to backend").
- 2026-08-17 — Implemented savings-bucket goal netting on branch `fix/savings-bucket-goal-netting`:
  `BudgetService.ComputeBucketSpentAsync`'s Savings bucket now nets `cat_savings_goal` transactions
  (contribution-expense minus withdrawal-income, floored at 0, via a new
  `ComputeGoalNetSavingsAsync` helper) into `Spent`, instead of excluding the category outright;
  every other bucket's exclusion is unchanged, and
  `CalculateFlatBudgetAdherenceScore`'s separate needs/wants-only exclusion was deliberately left
  untouched (different, correct design choice). New `BudgetServiceTests.cs` (3 tests, using the
  existing `TestDbContextFactory` EF Core InMemory pattern): nets a contribution against a
  withdrawal correctly; a withdrawal exceeding this month's contributions floors only the goal
  component at 0 without erasing an unrelated ordinary Savings-category expense (the exact
  data-loss repro reported from the mobile side); a month with no goal activity is unaffected.
  `dotnet build` 0 errors (6 pre-existing nullable warnings, unchanged); full
  `FinViet.Application.UnitTests` 238/238 (235 pre-existing + 3 new), no regressions. No live
  Postgres integration-test run (none configured in this environment). Not committed/pushed yet.
