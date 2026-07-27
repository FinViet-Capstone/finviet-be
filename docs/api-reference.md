# FinViet Backend API Reference

Extracted from `src/FinViet.Api/Controllers`, `src/FinViet.Application/DTOs`, and `context/be-revamp.md` (revamp history).  
Live Swagger/OpenAPI JSON is also available at `/swagger/v1/swagger.json` when the API is running.

## Conventions

- All routes are prefixed `api/...`.
- Auth: Bearer JWT (`Authorization: Bearer <accessToken>`) unless marked **Anonymous**.
- Standard envelope (unless noted otherwise):
  ```ts
  ApiResponse<T> = { success: boolean, message?: string, data?: T }
  ```
  `TransactionsController` returns raw objects/`PagedResult<T>` **without** this envelope.
- Paged envelope:
  ```ts
  PagedResult<T> = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
  ```
- `Idempotency-Key` header (optional string) supported on: create transaction, create/contribute saving goal, wallet transfer/withdraw.
- Error codes follow the `coding-standards.md` exception-to-status-code table: `NotFoundException → 404`, `BadRequestException → 400`, `ConflictException → 409`, `UnauthorizedException → 401`, `ValidationException → 422`.

---

## Auth — `api/auth`

> Most endpoints are **anonymous**. `/change-password` requires a Customer JWT.

| Method | Path | Auth | Request body | Response |
|---|---|---|---|---|
| POST | `/register` | Anonymous | `{ fullName, email, password }` | `ApiResponse<string>` (201) |
| POST | `/verify-email` | Anonymous | `{ token }` | `ApiResponse<string>` |
| GET | `/verify-email?token=` | Anonymous | query `token` | HTML page (text/html, not JSON) |
| POST | `/resend-verification` | Anonymous | `{ email }` | `ApiResponse<string>` |
| POST | `/login` | Anonymous | `{ email, password }` | `ApiResponse<AuthResponseDto>` |
| POST | `/admin-login` | Anonymous | `{ username, password }` | `ApiResponse<AuthResponseDto>` |
| POST | `/google-login` | Anonymous | `{ idToken }` | `ApiResponse<AuthResponseDto>` |
| POST | `/refresh-token` | Anonymous | `{ refreshToken }` | `ApiResponse<AuthResponseDto>` |
| POST | `/logout` | Anonymous | `{ refreshToken }` | 204 No Content |
| POST | `/forgot-password` | Anonymous | `{ email }` | `ApiResponse<string>` |
| POST | `/reset-password` | Anonymous | `{ token, newPassword, confirmPassword }` | `ApiResponse<string>` |
| POST | `/change-password` | **Customer** | `{ currentPassword, newPassword }` | `ApiResponse<string>` — revokes all other active refresh tokens on success; 400 if current password is wrong |

**AuthResponseDto**
```ts
{
  accessToken: string,
  refreshToken: string,
  accessTokenExpiry: DateTime,
  profile: ProfileDto
}
```

**ProfileDto**
```ts
{
  customerId: Guid,
  fullName: string,
  email: string,
  avatarUrl?: string,
  gender?: "Male" | "Female" | ...,
  dateOfBirth?: DateOnly,
  monthlyIncomeExpected?: number,
  isEmailVerified: boolean,
  isActive: boolean,
  onboardingDone: boolean,
  createdAt?: DateTime,
  // 50-30-20 allocation — falls back to column defaults (50/30/20) when no income_allocation_settings row exists
  needsPct: number,
  wantsPct: number,
  savingsPct: number,
  // from customer_settings — defaults to System / [80, 100] until first PATCH /profile/settings
  theme: "Light" | "Dark" | "System",
  notifBudgetThresholds: number[]   // [warningPct, exceededPct], e.g. [80, 100]
}
```

---

