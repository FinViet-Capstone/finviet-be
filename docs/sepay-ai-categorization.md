# SePay AI categorization and review inbox

Spec for making AI categorization of SePay-imported transactions reliable and giving it a review step, like CSV, SMS and photo already have.
It covers `finviet-be` and `finviet-mobile`.
Every decision below was settled in a design interview (2026-09-23/24); the decision log at the end maps each one to its question id (Q1 to Q31).

## Problem

SePay transactions are already sent to the AI today, but the result is mostly lost or invisible:

- `SepayWalletService.CategorizeAsync` (`src/FinViet.Infrastructure/Services/SepayWalletService.cs:1383`) runs after link, sync, sync-all and webhook inserts, one transaction at a time, inline in the request.
- It uses the generic `classification` rate-limit tier (6/min, 100/day), so an initial link that imports more than 6 expenses marks most of them `fallback` / `rate_limited`.
  Nothing retries them; `AiCategorizationService.ReprocessAsync` exists but has no caller.
- Per-row exceptions are swallowed, so failures are silent (KNOWN-ISSUES #2).
- The default AI preference is `suggest_only`, so the AI guess is stored in `ai_category_guess` / `ai_confidence` while `category_id` stays NULL.
  No read DTO exposes those columns, so the app just shows "Chưa phân loại".
- There is no review surface.
  The only per-transaction AI step is the "Gợi ý danh mục bằng AI" button on the transaction detail screen, which calls Gemini again.
- CSV, SMS and photo extraction have the same silent-failure problem: a Gemini error and "the AI could not decide" both come back as a null category.

## Goals

- SePay expenses get categorized reliably, in the background, on the batch rate-limit tier.
- Users review SePay AI results in one inbox: accept, change, dismiss, bulk-accept, and retry failed rows.
- Every entry method (CSV, SMS, photo, SePay) can tell "AI failed" apart from "AI unsure", with a shared category field on mobile.
- No database schema change (Q3).
  The team's database diagram stays accurate; categorization state is derived from existing columns.

## Non-goals

- Categorizing SePay income (Q1).
- Automatic merchant-rule learning from corrections (Q4); the inbox only offers a "create rule" shortcut.
- Push or local notifications for new SePay rows (Q15).
- Showing the reason an AI call failed (Q21).
- Automatically re-running AI on existing stuck rows (Q5); the user triggers retry.
- Re-running AI on rows that already have a suggestion (Q23).

## Categorization status (derived, no schema change)

`categorizationStatus` is computed from existing `transactions` columns in exactly one place, a repository helper used by the list filter, the DTO mapper and the worker, so the three can never disagree.
Rules are evaluated top to bottom; the first match wins.

| # | Status | Condition | In inbox | UI |
|---|---|---|---|---|
| 1 | `reviewed` | `ai_classification_source = 'manual'` | no | normal category, or "Chưa phân loại" if dismissed |
| 2 | `applied` | `category_id` is set | no | category, with "AI" or "Quy tắc" badge from `aiSource` where shown |
| 3 | `none` | not a SePay expense (`entry_method <> 'sepay_sync'` or `type <> 'expense'`) | no | unchanged |
| 4 | `suggested` | source `ai_suggestion`, `ai_category_guess` set | yes | guessed category + "AI" badge |
| 5 | `unsure` | source `ai_suggestion`, `ai_category_guess` NULL | yes | neutral "Chưa phân loại - chạm để chọn", no retry |
| 6 | `pending` | source NULL, `ai_classified_at` within the last 10 minutes | yes | spinner, "AI đang phân loại..." |
| 7 | `failed` | anything else (source `fallback`, or source NULL and not queued in the last 10 minutes) | yes | red "AI không phân loại được - thử lại", retry available |

Notes:

- The word "suggested" is internal only and never appears in the app.
- `ai_classified_at` gains a second meaning: it is written when a row is queued (source stays NULL) and overwritten when the worker finishes (Q28).
  Document this on the entity and in `docs/api-reference.md`.
- Rows the AI never ran on (older than the Q27 window, or queued rows lost in a restart) fall through to `failed`, because the user's next step is the same: retry or pick a category.
- Status 3 keeps the derived field meaningful for every transaction DTO (Q10) without claiming AI state for rows that were never AI candidates.
- With AI mode `off` (Q16), `failed` rows render with the neutral `unsure` wording and no retry, because nothing failed.

## Backend (`finviet-be`)

### Arrival pipeline (Q2, Q8, Q27)

1. Link (OAuth, static token, sandbox), manual sync, sync-all and webhook keep writing rows with `UpsertNormalizedAsync` and commit as today.
2. The inline `CategorizeAsync` call at `SepayWalletService.cs:242, :412, :562, :768, :1113` is replaced by an enqueue step.
3. The enqueue step selects the newly inserted expense ids whose `transaction_date` falls in the current or previous calendar month (Asia/Ho_Chi_Minh).
   Older imported rows are not queued and show as `failed` until the user retries them (Q27).
4. If the customer's AI mode is `off`, nothing is queued.
5. For the selected ids, set `ai_classified_at = now()` in the same transaction as the upsert, then write `(customerId, ids)` to an in-memory `Channel`.
6. The response returns without waiting for Gemini.

A new `SepayCategorizationWorker : BackgroundService` under `Infrastructure/Services/Background/` (same pattern as `WeeklyReportScheduler`) drains the channel:

- The channel is unbounded (it only holds ids).
- One customer batch at a time, up to 20 ids per batch, scope created per batch through `IServiceScopeFactory`.
- Each batch goes through a new `IAiCategorizationService.CategorizeManyAsync(customerId, ids)` that combines the per-row rules of `CategorizeTransactionAsync` (manual lock, merchant rule, mode, empty input) with the parallel classification of `PreviewManyAsync` (`SemaphoreSlim(6)`, rate-limit feature `classification_batch`, 500/min, 5,000/day).
- Rows whose source became `manual` while queued (accepted or dismissed meanwhile) are skipped.
- Outcomes are persisted per row:
  - rule match: `category_id` set, source `merchant_rule` (status `applied`);
  - mode `high_confidence_auto` and confidence at or above the threshold: `category_id` set, source `ai_auto` (status `applied`);
  - otherwise with a resolved category: guess and confidence stored, source `ai_suggestion` (status `suggested`);
  - model answered but no valid category (`unresolved_category`): source `ai_suggestion`, guess NULL (status `unsure`, Q31);
  - rate-limited, provider error or empty input: source `fallback` (status `failed`); not re-queued automatically.
- `ai_classified_at` is overwritten with the completion time.
- After a batch applies any category, call `SyncBudgetOnTransactionChangeAsync` once per affected (customer, month) (Q26).
- A worker crash or restart loses the channel contents; affected rows become `failed` after 10 minutes and the user can retry.

### Read side (Q9, Q10)

Add to `TransactionResponseDto` and `WalletTransactionResponse` (and their mappers, e.g. `TransactionRepository.MapToDto`):

- `categorizationStatus`: `none | pending | suggested | unsure | failed | applied | reviewed`
- `aiSuggestedCategoryId`, `aiSuggestedCategoryName` (from `ai_category_guess`)
- `aiConfidence`
- `aiSource`: `manual | merchant_rule | ai_auto | ai_suggestion | fallback | null`

Extend `GET /api/transactions` with filters:

- `categorizationStatus` (comma-separated list)
- `entryMethod` (e.g. `sepay_sync`)
- `walletId` if it is not already supported

The inbox query is `categorizationStatus=pending,suggested,unsure,failed&entryMethod=sepay_sync`, paged with the shared `PagedResult<T>`, newest first.
Badge counts use the same query with `pageSize=1` and read `TotalItems`.
The query should use the existing, currently unused partial index `ix_transactions_customer_ai_pending` where the plan allows; no new index is added (it would be a schema change).

### Write side (Q4, Q11, Q12, Q17, Q18, Q19, Q26)

All new routes live on `AiController` (`api/ai`, Customer role, `ApiResponse<T>` envelope).

| Endpoint | Body | Behavior |
|---|---|---|
| `POST /api/ai/transactions/{id}/override` (existing) | `{ categoryId }` | Unchanged semantics (correction log, source `manual`, locks the row), plus budget re-check. |
| `POST /api/ai/transactions/override-batch` (new) | `{ items: [{ transactionId, categoryId }] }`, max 200 | Applies each item independently; returns `{ results: [{ transactionId, outcome }] }` with `ok`, `not_found`, `category_unavailable` or `not_eligible`. One correction log per applied row. One budget re-check per affected month. |
| `POST /api/ai/transactions/{id}/dismiss` (new) | none | Allowed on SePay expenses with `category_id` NULL. Sets source `manual`, keeps `category_id` NULL and keeps `ai_category_guess`. No correction log. Returns the updated transaction DTO. |
| `POST /api/ai/transactions/{id}/undo-dismiss` (new) | `{ previousStatus: "suggested" \| "unsure" \| "failed" }` | Allowed only on a row that is `manual` with `category_id` NULL. Restores source `ai_suggestion` (suggested, unsure) or `fallback` (failed). Returns the updated DTO. |
| `POST /api/ai/sepay/retry` (new) | `{ walletId? }` | Selects the caller's SePay expenses in status `failed` (including never-queued rows), sets source NULL and `ai_classified_at = now()`, enqueues them, returns `202` with `{ queued }`. Returns 422 `ai_mode_off` when the AI mode is `off`. |

Also:

- `PUT/PATCH /api/transactions/{id}/classify` and `TransactionRepository.ClassifyAsync` set source `manual` (clearing `ai_confidence`) and write a correction log when the previous source was AI, so no edit route bypasses the lock (Q4).
- `MerchantRuleService.CreateRuleAsync` retro-apply calls the budget re-check once per affected month (Q26).
- Accepting a suggestion is `override` with the suggested category id; there is no separate accept endpoint.

### Extraction (CSV, SMS, photo) (Q25, Q29, Q31)

- `ExtractedTransactionItem` gains `categorizationStatus`: `ok | unsure | failed`.
  - `ok`: a category was resolved (rule or AI).
  - `unsure`: the AI answered but no valid category.
  - `failed`: rate-limited or provider error; this includes the case where `PreviewManySafeAsync` catches an exception for the whole batch.
- Income rows, which are never categorized, return `ok` with a NULL category and no badge.
- `AiClassificationResult` gains a `status` so `PreviewAsync` and `PreviewManyAsync` can report the distinction instead of an empty result.
- New `POST /api/ai/categorize/preview-batch` with `{ items: [{ key, input }] }`, max 200, synchronous, backed by `PreviewManyAsync` on the `classification_batch` tier.
  Returns `{ items: [{ key, categoryId, categoryName, confidence, status }] }`.
  Used by the CSV, SMS and photo reviews to retry `failed` rows.

### AI preference behavior (Q16, Q24)

- The existing preference (`ai_customer_preferences.categorization_mode`) stays the single source of truth.
- `suggest_only` (default) holds the AI category until the user accepts it; held rows count in budgets only after acceptance.
- `high_confidence_auto` applies confident results right away (`applied`, AI badge in the inbox and detail).
- Changing the mode is not retroactive: held rows stay in the inbox (Q5).
- `off` queues nothing, and the retry endpoint returns `ai_mode_off`.

### Backend docs

- `docs/api-reference.md`: new endpoints, new DTO fields, new list filters, the `ai_classified_at` meaning, and the stale lines `:589` and `:625` (AI fallback does return `categoryId` now; `AiClassificationResult` is missing `categoryId`).
- `KNOWN-ISSUES.md` (repo root): mark #2 resolved by this work once shipped.

## Mobile (`finviet-mobile`)

### Data layer

- `src/types/transaction.ts`: add `categorizationStatus`, `aiSuggestedCategoryId`, `aiSuggestedCategoryName`, `aiConfidence`, `aiSource` to `Transaction`; map them in `src/services/real/transactions.ts`.
- `toEntryMethod` (`transactions.ts:86-91`): map `sepay_sync` to `'linked'` so the link `MethodTag` renders for SePay rows.
- `src/types/extraction.ts` and `real/extraction.ts`: map the per-row `categorizationStatus`.
- `src/services/real/sepay.ts`: export through the `@/services` barrel and stop importing it directly from `useWallets.ts` (architecture rule).
- `real/wallets.ts` `toWallet`: populate `linkedMetadata` so the sync-status text on wallet detail renders.
- New service functions: `overrideCategoryBatch`, `dismissTransaction`, `undoDismissTransaction`, `retrySepayCategorization`, `previewCategorizeBatch`, plus the new list filters on `getTransactions`.
- `src/lib/queryKeys.ts`: `transactions.review(filters)` and `transactions.reviewCount(walletId?)` under the `transactions.all()` prefix.
- New hooks:
  - `useSepayReviewQueue(walletId?)`, with `refetchInterval: 3000` while any row is `pending`, and `false` otherwise (Q18);
  - `useSepayReviewCount(walletId?)`;
  - `useAcceptSuggestion`, `useAcceptAllSuggestions`, `useDismissTransaction`, `useUndoDismiss`, `useRetrySepayCategorization`, `usePreviewCategorizeBatch`.
- Category-changing mutations invalidate `transactions.all()`, budgets and the spending score, since budgets change (Q26).

### Shared `CategorySuggestionField` (Q13, Q14, Q30, Q31)

One component, used by CSV review, SMS review, photo confirm and the SePay inbox.

- Props: the category, `source` (`'ai' | 'rule' | null`), `status` (`ok | suggested | unsure | failed | pending`), `onPress` (opens `CategoryPickerSheet`), optional `onRetry`.
- Confidence is shown only as the "AI" or "Quy tắc" badge; there is no percentage and no low-confidence highlight on the category.
- SMS drops its orange category highlight (`sms.tsx:164`) and keeps the orange highlight on amount and merchant.
  Photo keeps its orange highlight on the OCR fields.
- `failed`: red "AI không phân loại được - thử lại", with the retry affordance when `onRetry` is given.
- `unsure`: neutral "Chưa phân loại - chạm để chọn".
- `pending`: spinner and "AI đang phân loại...".
- Removes the duplicated "AI không phân loại được" strings from `csv-review.tsx` and `photo-confirm.tsx`.

### SePay review inbox (Q7, Q12, Q17, Q19, Q20, Q23)

New route `app/sepay-review.tsx`, optional `walletId` param.

- Header "Cần xem lại" with the row count.
- Wallet filter chips when the customer has 2 or more linked SePay wallets; opening from wallet detail preselects that wallet.
- Retry banner "N giao dịch AI chưa phân loại được" plus "Thử lại AI", shown only when there are `failed` rows and the AI mode is not `off`.
  In `off` mode, show a link to `/settings/ai-preferences` instead (Q16).
- Row card: description, amount, wallet, date, `CategorySuggestionField`, and actions:
  - "Chấp nhận" (`suggested` rows only): override with the suggested category, optimistic removal, error toast and row restore on failure;
  - "Đổi" / tap the field: `CategoryPickerSheet`, then override, then offer the existing "create rule" prompt (as in `transactions/[id].tsx` `handleSave`);
  - "Bỏ qua": dismiss, optimistic removal, toast "Đã bỏ qua 1 giao dịch" with "Hoàn tác" that calls undo-dismiss with the row's previous status.
- Sticky footer "Chấp nhận tất cả gợi ý (N)": `override-batch` over every loaded `suggested` row; rows that fail stay in the list with a note "N giao dịch chưa lưu được".
- Rows you don't accept stay as they are; retry only targets `failed` rows (Q23).
- Pagination via the list endpoint, newest first.
- Strings go in a new `src/data/sepayReviewData.ts`; shared categorization strings go in a new `src/data/categorizationData.ts`.

### Entry points (Q15)

- Linked-wallet detail (`app/(tabs)/wallets/[id].tsx`): a "N cần xem" badge and button when the count is above 0.
- Link success (`app/link-sepay-token.tsx`) and manual sync success (`wallets/[id].tsx` `handleSync`): the alert offers "Xem N gợi ý".
- Home `UncategorizedBanner`: when SePay rows need review, it routes to the inbox with no filter.

### Transaction detail (Q10, Q24)

- When a row has a stored AI result (`suggested`), `app/(tabs)/transactions/[id].tsx` shows the suggestion card immediately from the DTO, with the "AI" badge, "Độ tin cậy N%", and Áp dụng / Bỏ qua.
  It no longer calls `POST /api/ai/categorize/{id}` for these rows.
- The live "Gợi ý danh mục bằng AI" button stays for uncategorized rows that have never been through the AI.

### CSV, SMS and photo reviews (Q25, Q29)

- Use `CategorySuggestionField` with the per-row status.
- Failed rows offer retry through `usePreviewCategorizeBatch` (one call per review screen, all failed rows).
- CSV keeps its existing import behavior (uncategorized rows may still be imported), and photo keeps its save gate.

### Mobile docs

- `context/project-spec.md` and `context/architecture.md`: replace the "silently degrades to null, FE can't detect it" notes with the new status model, and add the SePay review inbox to the UI/UX section.
- `docs/bdd-feature-audit.md`: refresh the stale SePay, CSV and photo scenarios.

## Bugs fixed along the way

- Mobile `toEntryMethod` ignores `sepay_sync`, so SePay rows render as manual entries.
- The mobile `Transaction` type has no AI fields (covered by the data-layer changes above).
- `toWallet` never sets `linkedMetadata`, so the wallet sync-status text never renders.
- `sepay.ts` bypasses the services barrel.
- `classify` does not set source `manual` or log a correction (Q4).
- Override, SePay inserts, AI auto-apply and merchant-rule retro-apply skip the budget re-check (Q26).
- Stale backend `api-reference.md` lines `:589` and `:625`, and the stale mobile `bdd-feature-audit.md`.
- `finviet-mobile/receipt-ocr-worktree/` is an empty, stale directory; delete it after confirming with the team (mobile `ai-interaction.md`: never delete files without clarification).

## Verification (Q22)

Backend integration tests (`tests/FinViet.Api.IntegrationTests`, against the running server):

- The status filter returns the right rows for each derived status, including the 10-minute `pending` expiry.
- `override-batch` with a mix of valid, foreign, missing and unavailable-category ids returns the right per-item outcomes and applies only the valid ones.
- Dismiss then undo-dismiss restores the previous status for `suggested`, `unsure` and `failed`.
- Retry returns `202` and moves `failed` rows to `pending`; returns `ai_mode_off` in `off` mode.
- `preview-batch` returns per-item status.
- `classify` sets source `manual`.

Backend unit tests (`tests/FinViet.Application.UnitTests` or infrastructure tests): the status derivation helper, `CategorizeManyAsync` outcome mapping, and the Q27 date window.

Mobile Jest tests: the new services (`src/services/real/__tests__/`), the new hooks, `CategorySuggestionField` states, and the inbox's optimistic update and rollback.

End-to-end, as a user would experience it:

1. Run the backend locally with a real Gemini key and link a sandbox account with `link-sandbox-token`.
   The history import lands in the inbox, with only current and previous month rows processed.
2. Lower `AiLimits` in Development to force rate limits; confirm failed rows appear and retry recovers them.
3. POST a sample payload to `sepay/webhook`; confirm the new row appears in the inbox through polling and then resolves.
4. On an Android emulator, exercise accept, change, dismiss, undo, bulk accept, wallet chips, the three entry points, the detail-screen stored suggestion, and CSV/SMS/photo failed-row retry.
5. Attach screenshots of each inbox state (pending, suggested, unsure, failed, empty, AI mode `off`) in light and dark themes to the PRs.

## Delivery

- One GitHub issue in `finviet-be` and one in `finviet-mobile`, both linking this spec, created after the spec is approved.
- Branches `feature/sepay-ai-review` in each repo, per each repo's workflow.
- Backend ships first; mobile reads the new DTO fields defensively (missing field = `none`) so the two can deploy independently.

## Related, not in scope

- KNOWN-ISSUES #1 (multi-photo batches categorize only one row) is in the same code area but was not part of this interview.
  It deserves its own issue and reproduction.
- `NotificationType.SepaySyncError` is defined but never sent.

## Decision log

| Q | Decision |
|---|---|
| Q1 | Backend fixes plus a SePay review inbox; expenses only. |
| Q2 | AI runs at arrival (best effort, background) and again from the inbox on request. |
| Q3 | No schema change; status is derived from existing columns. |
| Q4 | Accept and edit go through override (correction log and manual lock); `classify` fixed the same way; "create rule" shortcut; no automatic rule learning. |
| Q5 | Existing stuck rows are never re-run automatically; the user retries or edits. |
| Q6 | Spec in `finviet-be/docs/`, plus one GitHub issue per repo after approval. |
| Q7 | An explicit "Thử lại AI" button; opening the inbox does not trigger AI. |
| Q8 | In-memory `Channel` plus a `BackgroundService` worker on the batch tier. |
| Q9 | Inbox is SePay-only, built as filters on `GET /api/transactions`. |
| Q10 | AI fields and `categorizationStatus` on every transaction read DTO. |
| Q11 | Bulk accept saves what it can and reports each row. |
| Q12 | "Bỏ qua" (dismiss) exists. |
| Q13 | Shared `CategorySuggestionField` across CSV, SMS, photo and SePay. |
| Q14 | Category confidence is badge-only (AI / Quy tắc); photo keeps OCR-field highlights; detail keeps its percentage. |
| Q15 | Entry points: wallet detail badge, link/sync success alert, home banner; no notification. |
| Q16 | AI mode `off`: inbox still lists rows, hides retry, links to settings. |
| Q17 | Per-row actions save immediately with optimistic UI; bulk is one `override-batch` call with per-row results. |
| Q18 | Retry enqueues and returns 202; the inbox polls every 3 seconds while any row is pending. |
| Q19 | Dismiss = manual with no category, locked; undo from the toast. |
| Q20 | One inbox with wallet chips at `app/sepay-review.tsx?walletId=`. |
| Q21 | Generic failed message, no reason shown. |
| Q22 | Full verification plan (sandbox E2E with real Gemini, forced rate limits, simulated webhook, emulator screenshots, integration and Jest tests). |
| Q23 | Unaccepted rows keep their AI category; retry covers only failed rows. |
| Q24 | Follow the existing AI preference for when a category counts; the UI shows category plus AI badge either way. |
| Q25 | Extract rows get `ok / unsure / failed`; CSV, SMS and photo get failed-row retry. |
| Q26 | Every path that sets a category re-checks budgets once per affected month per request. |
| Q27 | Arrival AI only for current and previous month rows; older rows wait for a user retry. |
| Q28 | `ai_classified_at` marks enqueue time; `pending` expires after 10 minutes. |
| Q29 | New synchronous `POST /api/ai/categorize/preview-batch`, max 200 items. |
| Q30 | SMS uses badge-only on the category like the other flows. |
| Q31 | Separate `failed` (red, retry) and `unsure` (neutral, no retry) wording everywhere; SePay unsure = `ai_suggestion` with NULL guess. |
