# FinViet Backend API Reference

Extracted from `src/FinViet.Api/Controllers` and `src/FinViet.Application/DTOs` (+ Features).
Live Swagger/OpenAPI JSON is also available at `/swagger/v1/swagger.json` when the API is running.

## Conventions

- All routes are prefixed `api/...`.
- Auth: Bearer JWT (`Authorization: Bearer <accessToken>`) unless marked **Anonymous**.
- Standard envelope (unless noted otherwise):
  ```ts
  ApiResponse<T> = { success: boolean, message?: string, data?: T }
  ```
  `TransactionsController` returns raw objects/`PagedResult<T>` without this envelope.
- Paged envelope:
  ```ts
  PagedResult<T> = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
  ```
- `Idempotency-Key` header supported (optional) on: create transaction, create/contribute saving goal, wallet transfer/withdraw.

---

## Auth — `api/auth` (anonymous, except `/change-password`)

| Method | Path | Request | Response |
|---|---|---|---|
| POST | `/register` | `{ fullName, email, password }` | `ApiResponse<string>` (201) |
| POST | `/verify-email` | `{ token }` | `ApiResponse<string>` |
| GET | `/verify-email?token=` | query `token` | HTML page (not JSON) |
| POST | `/resend-verification` | `{ email }` | `ApiResponse<string>` |
| POST | `/login` | `{ email, password }` | `ApiResponse<AuthResponseDto>` |
| POST | `/admin-login` | `{ username, password }` | `ApiResponse<AuthResponseDto>` |
| POST | `/google-login` | `{ idToken }` | `ApiResponse<AuthResponseDto>` |
| POST | `/refresh-token` | `{ refreshToken }` | `ApiResponse<AuthResponseDto>` |
| POST | `/logout` | `{ refreshToken }` | 204 No Content |
| POST | `/forgot-password` | `{ email }` | `ApiResponse<string>` |
| POST | `/reset-password` | `{ token, newPassword, confirmPassword }` | `ApiResponse<string>` |
| POST | `/change-password` **(role: Customer)** | `{ currentPassword, newPassword }` | `ApiResponse<string>` (400 if current password is wrong) — revokes all other active refresh tokens on success |

**AuthResponseDto**
```ts
{ accessToken: string, refreshToken: string, accessTokenExpiry: DateTime, profile: ProfileDto }
```
**ProfileDto**
```ts
{
  customerId: Guid, fullName: string, email: string, avatarUrl?: string,
  gender?: "Male"|"Female"|..., dateOfBirth?: DateOnly, monthlyIncomeExpected?: number,
  isEmailVerified: bool, isActive: bool, onboardingDone: bool, createdAt?: DateTime,
  needsPct: number, wantsPct: number, savingsPct: number,
  theme: "Light"|"Dark"|"System", notifBudgetThresholds: number[] /* [warning, exceeded], e.g. [80,100] */
}
```

---

## Account — `api/account` (auth required)

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| DELETE | `/` | Customer | — | `ApiResponse<string>` — self soft-delete, revokes refresh tokens |
| PUT | `/deactivate/{customerId:guid}` | Admin | — | `ApiResponse<string>` (404 if not found) |

---

## Profile — `api/profile` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<ProfileDto>` |
| PUT | `/` | `UpdateProfileRequest` | `ApiResponse<ProfileDto>` |
| POST | `/avatar` | multipart file (`file`, ≤5MB JPEG/PNG/WebP) | `ApiResponse<string>` (avatar URL) |
| GET | `/income-allocation` | — | `ApiResponse<IncomeAllocationSummaryDto>` |
| POST | `/income-allocation` | `ScheduleIncomeAllocationRequest` | `ApiResponse<IncomeAllocationEntryDto>` |

**UpdateProfileRequest**
```ts
{ fullName: string, monthlyIncomeExpected?: number, gender?: Gender, dateOfBirth?: DateOnly }
```

> `needsPct`/`wantsPct`/`savingsPct` on `UpdateProfileRequest` only take effect while onboarding
> (`Customer.OnboardingDone` is still false) — that's the one-time "onboarding default" the
> income-allocation resolver falls back to. Once onboarding is done, sending any of
> `monthlyIncomeExpected`/`needsPct`/`wantsPct`/`savingsPct` here throws 422
> `allocation_locked_use_schedule_endpoint`; use the endpoints below instead.