## Account — `api/account` (auth required)

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| DELETE | `/` | Customer | — | `ApiResponse<string>` — soft-deletes own account and revokes all refresh tokens |
| PUT | `/deactivate/{customerId:guid}` | Admin | — | `ApiResponse<string>` (404 if customer not found) |

---

## Profile — `api/profile` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<ProfileDto>` |
| PUT | `/` | `UpdateProfileRequest` | `ApiResponse<ProfileDto>` |
| PATCH | `/settings` | `UpdateProfileSettingsRequest` | `ApiResponse<ProfileDto>` |
| POST | `/avatar` | multipart `file` (JPEG/PNG/WebP, ≤5 MB) | `ApiResponse<string>` (avatar URL) |
| GET | `/income-allocation` | — | `ApiResponse<IncomeAllocationSummaryDto>` |
| POST | `/income-allocation` | `ScheduleIncomeAllocationRequest` | `ApiResponse<IncomeAllocationEntryDto>` |

**UpdateProfileRequest**
```ts
{
  fullName: string,
  monthlyIncomeExpected?: number,
  gender?: "Male" | "Female" | ...,
  dateOfBirth?: DateOnly,
  onboardingDone?: boolean,
  // Only accepted while onboardingDone = false (one-time onboarding defaults).
  // After onboarding is done, sending these throws 422 allocation_locked_use_schedule_endpoint.
  // Use POST /profile/income-allocation instead.
  needsPct?: number,
  wantsPct?: number,
  savingsPct?: number
}
```

**UpdateProfileSettingsRequest**
```ts
{
  theme?: "Light" | "Dark" | "System",
  notifBudgetThresholds?: number[]   // [warningPct, exceededPct], e.g. [80, 100]
}
```
> Upserts the `customer_settings` row. `BudgetService` reads these thresholds when deciding whether to fire a `budget_alert` notification (falls back to `[80, 100]` if no settings row exists yet).

**ScheduleIncomeAllocationRequest**
```ts
{ monthlyIncome: number, needsPct: number, wantsPct: number, savingsPct: number }
```
> Always schedules for **next calendar month**. Calling again before rollover revises the same pending draft rather than creating a new row or touching the current month's locked entry.

**IncomeAllocationEntryDto**
```ts
{ effectiveMonth: string /* "yyyy-MM" */, monthlyIncome: number, needsPct: number, wantsPct: number, savingsPct: number }
```

**IncomeAllocationSummaryDto**
```ts
{
  current: IncomeAllocationEntryDto,   // current month — locked / read-only
  pending: IncomeAllocationEntryDto | null   // next month's draft, if scheduled
}
```

---

## Categories — `api/categories`

> Visibility rule: `custom_*` categories are scoped to their creator. A customer sees only their own custom categories alongside the global seeded `cat_*` set. Requesting another customer's `custom_*` id returns 404, not 403.

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| GET | `/?type=` | any authenticated | query `type?` | `ApiResponse<CategoryResponse[]>` |
| GET | `/{id}` | any authenticated | — | `ApiResponse<CategoryResponse>` (404) |
| POST | `/` | Admin | `CreateCategoryRequest` | `ApiResponse<CategoryResponse>` (201) |
| POST | `/custom` | Customer | `CreateCustomCategoryRequest` | `ApiResponse<CategoryResponse>` (201) — id is `custom_<uuid>`, always `expense` type, private to creator |
| PATCH | `/{id}` | Admin | `UpdateCategoryRequest` | `ApiResponse<CategoryResponse>` (404) |
| DELETE | `/{id}` | Admin | — | `ApiResponse<object?>` (404) |
| DELETE | `/custom/{id}` | Customer | — | `ApiResponse<object?>` — 404 if not yours or a seeded category; 400 if referenced by transactions |
| PUT | `/{id}/bucket` | Customer | `SetCategoryBucketRequest` | `ApiResponse<CategoryResponse>` — reassigns which budget bucket this expense category counts against, for the caller only |
| DELETE | `/{id}/bucket` | Customer | — | `ApiResponse<CategoryResponse>` — clears the caller's bucket override, reverting to the category's global default |

**CategoryResponse**
```ts
{
  categoryId: string,
  categoryName: string,
  nameVi?: string,
  nameEn?: string,
  type: string,           // "income" | "expense"
  isMandatory: boolean,
  expenseClass?: string,  // "needs" | "wants" | "savings" (global default bucket)
  icon?: string,
  color?: string,
  sortOrder?: number
}
```

**CreateCategoryRequest** (Admin): `{ categoryId?, categoryName?, nameVi?, nameEn?, type, isMandatory, expenseClass?, icon?, color?, sortOrder? }`  
**UpdateCategoryRequest** (Admin): same fields as above, all optional, no `categoryId`.  
**CreateCustomCategoryRequest** (Customer): `{ name, bucket: "needs" | "wants" | "savings", color? }` — no `type` (always `expense`); icon is device-local only and never sent to the backend.  
**SetCategoryBucketRequest**: `{ bucketId: "needs" | "wants" | "savings" }` — expense categories only; `cat_savings_goal` is reserved and cannot be reassigned.

> At creation time, `POST /categories/custom` automatically seeds an active `CustomerCategory` override row for the creator (bucket = whatever they picked), so the category is immediately usable without a follow-up `PUT .../bucket` call.

---

## Transactions — `api/transactions` (role: Customer)

> **Note:** This controller returns raw objects, **not** wrapped in `ApiResponse`.

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | `TransactionQueryDto` (query params) | `PagedResult<TransactionResponseDto>` |
| GET | `/summary?year=&month=` | query `year`, `month` | `TransactionSummaryResponseDto` |
| GET | `/{id:guid}` | — | `TransactionResponseDto` |
| POST | `/` | `CreateTransactionDto` + header `Idempotency-Key?` | `TransactionResponseDto` (201) |
| PUT | `/{id}` | `UpdateTransactionDto` | `TransactionResponseDto` |
| DELETE | `/{id}` | — | `bool` |
| PATCH | `/{id}/classify` | `ClassifyTransactionDto` | `TransactionResponseDto` |

**TransactionQueryDto** (query params)
```ts
{
  page: number = 1,
  pageSize: number = 20,
  walletId?: Guid,
  type?: string,           // "INCOME" | "EXPENSE" | "TRANSFER" | "DEBT_PAYMENT" (case-insensitive)
  categoryId?: string,
  from?: DateTime,
  to?: DateTime,
  q?: string,              // free-text search over Note and BeneficiaryName
  uncategorizedOnly: boolean = false
}
```

**CreateTransactionDto**
```ts
{
  walletId: Guid,
  categoryId?: string,
  transactionType: string,
  amount: number,
  transactionDate: DateTime,
  note?: string,
  description?: string,
  merchant?: string,
  entryMethod?: string
}
```

**UpdateTransactionDto**: `{ categoryId?: string }`  
**ClassifyTransactionDto**: `{ categoryId?: string }`

**TransactionResponseDto**
```ts
{
  transactionId: Guid,
  customerId: Guid,
  walletId: Guid,
  categoryId?: string,
  transactionType: string,
  sourceChannel: string,
  entryMethod: string,
  amount: number,
  transactionDate: DateTime,
  note?: string,
  description?: string,
  merchant?: string,
  transferPairId?: Guid,
  externalId?: string,
  createdAt: DateTime,
  updatedAt?: DateTime
}
```

**TransactionSummaryResponseDto**
```ts
{
  income: number,
  expense: number,
  net: number,
  byCategory: { categoryId?: string, categoryName?: string, total: number }[],
  byDay: { date: DateOnly, income: number, expense: number, net: number }[],
  topBeneficiaries: { beneficiary: string, total: number }[]
}
```

---