**ScheduleIncomeAllocationRequest**: `{ monthlyIncome: number, needsPct: number, wantsPct: number, savingsPct: number }` — always schedules for **next calendar month**; calling again before rollover just revises that pending draft.
**IncomeAllocationEntryDto**: `{ effectiveMonth: string /* yyyy-MM */, monthlyIncome: number, needsPct: number, wantsPct: number, savingsPct: number }`
**IncomeAllocationSummaryDto**: `{ current: IncomeAllocationEntryDto, pending: IncomeAllocationEntryDto | null }`

---

## Categories — `api/categories`

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| GET | `/?type=` | any authenticated | query `type?` | `ApiResponse<CategoryResponse[]>` (customer's own bucket override, if any, wins over the global default) |
| GET | `/{id}` | any authenticated | — | `ApiResponse<CategoryResponse>` (404) |
| POST | `/` | Admin | `CreateCategoryRequest` | `ApiResponse<CategoryResponse>` (201) |
| PATCH | `/{id}` | Admin | `UpdateCategoryRequest` | `ApiResponse<CategoryResponse>` (404) |
| DELETE | `/{id}` | Admin | — | `ApiResponse<object?>` (404) |
| PUT | `/{id}/bucket` | Customer | `SetCategoryBucketRequest` | `ApiResponse<CategoryResponse>` — reassign which bucket this expense category counts against, for the caller only. No admin approval needed. |
| DELETE | `/{id}/bucket` | Customer | — | `ApiResponse<CategoryResponse>` — clears the caller's override, reverting to the category's global default. |

**CategoryResponse**
```ts
{
  categoryId: string, categoryName: string, nameVi?: string, nameEn?: string,
  type: string, isMandatory: bool, expenseClass?: string, icon?: string,
  color?: string, sortOrder?: number
}
```
**CreateCategoryRequest**: `{ categoryId?, categoryName?, nameVi?, nameEn?, type, isMandatory, expenseClass?, icon?, color?, sortOrder? }`
**UpdateCategoryRequest**: all fields optional versions of the above (no `categoryId`).
**SetCategoryBucketRequest**: `{ bucketId: "needs" | "wants" | "savings" }` — expense categories only; `cat_savings_goal` is reserved and cannot be reassigned.

> The former `category-requests` admin-approval flow (submit → admin approve/reject → category
> created) was removed. Users already have the code-level right to change a category's bucket
> assignment, so bucket reassignment is now a direct, non-approved customer action via the two
> endpoints above, backed by the `customer_categories` table.

---

## Transactions — `api/transactions` (role: Customer)

> Note: This controller returns raw objects (not wrapped in `ApiResponse`).

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?page&pageSize&walletId&type&categoryId&from&to&q&uncategorizedOnly` | `TransactionQueryDto` (query) | `PagedResult<TransactionResponseDto>` |
| GET | `/summary?year=&month=` | query | `TransactionSummaryResponseDto` |
| GET | `/{id:guid}` | — | `TransactionResponseDto` |
| POST | `/` | `CreateTransactionDto` + header `Idempotency-Key?` | `TransactionResponseDto` (201) |
| PUT | `/{id}` | `UpdateTransactionDto` | `TransactionResponseDto` |
| DELETE | `/{id}` | — | `bool` |
| PATCH | `/{id}/classify` | `ClassifyTransactionDto` | `TransactionResponseDto` |

**CreateTransactionDto**
```ts
{ walletId: Guid, categoryId?: string, transactionType: string, amount: number,
  transactionDate: DateTime, note?: string, description?: string, merchant?: string, entryMethod?: string }
```
**UpdateTransactionDto**: `{ categoryId?: string }`
**ClassifyTransactionDto**: `{ categoryId?: string }`
**TransactionResponseDto**
```ts
{
  transactionId: Guid, customerId: Guid, walletId: Guid, categoryId?: string,
  transactionType: string, sourceChannel: string, entryMethod: string, amount: number,
  transactionDate: DateTime, note?: string, description?: string, merchant?: string,
  transferPairId?: Guid, externalId?: string, createdAt: DateTime, updatedAt?: DateTime
}
```
**TransactionSummaryResponseDto**
```ts
{
  income: number, expense: number, net: number,
  byCategory: { categoryId?, categoryName?, total: number }[],
  byDay: { date: DateOnly, income: number, expense: number, net: number }[],
  topBeneficiaries: { beneficiary: string, total: number }[]
}
```

---

## Wallets — `api/wallets` (role: Customer; the SePay webhook is anonymous)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<WalletListResponse>` |
| POST | `/` | `CreateWalletRequest` | `ApiResponse<WalletResponse>` (201) |
| GET | `/{id:guid}` | — | `ApiResponse<WalletResponse>` (404) |
| PATCH | `/{id:guid}` | `UpdateWalletRequest` | `ApiResponse<WalletResponse>` (404) |
| DELETE | `/{id:guid}` | — | 204 (404 if missing) |
| POST | `/transfer` | `TransferWalletRequest` + `Idempotency-Key?` | `ApiResponse<TransferWalletResponse>` |
| POST | `/withdraw` | `WithdrawWalletRequest` + `Idempotency-Key?` | `ApiResponse<WithdrawWalletResponse>` |
| GET | `/{id:guid}/transactions?page&pageSize&fromDate&toDate&categoryId&transactionType&sortOrder` | `WalletTransactionQuery` (query) | `ApiResponse<PagedResult<WalletTransactionResponse>>` |
| GET | `/sepay/authorize-url` | — | `ApiResponse<SepayAuthorizeUrlResponse>` |
| POST | `/sepay/bank-accounts` | `SepayBankAccountsRequest` | `ApiResponse<SepayBankAccountResponse[]>` |
| POST | `/sepay/link` | `LinkSepayAccountRequest` | `ApiResponse<SepayLinkResult>` |
| POST | `/sepay/link-token` | `LinkSepayTokenRequest` | `ApiResponse<SepayLinkResult>` |
| GET | `/sepay/links` | — | `ApiResponse<SepayLinkStatusResponse[]>` |
| POST | `/{id:guid}/sepay-sync` | — | `ApiResponse<SepayWalletSyncResponse>` |
| POST | `/sepay/sync-all` | — | `ApiResponse<SepaySyncAllResponse>` |
| POST | `/{id:guid}/sepay-webhook` | — | `ApiResponse<SepayWebhookRegistrationResponse>` |
| DELETE | `/{id:guid}/sepay-webhook` | — | `ApiResponse<SepayWebhookRegistrationResponse>` |
| DELETE | `/{id:guid}/sepay-link` | — | `ApiResponse<SepayUnlinkResponse>` |
| POST | `/sepay/webhook` **(Anonymous, `Authorization: Apikey <SePay:WebhookApiKey>`)** | `SepayWebhookRequest` | `ApiResponse<SepayWebhookResult>` |

**CreateWalletRequest**: `{ walletName, walletType, initialBalance }`
**UpdateWalletRequest**: `{ walletName?, walletType? }`
**WalletResponse**
```ts
{
  walletId: Guid, customerId: Guid, walletName: string, walletType: string, balance: number,
  sepayBankAccountId?: number, institutionName?: string, accountMask?: string,
  authMode?: "oauth" | "static", lastSyncedAt?: DateTime
}
```
**WalletListResponse**: `{ totalBalance: number, wallets: WalletResponse[] }`
**TransferWalletRequest**: `{ fromWalletId: Guid, toWalletId: Guid, amount: number, description?: string }`
**TransferWalletResponse**: `{ fromWalletId, toWalletId, fromWalletBalance, toWalletBalance }`
**WithdrawWalletRequest**: `{ fromWalletId: Guid, toWalletId?: Guid, amount: number, description?: string }`
**WithdrawWalletResponse**: `{ fromWalletId, fromWalletBalance, toWalletId?, toWalletBalance? }`
**WalletTransactionQuery**: `{ page=1, pageSize=10, fromDate?, toDate?, categoryId?, transactionType?, sortOrder="desc" }`
**WalletTransactionResponse**: `{ transactionId, walletId, categoryId?, transactionType, amount, transactionDate, note? }`

**SePay (bank-linking) DTOs**
```ts
SepayAuthorizeUrlResponse: { authorizeUrl: string, state: string, expiresAt: DateTime }
SepayBankAccountsRequest:  { code: string, state?: string }
SepayBankAccountResponse:  { id, label, accountNumber /* masked */, accountHolderName, balance,
                             bankShortName, bankCode, bankIconUrl?, alreadyLinked: boolean }
LinkSepayAccountRequest:   { code: string, state?: string, bankAccountId?: number }
LinkSepayTokenRequest:     { apiToken: string, accountNumber?: string }
SepayLinkResult:           { wallets: WalletResponse[], transactionsSynced: number }
SepayLinkStatusResponse:   { walletId, walletName, balance, authMode: "oauth" | "static",
                             sepayBankAccountId?, bankShortName?, accountMask?, accountHolderName?,
                             lastSyncedAt?, relinkRequired: boolean,
                             webhookId?: number, webhookRegistered: boolean }
SepayWebhookRegistrationResponse:
                           { walletId, webhookId: number, webhookUrl: string,
                             eventType: "All", alreadyExisted: boolean }
SepayWalletSyncResponse:   { walletId, balance, transactionsCreated, transactionsUpdated, syncedAt }
SepaySyncAllResponse:      { wallets: SepayWalletSyncResponse[], transactionsCreated,
                             transactionsUpdated, failures: Record<Guid, string> }
SepayUnlinkResponse:       { walletId, walletType: "basic", transactionsRetained: number }
SepayWebhookRequest:       { id: number, gateway?, transactionDate?, accountNumber?, code?, content?,
                             transferType?: "in" | "out", transferAmount, accumulated, subAccount?,
                             referenceCode?, description? }
SepayWebhookResult:        { success: boolean, outcome: "created" | "updated" | "ignored", walletId? }
```

**Linking flow (OAuth2)** — `GET /sepay/authorize-url` → open `authorizeUrl` in a WebView →
`POST /sepay/bank-accounts` with the returned `code` + `state` to let the user pick an account →
`POST /sepay/link` with the *same* `code`, `state` and the chosen `bankAccountId`. The exchanged
token is cached server-side for 5 minutes so the single-use code works across both calls.

**Real-time delivery** — when `SePay:WebhookUrl` and `SePay:WebhookApiKey` are configured, linking
also registers this API's receiver as a webhook on SePay (`event_type=All`, `authen_type=Api_Key`),
so transactions arrive without waiting for a sync. A failure there never fails the link — the
wallet just falls back to manual sync. `POST /{id}/sepay-webhook` registers it explicitly and is
idempotent (an existing webhook for the same account + URL is adopted, never duplicated);
`DELETE /{id}/sepay-webhook` removes it, and unlinking removes it best-effort. Needs the
`webhook:read` / `webhook:write` / `webhook:delete` scopes and a **public** URL — SePay refuses
loopback hosts, so local testing requires a tunnel. OAuth links only: a static User API token
cannot reach the webhook management API (`sepay_webhook_requires_oauth`).

**Business rules** — a `sepay_linked` wallet is read-only: it cannot take manual transactions
(`linked_wallet_read_only`) or take part in a transfer (`sepay_wallet_read_only`), and its synced
transactions cannot be deleted (`synced_transaction_locked`). `POST /withdraw` is the one money-out
path and requires a SePay-linked source (`withdraw_source_not_sepay`). Unlinking turns the wallet
back into `basic` and keeps its history.

---

## Budgets — `api/budgets` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?month=` | query `month?` | `ApiResponse<BudgetResponse[]>` |
| GET | `/buckets?month=` | query `month?` | `ApiResponse<BucketSummaryListResponse>` |
| POST | `/` | `UpsertBudgetRequest` | `ApiResponse<BudgetResponse>` |
| PATCH | `/{id:guid}` | `UpdateBudgetRequest` | `ApiResponse<BudgetResponse>` |
| DELETE | `/{id:guid}` | — | 204 |

**BudgetResponse**
```ts
{ id: Guid, categoryId: string, categoryName: string, walletId?: Guid, monthlyLimit: number,
  spent: number, remaining: number, percentage: number, status: string, bucket: string }
```
**UpsertBudgetRequest**: `{ categoryId, walletId?, monthlyLimit }`
**UpdateBudgetRequest**: `{ monthlyLimit }`
**BucketSummaryListResponse**
```ts
{ month: string, monthlyIncome: number, budgetAdherenceScore: number, uncategorizedRatio: number,
  uncategorizedWarning: bool, buckets: BucketSummaryResponse[] }
```
**BucketSummaryResponse**
```ts
{ bucket: string, allocationPct: number, allocationCap: number, categoryLimitTotal: number,
  spent: number, remaining: number, percentage: number, overAllocated: bool,
  expectedSpent: number, paceDeviation: number, paceStatus: string }
```

---

## Saving Goals — `api/saving-goals` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<SavingGoalResponse[]>` |
| GET | `/{id:guid}` | — | `ApiResponse<SavingGoalResponse>` (404) |
| POST | `/` | `CreateSavingGoalRequest` + `Idempotency-Key?` | `ApiResponse<SavingGoalResponse>` (201) |
| PATCH | `/{id:guid}` | `UpdateSavingGoalRequest` | `ApiResponse<SavingGoalResponse>` (404) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |
| POST | `/{id:guid}/contribute` | `ContributeSavingGoalRequest` + `Idempotency-Key?` | `ApiResponse<SavingGoalResponse>` (404) |

**CreateSavingGoalRequest**: `{ goalName, targetAmount, deadline?, initialAmount?, fundingWalletId? }`
**UpdateSavingGoalRequest**: `{ goalName?, targetAmount?, deadline? }`
**ContributeSavingGoalRequest**: `{ amount }`
**SavingGoalResponse**
```ts
{
  goalId, customerId, goalName, targetAmount, currentAmount, deadline?, fundingWalletId?,
  remainingAmount, progressPercent, daysRemaining?, isCompleted,
  monthlySavingNeeded?, monthsRemaining?
}
```

---

## Rules — `api/rules` (role: Customer) — merchant-keyword auto-categorization

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<RuleResponse[]>` |
| POST | `/` | `CreateRuleRequest` | `ApiResponse<CreateRuleResponse>` (201, 409 on conflict) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |

**CreateRuleRequest**: `{ merchantKeyword, categoryId }`
**RuleResponse**: `{ ruleId, merchantKeyword, categoryId, categoryName?, appliedCount, createdAt }`
**CreateRuleResponse**: `{ rule: RuleResponse, appliedCount }` — appliedCount = # transactions retro-tagged

---

## Notifications — `api/notifications` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?unread=` | query `unread: bool` | `ApiResponse<NotificationResponse[]>` |
| PATCH | `/{id:guid}/read` | — | `ApiResponse<object?>` (404) |
| POST | `/read-all` | — | `ApiResponse<{ count: number }>` |

**NotificationResponse**
```ts
{ notificationId, type, title, message?, entityType?, entityId?, isRead, sentAt? }
```

---

## Extract — `api/extract` (role: Customer) — parse-only, nothing persisted

| Method | Path | Request | Response |
|---|---|---|---|
| POST | `/sms` | `{ text: string }` (≤20,000 chars) | `ApiResponse<ExtractResponse>` |
| POST | `/csv` | multipart: `file` (.csv/.xlsx/.xls, ≤5MB) + `maxRows?` | `ApiResponse<ExtractResponse>` |

**ExtractResponse**
```ts
{
  rows: ExtractedTransactionItem[], totalScanned: number, skipped: number, errors: string[]
}
```
**ExtractedTransactionItem**
```ts
{ amount, type, merchant?, description?, transactionDate, categoryId?, categoryName?, confidence? }
```

---

## AI — `api/ai` (auth required)

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| POST | `/categorize/preview` | any | `{ input: string }` | `ApiResponse<AiClassificationResult>` |
| POST | `/categorize/{transactionId:guid}` | any | — | `ApiResponse<CategorizationOutcome>` |
| POST | `/transactions/{transactionId:guid}/override` | any | `{ categoryId: string }` | `ApiResponse<CategorizationOutcome>` |
| GET | `/score?period=WEEKLY\|MONTHLY` | any | query | `ApiResponse<SpendingScoreResult>` |
| GET | `/reports` | any | — | `ApiResponse<WeeklyReportResponse[]>` |
| GET | `/reports/{reportId:guid}` | any | — | `ApiResponse<WeeklyReportResponse>` (404) |
| POST | `/reports/generate` | any | — | `ApiResponse<WeeklyReportResponse>` — manually generates last completed week's report |
| POST | `/chat` | any | `{ question: string }` | `ApiResponse<ChatMessageResponse>` |
| GET | `/chat/history?limit=50` | any | query | `ApiResponse<ChatMessageResponse[]>` |
| POST | `/documents` | Admin | multipart: `file` (PDF, ≤20MB) + `title?` (form) | `ApiResponse<Guid>` (documentId) |

**AiClassificationResult**: `{ categoryName?: string, confidence: number }` (0–1)
**CategorizationOutcome**
```ts
{ transactionId, categoryId?, categoryName?, confidence?, isAiClassified: bool, queued: bool, source: "RULE"|"AI"|"FALLBACK" }
```
**SpendingScoreResult**
```ts
{
  periodType: "WEEKLY"|"MONTHLY", periodStart: DateOnly, periodEnd: DateOnly,
  finalScore: number, spikeScore?: number, budgetScore?: number, savingsScore?: number,
  weights: Record<string, number>, colorBadge: "GREEN"|"YELLOW"|"RED", comment?: string
}
```
**WeeklyReportResponse**: `{ reportId, periodStart, periodEnd, narrative, finalScore?, colorBadge?, generatedAt }`
**ChatAskRequest**: `{ question }`
**ChatMessageResponse**: `{ messageId, senderType: "USER"|"AI", content, timestamp? }`

---

## Common envelope types

```ts
ApiResponse<T> = { success: bool, message?: string, data?: T }
PagedResult<T> = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
```