## Wallets — `api/wallets` (role: Customer; SePay webhook is anonymous)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<WalletListResponse>` |
| POST | `/` | `CreateWalletRequest` | `ApiResponse<WalletResponse>` (201) |
| GET | `/{id:guid}` | — | `ApiResponse<WalletResponse>` (404) |
| PATCH | `/{id:guid}` | `UpdateWalletRequest` | `ApiResponse<WalletResponse>` (404) |
| DELETE | `/{id:guid}` | — | 204 (404 if not found) |
| POST | `/transfer` | `TransferWalletRequest` + `Idempotency-Key?` | `ApiResponse<TransferWalletResponse>` |
| POST | `/withdraw` | `WithdrawWalletRequest` + `Idempotency-Key?` | `ApiResponse<WithdrawWalletResponse>` |
| GET | `/{id:guid}/transactions` | `WalletTransactionQuery` (query) | `ApiResponse<PagedResult<WalletTransactionResponse>>` |
| GET | `/sepay/authorize-url` | — | `ApiResponse<SepayAuthorizeUrlResponse>` |
| POST | `/sepay/bank-accounts` | `SepayBankAccountsRequest` | `ApiResponse<SepayBankAccountResponse[]>` |
| POST | `/sepay/link` | `LinkSepayAccountRequest` | `ApiResponse<SepayLinkResult>` |
| POST | `/sepay/link-token` | `LinkSepayTokenRequest` | `ApiResponse<SepayLinkResult>` |
| GET | `/sepay/links` | — | `ApiResponse<SepayLinkStatusResponse[]>` |
| POST | `/{id:guid}/sepay-sync` | — | `ApiResponse<SepayWalletSyncResponse>` |
| POST | `/sepay/sync-all` | — | `ApiResponse<SepaySyncAllResponse>` |
| POST | `/{id:guid}/sepay-webhook` | — | `ApiResponse<SepayWebhookRegistrationResponse>` — idempotent |
| DELETE | `/{id:guid}/sepay-webhook` | — | `ApiResponse<SepayWebhookRegistrationResponse>` |
| DELETE | `/{id:guid}/sepay-link` | — | `ApiResponse<SepayUnlinkResponse>` |
| POST | `/sepay/webhook` **(Anonymous, `Authorization: Apikey <SePay:WebhookApiKey>`)** | `SepayWebhookRequest` | `ApiResponse<SepayWebhookResult>` |

**CreateWalletRequest**: `{ walletName: string, walletType: string, initialBalance: number }`  
**UpdateWalletRequest**: `{ walletName?: string, walletType?: string }`

**WalletResponse**
```ts
{
  walletId: Guid,
  customerId: Guid,
  walletName: string,
  walletType: string,
  balance: number,
  sepayBankAccountId?: number,
  institutionName?: string,
  accountMask?: string,
  authMode?: "oauth" | "static",
  lastSyncedAt?: DateTime
}
```

**WalletListResponse**: `{ totalBalance: number, wallets: WalletResponse[] }`  
**TransferWalletRequest**: `{ fromWalletId: Guid, toWalletId: Guid, amount: number, description?: string }`  
**TransferWalletResponse**: `{ fromWalletId, toWalletId, fromWalletBalance, toWalletBalance }`  
**WithdrawWalletRequest**: `{ fromWalletId: Guid, toWalletId?: Guid, amount: number, description?: string }`  
**WithdrawWalletResponse**: `{ fromWalletId, fromWalletBalance, toWalletId?, toWalletBalance? }`

**WalletTransactionQuery** (query params)
```ts
{
  page: number = 1,
  pageSize: number = 10,
  fromDate?: DateTimeOffset,
  toDate?: DateTimeOffset,
  categoryId?: string,
  transactionType?: string,   // "income" | "expense" | "transfer_out" | "transfer_in"
  sortOrder: string = "desc"  // "asc" | "desc"
}
```

**WalletTransactionResponse**
```ts
{
  transactionId: Guid,
  walletId: Guid,
  categoryId?: string,
  transactionType: string,
  amount: number,
  transactionDate: DateTimeOffset,
  note?: string
}
```

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

SepayWebhookRequest:       { id: number, gateway?, transactionDate?, accountNumber?, code?,
                             content?, transferType?: "in" | "out", transferAmount, accumulated,
                             subAccount?, referenceCode?, description? }
SepayWebhookResult:        { success: boolean, outcome: "created" | "updated" | "ignored", walletId? }
```

**Linking flow (OAuth2):**  
`GET /sepay/authorize-url` → open `authorizeUrl` in a WebView → `POST /sepay/bank-accounts` with `code` + `state` to list accounts → `POST /sepay/link` with the same `code`, `state`, and chosen `bankAccountId`. The exchanged token is cached server-side for 5 minutes so the single-use code works across both calls.

**Linking flow (static API token):**  
`POST /sepay/link-token` with `{ apiToken, accountNumber? }` — bypasses OAuth; the SePay personal API token is stored and used for subsequent syncs.

**Real-time delivery:** When `SePay:WebhookUrl` and `SePay:WebhookApiKey` are configured, linking also registers `POST /api/wallets/sepay/webhook` as a receiver on SePay (`event_type=All`, `authen_type=Api_Key`). A webhook registration failure never fails the link — the wallet falls back to manual sync. `POST /{id}/sepay-webhook` re-registers explicitly (idempotent); `DELETE /{id}/sepay-webhook` removes it. OAuth links only — a static API token cannot reach the webhook management API (`sepay_webhook_requires_oauth`).

**Business rules:**  
- A `sepay_linked` wallet is **read-only**: no manual transactions (`linked_wallet_read_only`), cannot be used in a transfer (`sepay_wallet_read_only`), and its synced transactions cannot be deleted (`synced_transaction_locked`).  
- `POST /withdraw` is the only money-out path and requires a SePay-linked source (`withdraw_source_not_sepay`).  
- Unlinking (`DELETE /{id}/sepay-link`) converts the wallet back to `basic` type and keeps its transaction history.

---

## Budgets — `api/budgets` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?month=` | query `month?` (yyyy-MM) | `ApiResponse<BudgetResponse[]>` |
| GET | `/buckets?month=` | query `month?` (yyyy-MM) | `ApiResponse<BucketSummaryListResponse>` |
| POST | `/` | `UpsertBudgetRequest` | `ApiResponse<BudgetResponse>` |
| PATCH | `/{id:guid}` | `UpdateBudgetRequest` | `ApiResponse<BudgetResponse>` |
| DELETE | `/{id:guid}` | — | 204 |

> `GET /budgets/buckets` resolves the income/allocation via `income_allocation_settings` for the requested month (carry-forward to the latest entry with `effectiveMonth ≤ month`). Editing the current allocation no longer retroactively changes past months' bucket-adherence numbers.

**BudgetResponse**
```ts
{
  id: Guid,
  categoryId: string,
  categoryName: string,
  walletId?: Guid,
  monthlyLimit: number,
  spent: number,
  remaining: number,
  percentage: number,
  status: string,   // "ok" | "warning" | "exceeded" — driven by customer's NotifBudgetThresholds
  bucket: string    // "needs" | "wants" | "savings"
}
```

**UpsertBudgetRequest**: `{ categoryId: string, walletId?: Guid, monthlyLimit: number }`  
**UpdateBudgetRequest**: `{ monthlyLimit: number }`

**BucketSummaryListResponse**
```ts
{
  month: string,                  // "yyyy-MM"
  monthlyIncome: number,          // from income_allocation_settings for the requested month
  budgetAdherenceScore: number,
  uncategorizedRatio: number,
  uncategorizedWarning: boolean,
  buckets: BucketSummaryResponse[]
}
```

**BucketSummaryResponse**
```ts
{
  bucket: string,             // "needs" | "wants" | "savings"
  allocationPct: number,
  allocationCap: number,
  categoryLimitTotal: number,
  spent: number,
  remaining: number,
  percentage: number,
  overAllocated: boolean,
  expectedSpent: number,
  paceDeviation: number,
  paceStatus: string
}
```

---

## Saving Goals — `api/saving-goals` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<SavingGoalResponse[]>` |
| GET | `/{id:guid}` | — | `ApiResponse<SavingGoalResponse>` (404) |
| POST | `/` | `CreateSavingGoalRequest` + header `Idempotency-Key?` | `ApiResponse<SavingGoalResponse>` (201) |
| PATCH | `/{id:guid}` | `UpdateSavingGoalRequest` | `ApiResponse<SavingGoalResponse>` (404) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |
| POST | `/{id:guid}/contribute` | `ContributeSavingGoalRequest` + header `Idempotency-Key?` | `ApiResponse<SavingGoalResponse>` (404) |

**CreateSavingGoalRequest**: `{ goalName: string, targetAmount: number, deadline?: DateOnly, initialAmount?: number, fundingWalletId?: Guid }`  
**UpdateSavingGoalRequest**: `{ goalName?: string, targetAmount?: number, deadline?: DateOnly }`  
**ContributeSavingGoalRequest**: `{ amount: number }`

**SavingGoalResponse**
```ts
{
  goalId: Guid,
  customerId: Guid,
  goalName: string,
  targetAmount: number,
  currentAmount: number,
  deadline?: DateOnly,
  fundingWalletId?: Guid,
  remainingAmount: number,
  progressPercent: number,
  daysRemaining?: number,
  isCompleted: boolean,
  monthlySavingNeeded?: number,   // null if no deadline
  monthsRemaining?: number        // null if no deadline
}
```

---

## Rules — `api/rules` (role: Customer) — merchant-keyword auto-categorization

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<RuleResponse[]>` |
| POST | `/` | `CreateRuleRequest` | `ApiResponse<CreateRuleResponse>` (201; 409 on keyword conflict) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |

**CreateRuleRequest**: `{ merchantKeyword: string, categoryId: string }`  
**RuleResponse**: `{ ruleId: Guid, merchantKeyword: string, categoryId: string, categoryName?: string, appliedCount: number, createdAt: DateTime }`  
**CreateRuleResponse**: `{ rule: RuleResponse, appliedCount: number }` — `appliedCount` = number of existing transactions retroactively re-tagged by this new rule (fund-transfer transactions are skipped).

---

## Notifications — `api/notifications` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?unread=` | query `unread: boolean` | `ApiResponse<NotificationResponse[]>` |
| PATCH | `/{id:guid}/read` | — | `ApiResponse<object?>` (404) |
| POST | `/read-all` | — | `ApiResponse<{ count: number }>` |

**NotificationResponse**
```ts
{
  notificationId: Guid,
  type: string,
  title: string,
  message?: string,
  entityType?: string,
  entityId?: Guid,
  isRead: boolean,
  sentAt?: DateTime
}
```

---

## Extract — `api/extract` (role: Customer) — parse-only, nothing persisted

| Method | Path | Request | Response |
|---|---|---|---|
| POST | `/sms` | `{ text: string }` (≤ 20,000 chars) | `ApiResponse<ExtractResponse>` |
| POST | `/csv` | multipart: `file` (.csv / .xlsx / .xls, ≤ 5 MB) + `maxRows?: number` | `ApiResponse<ExtractResponse>` |

**ExtractResponse**
```ts
{
  rows: ExtractedTransactionItem[],
  totalScanned: number,
  skipped: number,
  errors: string[]
}
```

**ExtractedTransactionItem**
```ts
{
  amount: number,
  type: string,
  merchant?: string,
  description?: string,
  transactionDate: DateTime,
  categoryId?: string,    // reserved; not populated by the SMS preview
  categoryName?: string,  // AI-suggested category name (null when unresolved)
  confidence?: number     // model confidence 0.0–1.0 (null when no suggestion)
}
```

---

## AI — `api/ai` (auth required)

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| POST | `/categorize/preview` | any | `CategorizePreviewRequest` | `ApiResponse<AiClassificationResult>` |
| POST | `/categorize/{transactionId:guid}` | any | — | `ApiResponse<CategorizationOutcome>` |
| POST | `/transactions/{transactionId:guid}/override` | any | `OverrideCategoryRequest` | `ApiResponse<CategorizationOutcome>` |
| GET | `/score?period=WEEKLY\|MONTHLY` | any | query `period` (default `WEEKLY`) | `ApiResponse<SpendingScoreResult>` |
| GET | `/reports` | any | — | `ApiResponse<WeeklyReportResponse[]>` |
| GET | `/reports/{reportId:guid}` | any | — | `ApiResponse<WeeklyReportResponse>` (404) |
| POST | `/reports/generate` | any | — | `ApiResponse<WeeklyReportResponse>` — manually generates the most recent completed week's report |
| POST | `/chat` | any | `ChatAskRequest` | `ApiResponse<ChatMessageResponse>` |
| GET | `/chat/history?limit=50` | any | query `limit` (default 50) | `ApiResponse<ChatMessageResponse[]>` |
| POST | `/documents` | **Admin** | multipart: `file` (PDF, ≤ 20 MB) + `title?` (form field) | `ApiResponse<Guid>` (documentId) — ingests PDF into the global RAG knowledge corpus |

**CategorizePreviewRequest**: `{ input: string }` — free-text beneficiary name or note to classify.  
**OverrideCategoryRequest**: `{ categoryId: string }` — overrides and writes the category to the transaction; also creates/updates a beneficiary rule for future auto-categorization.

**AiClassificationResult**
```ts
{ categoryName?: string, confidence: number }   // confidence: 0.0–1.0
```

**CategorizationOutcome**
```ts
{
  transactionId: Guid,
  categoryId?: string,
  categoryName?: string,
  confidence?: number,
  isAiClassified: boolean,
  queued: boolean,   // true when AI provider was unavailable; transaction queued for re-processing
  source: "RULE" | "AI" | "FALLBACK"
}
```

**SpendingScoreResult**
```ts
{
  periodType: "WEEKLY" | "MONTHLY",
  periodStart: DateOnly,
  periodEnd: DateOnly,
  finalScore: number,         // 0–100
  spikeScore?: number,
  budgetScore?: number,
  savingsScore?: number,
  weights: Record<string, number>,
  colorBadge: "GREEN" | "YELLOW" | "RED",
  comment?: string            // short Vietnamese comment; may be empty if AI comment was unavailable
}
```

**WeeklyReportResponse**
```ts
{ reportId: Guid, periodStart: DateOnly, periodEnd: DateOnly, narrative: string, finalScore?: number, colorBadge?: string, generatedAt: DateTime }
```

**ChatAskRequest**: `{ question: string }`  
**ChatMessageResponse**: `{ messageId: Guid, senderType: "USER" | "AI", content: string, timestamp?: DateTime }`

---

## Common envelope types

```ts
ApiResponse<T>  = { success: boolean, message?: string, data?: T }
PagedResult<T>  = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
```

---

## Error codes (selected)

| Code string | HTTP | Trigger |
|---|---|---|
| `allocation_locked_use_schedule_endpoint` | 422 | Sending allocation fields on `PUT /profile` after onboarding is complete |
| `linked_wallet_read_only` | 400 | Manual transaction attempted on a `sepay_linked` wallet |
| `sepay_wallet_read_only` | 400 | Transfer using a SePay-linked wallet |
| `synced_transaction_locked` | 400 | Delete attempted on a SePay-synced transaction |
| `withdraw_source_not_sepay` | 400 | `POST /wallets/withdraw` with a non-SePay source wallet |
| `sepay_webhook_requires_oauth` | 400 | Webhook registration attempted on a static-token-linked wallet |
