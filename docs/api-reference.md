# FinViet Backend API Reference

Extracted from `src/FinViet.Api/Controllers`, `src/FinViet.Application/{Features,DTOs}`, `src/FinViet.Infrastructure/{Features,Services,ExternalServices}`, and `context/be-revamp.md` (revamp history).
Live Swagger/OpenAPI JSON is also available at `/swagger/v1/swagger.json` when the API is running.

Every endpoint below documents **Validation** (exact field-level rules — FluentValidation, or manual/inline checks where no validator exists) and **Business logic** (what the handler actually does, side effects, edge cases) in addition to the request/response shape, so this doc can be wired directly into the mobile client's request builders and error mapping.

> **Validator coverage note**: FluentValidation validators only exist for **Auth** and **Profile** commands. Every other feature (Categories, Rules, Transactions, Wallets, Budgets, SavingGoals, Notifications, Extract, AI) has **no FluentValidation validators and no data-annotation attributes** — all validation is manual `if`/`Must`-style checks inline in services/handlers, documented explicitly per endpoint below.

## Conventions

- All routes are prefixed `api/...`.
- Auth: Bearer JWT (`Authorization: Bearer <accessToken>`) unless marked **Anonymous**.
- Standard envelope:
  ```ts
  ApiResponse<T> = { success: boolean, message?: string, data?: T }
  ```
  All controllers, including `TransactionsController`, use this envelope.
- Paged envelope:
  ```ts
  PagedResult<T> = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
  ```
- `Idempotency-Key` header: **required** (400 if missing/blank, max 200 chars) on wallet transfer, wallet withdraw, saving-goal create, and saving-goal contribute. Optional on transaction create (silently skips replay dedup if omitted).
- Exception → HTTP status mapping (`Api/Middlewares/ExceptionHandlingMiddleware.cs`):

  | Exception | Status |
  |---|---|
  | `FluentValidation.ValidationException` (auto, `ValidationBehavior`) | 400 |
  | `BadRequestException` / manual `ValidationException` | 400 |
  | `UnauthorizedException` / `UnauthorizedAccessException` | 401 |
  | `ForbiddenException` | 403 |
  | `NotFoundException` | 404 |
  | `ConflictException` | 409 |
  | `BusinessRuleException` (carries `Code`) | 422 |
  | `ExternalServiceException` | 502 |
  | `IntegrationUnavailableException` | 503 |
  | anything else | 500 (stack trace only in Development) |

---

## Auth — `api/auth`

> Most endpoints are **anonymous**. `/change-password` requires a Customer JWT. Password hashing = **BCrypt** everywhere. JWT access-token TTL = `Jwt:AccessTokenExpiryMinutes` config (default 15 min); refresh-token TTL = `Jwt:RefreshTokenExpiryDays` (default 7 days). Verification/reset codes are 6 characters, uniqueness-checked against active tokens before persisting.

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
| POST | `/change-password` | **Customer** | `{ currentPassword, newPassword }` | `ApiResponse<string>` |

**AuthResponseDto**
```ts
{ accessToken: string, refreshToken: string, accessTokenExpiry: DateTime, profile: ProfileDto }
```

**ProfileDto**
```ts
{
  customerId: Guid, fullName: string, email: string, avatarUrl?: string,
  gender?: "Male" | "Female" | ..., dateOfBirth?: DateOnly, monthlyIncomeExpected?: number,
  isEmailVerified: boolean, isActive: boolean, onboardingDone: boolean, createdAt?: DateTime,
  needsPct: number, wantsPct: number, savingsPct: number,   // 50-30-20 defaults if no allocation row
  theme: "Light" | "Dark" | "System", notifBudgetThresholds: number[]   // [warningPct, exceededPct]
}
```

### POST `/register`
**Validation** (`RegisterCommandValidator`): `fullName` required, max 100 chars. `email` required, valid email format, max 255 chars. `password` required, min 8 chars, must contain ≥1 uppercase letter, must contain ≥1 digit.
**Business logic**: Normalizes email (lowercase/trim); existing email → `ConflictException` 409 "Email '...' is already registered." (also catches Postgres unique-violation 23505 as a fallback). Creates customer with `IsEmailVerified=false`. Generates unique 6-char code, `EmailVerificationToken` expires in **24h**. Sends verification email — if send fails, logs and still returns 201 (does not throw); response tells the client to use resend.

### POST `/verify-email` / GET `/verify-email?token=`
**Validation**: none — `token` plain required string, no format check.
**Business logic**: Token not found → 404. Already used (`UsedAt != null`) → 400. Expired → 400. Otherwise sets `IsEmailVerified=true`, `EmailVerifiedAt`. GET variant renders an HTML success/failure page instead of JSON, catching the same exceptions.

### POST `/resend-verification`
**Validation**: `email` required, valid format, max 255 chars.
**Business logic**: Unknown email → generic success message (no enumeration leak). Already verified → 400. Invalidates prior unused tokens, issues a new 24h token. Send failure → 400 "Could not send verification email...".

### POST `/login`
**Validation**: `email` required + valid format; `password` required (no length rule).
**Business logic**: Wrong email/password → `UnauthorizedException` 401 "Invalid email or password." (generic). Unverified email → 400. Token issuance (`LoginCommandHandler.IssueTokensAsync`, shared with Google login) generates JWT + opaque refresh token, persists `RefreshToken` row.

### POST `/admin-login`
**Validation**: `username` required; `password` required.
**Business logic**: Looks up a separate `Admins` table (not Customers) — fully separate identity from customer login. Mismatch → 401 generic. JWT role = `"Admin"`; **no refresh token issued** (admin sessions are access-token-only).

### POST `/google-login`
**Validation**: none — `idToken` plain required string, no FluentValidation rule.
**Business logic**: Verifies Firebase ID token via `IFirebaseAuthService`; invalid → 401. Missing email in Firebase payload → 400. Account linking: matches by `GoogleId` first, then falls back to matching by email (auto-links an existing password account to Google on first Google sign-in, silently sets `GoogleId`). No match → auto-creates customer with a random unusable BCrypt password placeholder. Existing customer deactivated → `ForbiddenException` 403. Delegates token issuance to `LoginCommandHandler.IssueTokensAsync`.

### POST `/refresh-token`
**Validation**: none.
**Business logic**: Token not found / revoked / expired → 401. Owner customer inactive → 403. **Rotation**: presented token is marked revoked, a brand-new access+refresh pair is issued (same TTL rules as login).

### POST `/logout`
**Validation**: none.
**Business logic**: If token found and not already revoked, marks it revoked; unknown/already-revoked token silently no-ops. Always 204.

### POST `/forgot-password`
**Validation**: none.
**Business logic**: Unknown email → same generic success message as the real case (anti-enumeration), nothing sent. Invalidates old unused reset tokens, issues a new code expiring in **1 hour** (shorter than the 24h verify-email token). Note: the reset email send call is **not** wrapped in try/catch — unlike register/resend, a send failure here propagates as an unhandled exception (500) instead of a friendly message.

### POST `/reset-password`
**Validation** (`ResetPasswordCommandValidator`): `token` required; `newPassword` required, min 8 chars, ≥1 uppercase, ≥1 digit; `confirmPassword` must equal `newPassword`.
**Business logic**: Handler re-checks the password match (redundant with validator) → 400. Token not found → 404; used → 400; expired → 400. Updates password hash, marks token used. **Side effect**: revokes all of the customer's active refresh tokens (forces logout everywhere).

### POST `/change-password` (Customer)
**Validation** (`ChangePasswordCommandValidator`): `currentPassword` required; `newPassword` required, min 8 chars, ≥1 uppercase, ≥1 digit.
**Business logic**: Wrong current password (or null hash, e.g. Google-only accounts) → 400 "Current password is incorrect." On success, revokes all active refresh tokens (same forced-logout-elsewhere behavior as reset-password).

---

## Account — `api/account` (auth required)

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| DELETE | `/` | Customer | — | `ApiResponse<string>` |
| PUT | `/deactivate/{customerId:guid}` | Admin | — | `ApiResponse<string>` (404 if customer not found) |

**Validation**: none on either endpoint (no body).
**Business logic**:
- `DELETE /` (self soft-delete): loads active customer by JWT id → 404 if missing/inactive; sets `IsActive=false` **and** `DeletedAt=UtcNow`; revokes all active refresh tokens.
- `PUT /deactivate/{customerId}` (admin): loads target regardless of current active state → 404 if missing; sets `IsActive=false` (does **not** set `DeletedAt` — distinguishes admin deactivation from self-delete); revokes all of the target's active refresh tokens.

---

## Profile — `api/profile` (role: Customer)

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<ProfileDto>` |
| PUT | `/` | `UpdateProfileRequest` | `ApiResponse<ProfileDto>` |
| PATCH | `/settings` | `UpdateProfileSettingsRequest` | `ApiResponse<ProfileDto>` |
| POST | `/avatar` | multipart `file` (JPEG/PNG/WebP, ≤5 MB) | `ApiResponse<string>` (avatar URL) |
| GET | `/income-allocation?month=` | query `month?` (yyyy-MM) | `ApiResponse<IncomeAllocationSummaryDto>` |
| POST | `/income-allocation` | `ScheduleIncomeAllocationRequest` | `ApiResponse<IncomeAllocationEntryDto>` |

**UpdateProfileRequest**: `{ fullName, monthlyIncomeExpected?, gender?, dateOfBirth?, onboardingDone?, needsPct?, wantsPct?, savingsPct? }`
**UpdateProfileSettingsRequest**: `{ theme?, notifBudgetThresholds? }`
**ScheduleIncomeAllocationRequest**: `{ monthlyIncome, needsPct, wantsPct, savingsPct }`
**IncomeAllocationEntryDto**: `{ effectiveMonth: "yyyy-MM", monthlyIncome, needsPct, wantsPct, savingsPct }`
**IncomeAllocationSummaryDto**: `{ current: IncomeAllocationEntryDto, pending: IncomeAllocationEntryDto | null }`

### PUT `/` (UpdateProfile)
**Validation** (`UpdateProfileCommandValidator`): `fullName` NotEmpty, max 100 chars. `monthlyIncomeExpected`, if provided, `>= 0`. The needs/wants/savings trio is **all-or-nothing** (`Must` rule: if any one is set, all three must be set) — "needsPct, wantsPct and savingsPct must be provided together." Each, when present, `InclusiveBetween(0, 100)`. **Sum-to-100 is enforced in the validator** (all three present ⇒ must sum to exactly 100).
**Business logic**: 404 if customer missing/inactive. **Onboarding lock**: if any allocation field (`monthlyIncomeExpected`/`needsPct`/`wantsPct`/`savingsPct`) is supplied **and** `customer.OnboardingDone == true`, throws `BusinessRuleException(..., "allocation_locked_use_schedule_endpoint")` → **422**. Rationale: those columns are the one-time onboarding default that `IncomeAllocationService` falls back to when no history row exists; editing them post-onboarding would retroactively drift already-effective months — use `POST /income-allocation` instead. Otherwise: `fullName` always updated (trimmed); `monthlyIncomeExpected`/`gender`/`dateOfBirth`/`onboardingDone` updated only if provided; the allocation trio assigned only if all three present (handler trusts the validator's sum check, does not re-verify).

### PATCH `/settings`
**Validation** (`UpdateProfileSettingsCommandValidator`): `notifBudgetThresholds`, if not null, must have exactly 2 elements; each value `> 0 and <= 100`; `thresholds[0] < thresholds[1]` (warning must be lower than exceeded).
**Business logic**: 404 if customer missing. **Upsert**: creates a `CustomerSetting` row on first call (no other code path creates it — first PATCH wins). Sets `theme` if provided, `notifBudgetThresholds` if not null; always stamps `UpdatedAt`.

### POST `/avatar`
**Validation** (`AvatarValidationRules.Validate`, inline, not FluentValidation): content type must be `image/jpeg`/`image/png`/`image/webp` (case-insensitive) else 400 "Only JPEG, PNG, and WebP images are allowed."; size `> 5 MB` else 400 "Avatar file size must not exceed 5 MB."; magic-byte sniff (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP `RIFF....WEBP`) must match the declared content type else 400 "File content does not match the declared image type."
**Business logic**: 404 if customer missing. If an avatar already exists, the **old file is deleted first**. New file stored at `{WebRootPath}/avatars/{Guid:N}{ext}` (ext from content type, default `.jpg`); stored/returned URL is the relative path `/avatars/{name}`.

### GET `/income-allocation?month=`
**Validation**: `month`, if provided, must be `yyyy-MM` (same format rule and error message as `GET /budgets?month=`'s `BudgetService.ResolveMonthWindow`) → 400 "Month must use yyyy-MM format." if not.
**Business logic** (`IncomeAllocationService`, ICT = UTC+7): without `month`: `current` = `GetEffectiveAsync(customerId, currentMonth)` — picks the row with the **largest `EffectiveMonth` ≤ month** (carry-forward, ordinal string compare on `yyyy-MM`); if none qualifies, falls back to the `Customer` row's own `MonthlyIncomeExpected`/`NeedsPct`/`WantsPct`/`SavingsPct` (the 50/30/20 onboarding defaults). `pending` = the row with `EffectiveMonth == nextMonth`, or `null`. **With `month`**: the same carry-forward resolution runs against the requested month instead of today's — lets the caller ask "what was the split in effect for month X?" for any past or future month — and `current.effectiveMonth` in the response reflects whichever row actually carried forward (may be earlier than the requested month), matching `GET /budgets/buckets?month=`'s existing resolution semantics. `pending` is always `null` when `month` is given — "next real calendar month's draft" isn't a meaningful concept relative to an arbitrary queried month.

### POST `/income-allocation` (schedule)
**Validation** (`ScheduleIncomeAllocationChangeCommandValidator`): `monthlyIncome >= 0`; each pct `InclusiveBetween(0, 100)`; sum-to-100 enforced in the validator.
**Business logic**: 404 if customer missing. **Always targets next calendar month** (`UtcNow.AddMonths(1)`), never the current one. If a draft row for next month already exists, revises it in place; otherwise creates one. The currently-locked month (or the onboarding fallback) is never touched by this call — it rolls over naturally once "current" queries land on/after the new row's `EffectiveMonth`.

---

## Categories — `api/categories`

> No FluentValidation validators exist. All validation runs inline in `CategoryService`/`CategoryRules` (`src/FinViet.Infrastructure/Services/CategoryService.cs`). Visibility rule: `custom_*` categories are scoped to their creator via an active `CustomerCategory` ownership row; a customer sees only their own custom categories plus the global seeded `cat_*` set. Requesting another customer's `custom_*` id returns 404, not 403.

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| GET | `/?type=` | any authenticated | query `type?` | `ApiResponse<CategoryResponse[]>` |
| GET | `/{id}` | any authenticated | — | `ApiResponse<CategoryResponse>` (404) |
| POST | `/` | Admin | `CreateCategoryRequest` | `ApiResponse<CategoryResponse>` (201) |
| POST | `/custom` | Customer | `CreateCustomCategoryRequest` | `ApiResponse<CategoryResponse>` (201) |
| PATCH | `/{id}` | Admin | `UpdateCategoryRequest` | `ApiResponse<CategoryResponse>` (404) |
| DELETE | `/{id}` | Admin | — | `ApiResponse<object?>` (404) |
| DELETE | `/custom/{id}` | Customer | — | `ApiResponse<object?>` |
| PUT | `/{id}/bucket` | Customer | `SetCategoryBucketRequest` | `ApiResponse<CategoryResponse>` |
| DELETE | `/{id}/bucket` | Customer | — | `ApiResponse<CategoryResponse>` |

**CategoryResponse**: `{ categoryId, categoryName, nameVi?, nameEn?, type, isMandatory, expenseClass?, icon?, color?, sortOrder? }`
**CreateCategoryRequest** (Admin): `{ categoryId?, categoryName?, nameVi?, nameEn?, type, isMandatory, expenseClass?, icon?, color?, sortOrder? }`
**UpdateCategoryRequest** (Admin): same, all optional, no `categoryId`.
**CreateCustomCategoryRequest** (Customer): `{ name, bucket: "needs"|"wants"|"savings", color? }` — no `type` (always `expense`); `icon` is device-local, never sent.
**SetCategoryBucketRequest**: `{ bucketId: "needs"|"wants"|"savings" }`

### GET `/` , GET `/{id}`
**Business logic**: List excludes `cat_savings_goal` (get-by-id does not). Custom categories visible only if caller has an active `CustomerCategory` row for that id; seeded categories visible to all. If the caller has an active bucket override for a category, `expenseClass` in the response reflects the override, not the category's global default.

### POST `/` (Admin)
**Validation**: name = first non-empty of `nameVi`/`categoryName`, else 400 "Category name is required." `type` must normalize to `income`/`expense` else 400 "Category type must be one of: income, expense." For `expense`, `expenseClass` required (400 "Expense class is required for expense categories.") and must be one of needs/wants/savings (400 "Expense class must be one of: needs, wants, savings."); `income` forces `expenseClass = null`. If `categoryId` supplied and it already exists → 400 "Category id already exists." Name must be unique (case-insensitive) per type → 400 "Category name already exists for this type."
**Business logic**: If no `categoryId` given, auto-generates slug `cat_<slugified-name-en>` (accent-stripped, non-alphanumeric → `_`), appending `_2`, `_3`... on collision.

### POST `/custom` (Customer)
**Validation**: `name.Trim()` required else 400 "Category name is required." `bucket` normalized to needs/wants/savings, same error format as admin create. Name uniqueness checked scoped to `type="expense"`.
**Business logic**: Always `type="expense"`, `isMandatory=false`, `icon=null`. `categoryId = "custom_" + Guid.NewGuid()`. Immediately seeds an active `CustomerCategory` override row (`source="system"`) with the chosen bucket — the category is usable immediately without a follow-up `PUT .../bucket` call, and this is also what makes it visible to the creator.

### PATCH `/{id}` (Admin)
**Validation**: `type`, if provided, re-normalized. `categoryName`/`nameVi`, if provided, must not be blank (400 "Category name cannot be empty."); uniqueness re-checked excluding self. `expenseClass`, if provided, normalized against the (possibly just-updated) type.
**Business logic**: Partial update — only non-null fields applied.

### DELETE `/{id}` (Admin) / DELETE `/custom/{id}` (Customer)
**Business logic**: Both check `Transactions.Any(t => t.CategoryId == categoryId)` and reject with 400 "Cannot delete category because it is referenced by transactions." if so; otherwise hard-delete. The customer variant additionally 404s if the id doesn't start with `custom_` or the caller has no active ownership row for it.

### PUT `/{id}/bucket` (Customer)
**Validation**: category must be `type="expense"` else 400 "Only expense categories can be assigned to a bucket."; category id must not be `cat_savings_goal` (case-insensitive) else 400 **"Saving goal contributions cannot be reassigned to a different bucket."** — goal contributions must always flow to the savings bucket. `bucketId` normalized to needs/wants/savings.
**Business logic**: Upserts the caller's `CustomerCategory` row (`source="request"`).

### DELETE `/{id}/bucket` (Customer)
**Business logic**: If an active override exists, sets `IsActive=false` (reverts to global default); no-op (not an error) if none exists.

---

## Transactions — `api/transactions` (role: Customer)

> No FluentValidation validators exist. All validation is inline in `TransactionRules` (static class in `TransactionHandlers.cs`) and `TransactionRepository`.

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | `TransactionQueryDto` (query params) | `ApiResponse<PagedResult<TransactionResponseDto>>` |
| GET | `/summary?year=&month=` | query `year`, `month` | `ApiResponse<TransactionSummaryResponseDto>` |
| GET | `/{id:guid}` | — | `ApiResponse<TransactionResponseDto>` |
| POST | `/` | `CreateTransactionDto` + header `Idempotency-Key?` | `ApiResponse<TransactionResponseDto>` (201) |
| PUT | `/{id}` | `UpdateTransactionDto` | `ApiResponse<TransactionResponseDto>` |
| DELETE | `/{id}` | — | `ApiResponse<bool>` |
| PATCH | `/{id}/classify` | `ClassifyTransactionDto` | `ApiResponse<TransactionResponseDto>` |

**TransactionQueryDto** (query params)
```ts
{
  page: number = 1, pageSize: number = 20,   // pageSize clamped to 1–100
  walletId?: Guid, type?: string,            // "expense" | "income" | "transfer_out" | "transfer_in" (case-insensitive)
  categoryId?: string, from?: DateTime, to?: DateTime,
  q?: string,               // free-text ILIKE search — see note below
  uncategorizedOnly: boolean = false
}
```
> **Doc/code correction**: `q` searches `Description` and `Merchant` (Postgres `ILIKE '%term%'`), **not** `Note`/`BeneficiaryName` as a stale comment on the DTO claims — build the mobile search UI against `description`/`merchant`.

**CreateTransactionDto**: `{ walletId, categoryId?, transactionType, amount, transactionDate, note?, description?, merchant?, entryMethod? }`
**UpdateTransactionDto**: `{ categoryId?, amount?, merchant?, transactionDate? }` — **partial update**: a field left `null`/omitted is left unchanged. `walletId` and `transactionType` remain immutable after creation (out of scope for this endpoint).
**ClassifyTransactionDto**: `{ categoryId?: string }`

**TransactionResponseDto**: `{ transactionId, customerId, walletId, categoryId?, transactionType, sourceChannel, entryMethod, amount, transactionDate, note?, description?, merchant?, transferPairId?, externalId?, createdAt, updatedAt? }`

**TransactionSummaryResponseDto**: `{ income, expense, net, byCategory: {categoryId?, categoryName?, total}[], byDay: {date, income, expense, net}[], topBeneficiaries: {beneficiary, total}[] }`

### POST `/` (create)
**Validation**: `transactionType` normalized (`income`/`in`→income, `expense`/`out`→expense); anything else → 400 "Invalid transaction type '{x}'. Allowed values: income, expense." `transfer_out`/`transfer_in` explicitly rejected → 422 `transfer_managed` "Transfer legs are created only by the wallet transfer flow." `amount <= 0` → 400. `categoryId == "cat_income"` silently remapped to `cat_income_other`. `categoryId == "cat_savings_goal"` → 422 `goal_transaction_locked`. Unknown `categoryId` → 404. Category `type` mismatched with transaction type → 422 `category_type_mismatch`.
**Business logic**: Runs inside a DB transaction. **Idempotency**: `IdempotencyStore.ClaimAsync` keyed on `customerId + "transaction-create" + Idempotency-Key + hash(walletId, categoryId, transactionType, amount, transactionDate, note)`; a replayed key returns the stored response without re-running side effects. **Wallet lock**: raw `SELECT ... FOR UPDATE`; missing/not-owned/deleted wallet → 404. **`linked_wallet_read_only`**: if wallet type is `sepay_linked` → 422 "Bank-linked wallets are read-only. Transactions are created by synchronization." Balance updated in the same lock; would-go-negative → 422 `insufficient_balance`. `entryMethod` normalizes to `manual` (default) / `csv_import` / `sms_paste` / `sepay_sync` / `finverse_sync` / `photo`; `sourceChannel` in the response is just this same value. **Auto-categorization**: only runs when `categoryId` omitted and type is `expense` — synchronous rule-based lookup via `IMerchantRuleService.ResolveAsync` (merchant-keyword rules, not AI); an incompatible match is silently ignored (transaction stays uncategorized) rather than blocking creation. Budgets are re-synced for expense transactions (best-effort, swallows errors).

### PUT `/{id}`
**Validation**: transaction must exist and be owned (directly, or via the wallet) → 404 either way (no existence-leak). `EnsureNotTransfer` blocks editing transfer legs at all (any field) → 422 `transfer_managed`. If `amount` provided: `amount <= 0` → 400 (same rule as create). If `categoryId` provided: re-validated with the same rules as create — unknown category → 404, `cat_income` → `cat_income_other` remap, `cat_savings_goal` → 422 `goal_transaction_locked`, type mismatch → 422 `category_type_mismatch`. **`synced_transaction_fields_locked`**: if the transaction's wallet is `sepay_linked` and the request includes any of `amount`/`merchant`/`transactionDate` → 422 "Only the category can be changed for transactions from a bank-synced wallet." — category alone is still editable on a synced transaction; wallet type is immutable post-creation, so this is checked once, unlocked, before opening the write transaction (no race).
**Business logic**: Runs inside a DB transaction with the same wallet row-lock pattern as create/delete (only acquired when `amount`/`merchant`/`transactionDate` is actually being changed — a category-only edit doesn't lock the wallet). If `amount` changed: reverses the old balance delta on the wallet and applies the new one; would-go-negative → 422 `insufficient_balance` (same code as create, reused). `merchant`/`transactionDate`, if provided, are written directly. `categoryId`, if provided, is written directly (no separate rule-service interaction — classifying via `PUT` never creates/updates a beneficiary rule). If the resulting/unchanged type is `expense`, budgets are re-synced for that month afterward.

### PATCH `/{id}/classify`
**Validation**: same ownership/transfer/category rules as `PUT` above, but this endpoint only ever accepts `{ categoryId? }` — **no partial-update semantics**: whatever `categoryId` value is sent (including `null`) always overwrites the stored category.
**Business logic**: Calls the same underlying `ClassifyAsync(transactionId, categoryId, ...)` repository path used before the `PUT` rework — sets only `categoryId` + `updatedAt`, no wallet lock, does not check `synced_transaction_fields_locked`/`synced_transaction_locked` (category is always editable regardless of wallet type). Never creates/updates a beneficiary rule (only `POST /ai/transactions/{id}/override`... actually see AI section: even override no longer does this, see note there). If the resulting type is `expense`, budgets are re-synced for that month.

### DELETE `/{id}`
**Business logic**: Runs in a DB transaction; not-found/not-owned → 404. **`synced_transaction_locked`**: if `entryMethod == "sepay_sync"` → 422 "Provider-synced transactions cannot be deleted." Wallet(s) row-locked; balance reversed (income reversed as `-amount`, expense as `+amount`); would-go-negative → 422 `reversal_insufficient_balance`. **Transfer pairs**: if `transferPairId` set, both legs must resolve to exactly one `transfer_out` + one `transfer_in` else 422 `transfer_pair_invalid`; both wallets must still be owned by the customer else 422 `transfer_wallet_missing`; either leg going negative on reversal → 422 `transfer_reversal_insufficient_balance`. Both legs deleted together.

### GET `/summary`
**Validation**: `month` must be in [1,12] → 400.
**Business logic**: **Excludes `transfer_out`/`transfer_in`** from all aggregates. `byCategory` groups expenses only, sorted desc by total. `byDay` groups all income+expense rows by calendar day. `topBeneficiaries` groups expense rows with non-blank `merchant`, sorted desc, top 10.

### GET `/` (list)
**Business logic**: `pageSize` clamped 1–100 (defaults 20 if out of range); `page < 1` → 1. `type` filter validated against the 4-value enum (400 if invalid). `uncategorizedOnly` = `categoryId == null` **and excludes both transfer types**. `q` uses Postgres `ILIKE` over `description`/`merchant` (see correction above). `to` is treated as an exclusive next-day boundary. Ordered `transactionDate DESC, transactionId DESC`.

---

## Wallets — `api/wallets` (role: Customer; SePay webhook is anonymous)

> No FluentValidation validators exist. All validation is inline in `WalletRules` (`src/FinViet.Infrastructure/Services/WalletRules.cs`) and `WalletService`/`SepayWalletService`.

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<WalletListResponse>` |
| POST | `/` | `CreateWalletRequest` | `ApiResponse<WalletResponse>` (201) |
| GET | `/{id:guid}` | — | `ApiResponse<WalletResponse>` (404) |
| PATCH | `/{id:guid}` | `UpdateWalletRequest` | `ApiResponse<WalletResponse>` (404) |
| DELETE | `/{id:guid}` | — | 204 (404 if not found) |
| POST | `/transfer` | `TransferWalletRequest` + `Idempotency-Key` | `ApiResponse<TransferWalletResponse>` |
| POST | `/withdraw` | `WithdrawWalletRequest` + `Idempotency-Key` | `ApiResponse<WithdrawWalletResponse>` |
| GET | `/{id:guid}/transactions` | `WalletTransactionQuery` (query) | `ApiResponse<PagedResult<WalletTransactionResponse>>` |
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
| POST | `/sepay/webhook` **(Anonymous, `Authorization: Apikey <key>`)** | `SepayWebhookRequest` | `ApiResponse<SepayWebhookResult>` |

**CreateWalletRequest**: `{ walletName, walletType, initialBalance }`
**UpdateWalletRequest**: `{ walletName?, walletType? }` — `walletType` must be omitted; sending any value is rejected (see below).
**WalletResponse**: `{ walletId, customerId, walletName, walletType, balance, sepayBankAccountId?, institutionName?, accountMask?, authMode?, lastSyncedAt? }`
**WalletListResponse**: `{ totalBalance, wallets: WalletResponse[] }`
**TransferWalletRequest**: `{ fromWalletId, toWalletId, amount, description? }`
**WithdrawWalletRequest**: `{ fromWalletId, toWalletId?, amount, description? }`
**WalletTransactionQuery**: `{ page=1, pageSize=10, fromDate?, toDate?, categoryId?, transactionType?, sortOrder="desc" }`

**SePay DTOs** (unchanged from prior reference — see original field lists):
`SepayAuthorizeUrlResponse`, `SepayBankAccountsRequest/Response`, `LinkSepayAccountRequest/TokenRequest`, `SepayLinkResult`, `SepayLinkStatusResponse`, `SepayWebhookRegistrationResponse`, `SepayWalletSyncResponse`, `SepaySyncAllResponse`, `SepayUnlinkResponse`, `SepayWebhookRequest/Result`.

### Wallets core

**POST `/` (create)** — Validation: `walletName` required (non-blank); `walletType` required and must normalize to `"basic"` (legacy values `CASH`/`BANK_ACCOUNT`/`CREDIT_CARD`/`E_WALLET`/`INVESTMENT` also normalize to `basic`) else 400 "Wallet type must be one of: basic."; `initialBalance` cannot be negative; name (trimmed) must be unique per customer (case-insensitive) else 400; **max 10 wallets/customer** else 400 "Maximum 10 wallets allowed per account." Business logic: `balance = initialBalance` at creation, no locking needed.

**PATCH `/{id}` (update)** — Validation: `walletName`, if provided, non-blank; `walletType` **must be null** — any value → 400 "Wallet type cannot be changed after creation."; name-uniqueness re-checked. Business logic: only the name is mutable.

**DELETE `/{id}`** — Business logic: soft delete (`isDeleted=true`), history preserved. Blocks deleting the customer's **last active wallet** → 422 `last_wallet`. No explicit block on nonzero balance or on `sepay_linked` type — a linked wallet can be deleted like any other as long as it isn't the last one.

**POST `/transfer`** — Validation: `fromWalletId != toWalletId` (400); `amount > 0` (400); `Idempotency-Key` required (400 if missing). Business logic: `ReadCommitted` transaction. Idempotency claim keyed on `(customerId, "wallet-transfer", key)`; a concurrent duplicate blocks on `FOR UPDATE` then replays or 409s ("already used with a different request payload" / "still being processed"). Both wallet rows locked via inline raw-SQL `FOR UPDATE ... ORDER BY id` (deterministic order to avoid deadlocks). **`sepay_wallet_read_only`**: either wallet being `sepay_linked` → 422 "SePay-linked wallets are read-only and cannot participate in manual transfers." Insufficient balance → 400. Writes a paired `transfer_out`/`transfer_in` row sharing one `transferPairId`.

**POST `/withdraw`** — Validation: `amount > 0` (400); `toWalletId != fromWalletId` (400); `Idempotency-Key` required. Business logic: same locking/idempotency pattern (`operation="wallet-withdraw"`). **`withdraw_source_not_sepay`**: source must be `sepay_linked` else 422 — rationale: withdrawal represents money actually leaving the bank account, so only a real bank-linked wallet can be the source. Insufficient balance → 400. **`withdraw_target_sepay_read_only`**: a `sepay_linked` receiving wallet is rejected (422) — it can't receive a withdrawal. Source booked as `expense`; receiving wallet (if any) booked as `income`; with no receiving wallet, funds simply leave tracked wallets.

**GET `/{id}/transactions`** — Validation: `page <= 0` → 400; `pageSize` must be 1–100 → 400; `transactionType`, if given, must be one of `income`/`expense`/`transfer_out`/`transfer_in`. Wallet must exist → 404.

### SePay linking

**GET `/sepay/authorize-url`** — Business logic: requires `ClientId`/`RedirectUri` configured, else 503 `sepay_not_configured`. `state` = a signed, single-customer, expiring token (`ISepayLinkStateProtector`), lifetime clamped 1–30 min (`SepayOptions.LinkStateLifetimeMinutes`).

**POST `/sepay/bank-accounts`** — Validation: `code` required; `state`, if present, must be a valid signature belonging to the caller else 422 `sepay_state_invalid`. Business logic: exchanged OAuth token is cached under `sepay:code:{customerId}:{sha256(code)}` for **5 minutes** (SePay codes are single-use, so the same code can serve both this call and the subsequent `link` call). `alreadyLinked` flags accounts already tied to a `SepayLink` for this customer.

**POST `/sepay/link` (OAuth)** — Validation: `code` required; `state` validated as above; requires ≥1 active bank account (400 "No active bank accounts found on your SePay account."); `bankAccountId`, if given, must exist/be active (404). Business logic: fetches full transaction history *before* opening the DB transaction (avoids holding a `Serializable` tx open across many outbound calls). DB work in a `Serializable` transaction. Only `basic`-type wallets count toward the 10-wallet cap (422 if exceeded) — linked wallets never count. Re-linking an already-linked `SepayBankAccountId` reuses the wallet and just refreshes tokens/balance. New link creates a `Wallet` (`walletType="sepay_linked"`, name `"SePay - {bank}"`, truncated to 120 chars) and a `SepayLink` (`authMode="oauth"`); tokens stored encrypted. Imports fetched history via upsert, runs AI categorization for new expenses post-commit, then best-effort auto-registers a webhook if `SePay:WebhookUrl`/`WebhookApiKey` are configured (failure never fails the link).

**POST `/sepay/link-token` (static)** — Validation: `apiToken` required; SePay rejecting it → 400 "The SePay API token is invalid or expired." Business logic: no code/state exchange; stores the raw token itself (encrypted) as the "access token", no refresh token, no expiry ("static tokens do not expire"). `sepayBankAccountId = 0` (no numeric id available for static links). Re-link matched on `(authMode="static", accountNumber)` instead of bank-account id. No auto webhook registration (static links can't hold webhook scopes).

**GET `/sepay/links`** — Business logic: `relinkRequired` = true if no access token stored, or (OAuth only, no refresh token) once `accessTokenExpiresAt <= now`; static links are never relink-required while a token is present. `webhookRegistered = sepayWebhookId.HasValue`.

**POST `/{id}/sepay-sync`, POST `/sepay/sync-all`** — Business logic: `fromDate` = `lastSyncedAt - overlapDays` (clamped 0–90) to re-pull a few overlapping days (dedup absorbs re-fetches). OAuth tokens refreshed only within `AccessTokenExpirySkewSeconds` of expiry; refresh failure → 422 `sepay_relink_required`. `transactionsCreated` vs `transactionsUpdated` derived from the upsert's `INSERT ... ON CONFLICT (external_id) ... RETURNING (xmax = 0) AS inserted` — `xmax=0` = newly inserted (created), conflict-triggered update = updated. Wallet balance is overwritten by SePay's reported balance (authoritative). `sync-all` iterates each linked wallet, capturing per-wallet failures into `failures[walletId]` so one bad link doesn't block the rest.

**POST/DELETE `/{id}/sepay-webhook`** — Business logic: static links rejected → 422 `sepay_webhook_requires_oauth` (the static token only authenticates `/userapi`, never webhook-management scopes). Requires `WebhookApiKey` configured (422 `sepay_webhook_disabled`) and a public http(s), non-loopback `WebhookUrl` (422 `sepay_webhook_url_missing` / `sepay_webhook_url_invalid`). Idempotent: if a matching registration already exists for that bank account + URL (trailing-slash/case-insensitive compare), it's adopted (`alreadyExisted=true`) instead of duplicated.

**POST `/sepay/webhook` (inbound, Anonymous)** — Validation/auth: requires `WebhookApiKey` configured server-side (else 503-equivalent `sepay_webhook_disabled`). `Authorization: Apikey <key>` header compared with `CryptographicOperations.FixedTimeEquals` — mismatch → 401. `payload.id <= 0` → 400. Both `accountNumber` and `subAccount` missing → 400. `transferType` must be exactly `"in"`/`"out"` (case-insensitive) else 400 — direction is never guessed. Business logic: matches `SepayLink.accountNumber` against `accountNumber` OR `subAccount`; zero or multiple matches → `outcome="ignored"` (acknowledged so SePay stops retrying; a later regular sync will still pick it up correctly for the multi-match case). Dedup via the same `external_id = "sepay:{id}"` upsert used by sync, so webhook redelivery and later sync both converge safely. Wallet balance updated to `payload.accumulated` only if `> 0` (some gateways omit it, deserializing as 0 — treated as non-authoritative in that case). `outcome`: `"ignored"` if the upsert returned no row (e.g. `external_id` owned by another customer, or a zero-amount row skipped), else `"created"`/`"updated"` per the same xmax logic as sync.

**DELETE `/{id}/sepay-link` (unlink)** — Business logic: best-effort deletes the registered webhook on SePay (failure only logged; skipped entirely for static links). Converts wallet back to `walletType="basic"`, removes the `SepayLink` row. `transactionsRetained` = count of `entryMethod="sepay_sync"` transactions on the wallet — history is never deleted, only the link/authorization record is.

---

## Budgets — `api/budgets` (role: Customer)

> No FluentValidation validators exist. All validation is inline in `BudgetService` (`src/FinViet.Infrastructure/Services/BudgetService.cs`).

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?month=` | query `month?` (yyyy-MM) | `ApiResponse<BudgetResponse[]>` |
| GET | `/buckets?month=` | query `month?` (yyyy-MM) | `ApiResponse<BucketSummaryListResponse>` |
| POST | `/` | `UpsertBudgetRequest` | `ApiResponse<BudgetResponse>` |
| PATCH | `/{id:guid}` | `UpdateBudgetRequest` | `ApiResponse<BudgetResponse>` |
| DELETE | `/{id:guid}` | — | 204 |

**BudgetResponse**
```ts
{
  id, categoryId, categoryName, walletId?, monthlyLimit, spent, remaining, percentage,
  status: "GREEN" | "YELLOW" | "RED",   // correction: NOT "ok"/"warning"/"exceeded"
  bucket: "needs" | "wants" | "savings"
}
```
**UpsertBudgetRequest**: `{ categoryId, walletId?, monthlyLimit }`
**UpdateBudgetRequest**: `{ monthlyLimit }`
**BucketSummaryListResponse**: `{ month, monthlyIncome, budgetAdherenceScore, uncategorizedRatio, uncategorizedWarning, buckets: BucketSummaryResponse[] }`
**BucketSummaryResponse**: `{ bucket, allocationPct, allocationCap, categoryLimitTotal, spent, remaining, percentage, overAllocated, expectedSpent, paceDeviation, paceStatus }`

### POST `/` (upsert) / PATCH `/{id}`
**Validation**: `monthlyLimit <= 0` → 400 "Monthly limit must be greater than 0." `categoryId` required and must exist (404); must be `type="expense"`; cannot be `cat_savings_goal` or the "Chưa phân loại" (uncategorized) category → 400 "This category cannot be budgeted directly." Category must be active in the customer's category set (auto-seeded from all expense categories on first use) → 400 "Category is not available for this customer." `walletId`, if given, must belong to the customer and not be deleted.
**Business logic**: Upsert key = `(customerId, categoryId, walletId)` — a wallet-scoped budget is distinct from the wallet-less budget for the same category; matching row updated (`monthlyLimit` overwritten, `lastAlertThreshold` reset to 0), else inserted. **True upsert, never conflicts.** `PATCH` only changes `monthlyLimit` (and resets the alert threshold) — `categoryId`/`walletId` are immutable after creation.

### GET `/` / GET `/buckets`
**Business logic**:
- `status`: derived via a fixed 80% warning threshold — `>= 100% → RED`, `>= 80% → YELLOW`, else `GREEN`.
- **Alert notifications** (not on GET — triggered by `SyncBudgetOnTransactionChangeAsync` on transaction create/update/delete): reads `CustomerSettings.notifBudgetThresholds` (`[warning, exceeded]`, falls back to `[80, 100]` if unset/malformed). When usage crosses a threshold higher than the budget's `lastAlertThreshold`, inserts a `Notification` (`type="budget_alert"`) and calls the Firebase push notifier; the threshold resets to 0 once usage drops back below the warning line.
- **Carry-forward income allocation**: same `ResolveEffectiveRow` logic as Profile — the row with the largest `effectiveMonth ≤ month`, else the customer's live default columns.
- `uncategorizedRatio = uncategorizedSpent / totalSpent * 100` (0 if no spend); `uncategorizedWarning = ratio > 20`. "Uncategorized" = `categoryId == null` or the "Chưa phân loại" category.
- `budgetAdherenceScore` = weighted average of a pacing score over **needs/wants only** (savings excluded), weighted by each bucket's `allocationPct`; pacing score is 100 if `deviation <= 0`, else `max(0, min(100, 100 - deviation*100))`.
- `expectedSpent = limit * elapsedDays / totalDays`, `limit = max(allocationCap, categoryLimitTotal)`, "today" computed in ICT (UTC+7); clamped to 0 before period start, full period after.
- `paceDeviation = (actual - expected) / expected` (special-cased when `expected <= 0`); `paceStatus`: `deviation <= -0.15 → "UNDER_PACE"`, `deviation <= 0 → "ON_TRACK"`, `> 0 → "OVER_PACE"`.
- `overAllocated = categoryLimitTotal > allocationCap && allocationCap > 0`.

---

## Saving Goals — `api/saving-goals` (role: Customer)

> No FluentValidation validators exist. All validation is inline in `SavingGoalService` (`src/FinViet.Infrastructure/Services/SavingGoalService.cs`).

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<SavingGoalResponse[]>` |
| GET | `/{id:guid}` | — | `ApiResponse<SavingGoalResponse>` (404) |
| POST | `/` | `CreateSavingGoalRequest` + `Idempotency-Key` | `ApiResponse<SavingGoalResponse>` (201) |
| PATCH | `/{id:guid}` | `UpdateSavingGoalRequest` | `ApiResponse<SavingGoalResponse>` (404) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |
| POST | `/{id:guid}/contribute` | `ContributeSavingGoalRequest` + `Idempotency-Key` | `ApiResponse<SavingGoalResponse>` (404) |
| POST | `/{id:guid}/withdraw` | `WithdrawSavingGoalRequest` + `Idempotency-Key` | `ApiResponse<SavingGoalResponse>` (404) |
| GET | `/{id:guid}/contributions` | — | `ApiResponse<SavingGoalContributionResponse[]>` (404) |

**CreateSavingGoalRequest**: `{ goalName, targetAmount, deadline?, initialAmount?, fundingWalletId? }`
**UpdateSavingGoalRequest**: `{ goalName?, targetAmount?, deadline? }`
**ContributeSavingGoalRequest**: `{ amount, fundingWalletId?, note? }`
**WithdrawSavingGoalRequest**: `{ amount, walletId, note? }` — `walletId` is **required on every call**; a goal has no static withdrawal wallet (mirrors the per-action wallet choice on contribute — see below).
**SavingGoalResponse**: `{ goalId, customerId, goalName, targetAmount, currentAmount, deadline?, fundingWalletId?, remainingAmount, progressPercent, daysRemaining?, isCompleted, monthlySavingNeeded?, monthsRemaining? }`
**SavingGoalContributionResponse**: `{ contributionId, goalId, amount, type: "contribution" | "withdrawal", contributedAt, note?, transactionId? }` — `amount` is always stored positive; direction comes from `type`. Backed by a new `type` column on `savings_goal_contributions` (migration `V24`, must be run manually — see Error codes section note); `note` reuses a `varchar(255)` column that already existed in the v3 baseline schema.

### POST `/` (create)
**Validation**: `goalName` required; `targetAmount <= 0` rejected; `deadline` required and must be strictly **future** (`> today`); `initialAmount < 0` rejected; `initialAmount > targetAmount` rejected; duplicate `goalName` (case-insensitive) among the customer's non-deleted goals rejected.
**Business logic**: `Idempotency-Key` **required** (400 if missing, max 200 chars); replay via `IdempotencyStore` keyed `(customerId, "saving-goal-create", key)`. `fundingWalletId`, if set, must belong to the customer, not be deleted, and not be `sepay_linked` (422 `goal_funding_wallet_sepay_unsupported`), but is only actually **debited if `initialAmount > 0`**: locks the wallet, checks `balance >= amount`, decrements it, records an expense `Transaction` (`categoryId="cat_savings_goal"`) plus a `SavingGoalContribution` row (`type="contribution"`). If `initialAmount` is 0/absent, no debit occurs even with a funding wallet set. Milestone notifications (25/50/75/100%) fire after commit if `initialAmount > 0`.

### PATCH `/{id}`
**Validation**: `targetAmount <= 0` rejected; new `targetAmount` cannot be less than current `currentAmount`; `deadline` must be strictly future; name uniqueness re-checked on rename.

### POST `/{id}/contribute`
**Validation**: `amount <= 0` rejected; `amount > targetAmount - currentAmount` → 422 `goal_remaining_exceeded` (cannot overshoot the target). `fundingWalletId`, if provided, must belong to the customer, not be deleted, and not be `sepay_linked` → 422 `goal_funding_wallet_sepay_unsupported` "Contributions can only be funded from a regular wallet, not a bank-linked one." `note`, if provided, is trimmed and capped at 255 chars (the DB column's actual limit) → 400 if longer.
**Business logic**: `Idempotency-Key` required, keyed `(customerId, "saving-goal-contribute:{goalId}", key)`. **Per-action wallet choice**: wallet resolution is request's `fundingWalletId` if provided, else the goal's stored `fundingWalletId` (a creation-time pre-fill/fallback only, never the sole mechanism — there is intentionally no "change the goal's funding wallet" endpoint); **if neither resolves, no wallet debit happens — `currentAmount` is simply incremented**. When a wallet is used: locked, balance-checked (422 `insufficient_balance` if short), debited, expense `Transaction` + `SavingGoalContribution` (`type="contribution"`, `note` persisted) recorded. `isCompleted = currentAmount >= targetAmount` re-evaluated after each contribution. Milestone notifications (25/50/75/100%, distinct "goal completed" copy at 100%) fire whenever the percentage crosses a threshold between the previous and new amount.

### POST `/{id}/withdraw`
**Validation**: `amount <= 0` → 400. `amount > currentAmount` → 422 `goal_withdraw_exceeds_saved` "Cannot withdraw more than the goal's current saved amount." `walletId` must belong to the customer and not be deleted → 404 if not. `walletId` must **not** be `sepay_linked` → 422 `goal_withdraw_target_sepay_unsupported` "Withdrawals can only go to a regular wallet, not a bank-linked one." (same rationale as the wallet-level `POST /wallets/withdraw` feature — crediting a bank-synced wallet manually would desync it from the real account.) `note`, if provided, same 255-char cap as contribute.
**Business logic**: `Idempotency-Key` **required**, keyed `(customerId, "saving-goal-withdraw:{goalId}", key)` — same claim/replay pattern as contribute. DB transaction, row-locks the goal and the target wallet. Credits `wallet.balance += amount` via an **income** `Transaction` (`categoryId="cat_savings_goal"`), linked through the contribution row's `transactionId`. Decrements `goal.currentAmount -= amount`; re-evaluates `isCompleted = currentAmount >= targetAmount` — a withdrawal can drop a completed goal back to incomplete. Inserts a `SavingGoalContribution` row with `type="withdrawal"`, `amount` stored positive (direction comes from `type`, not sign). No milestone notifications fire for a withdrawal (the milestone check only fires going *up* through a threshold).

### GET `/{id}/contributions`
**Validation**: goal must exist and be owned by the caller → 404 (same no-existence-leak pattern as the other goal endpoints).
**Business logic**: Reads `SavingGoalContribution` rows for the goal, ordered newest (`contributedAt`) first. Returns both contributions and withdrawals in one combined, chronologically-ordered ledger.

### DELETE `/{id}`
**Business logic**: Runs in a DB transaction, row-locks the goal. All linked `SavingGoalContribution` rows and their `Transaction`s are found; each must be `cat_savings_goal` and either `expense` (contribution) or `income` (withdrawal), else 422 `goal_ledger_invalid`. Their wallets are locked and reversed **according to each entry's direction**: a contribution (expense) is refunded back (`wallet.balance += amount`); a withdrawal (income) has its credit undone (`wallet.balance -= amount`) — the net effect always equals refunding exactly the goal's still-unwithdrawn `currentAmount`. If undoing a withdrawal would drive its destination wallet negative (the withdrawn cash was already spent elsewhere) → 422 `goal_ledger_reversal_insufficient_balance`. A wallet deleted in the meantime → 422 `goal_wallet_missing`. Transactions, contributions, and the goal row are then all deleted together.

### `monthlySavingNeeded` / `monthsRemaining`
Only computed when `deadline` is set. `monthsRemaining` = whole calendar months to deadline (floored at 0, decremented by 1 if `deadline.Day < today.Day`). `remaining = max(0, targetAmount - currentAmount)`. `monthlySavingNeeded`: `0` if `remaining <= 0`; `remaining / monthsRemaining` (2dp) if `monthsRemaining >= 1`; else the whole `remaining` amount (due immediately, deadline within the current month).

---

## Rules — `api/rules` (role: Customer) — merchant-keyword auto-categorization

> No FluentValidation validators exist. All validation is inline in `MerchantRuleService`.

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/` | — | `ApiResponse<RuleResponse[]>` |
| POST | `/` | `CreateRuleRequest` | `ApiResponse<CreateRuleResponse>` (201; 409 on keyword conflict) |
| DELETE | `/{id:guid}` | — | `ApiResponse<object?>` (404) |

**CreateRuleRequest**: `{ merchantKeyword, categoryId }`
**RuleResponse**: `{ ruleId, merchantKeyword, categoryId, categoryName?, appliedCount, createdAt }`
**CreateRuleResponse**: `{ rule: RuleResponse, appliedCount }`

### GET `/`
**Business logic**: Scoped to the caller's `customerId`. Ordered by `merchantKeyword.Length` desc, then `createdAt` desc (longest/most-specific keyword first).

### POST `/`
**Validation**: `merchantKeyword.Trim()` non-empty → 400 "Merchant keyword is required."; `categoryId` non-empty → 400 "Category id is required."; category must exist → 404; category must not be `cat_savings_goal` → 400 "This category cannot be assigned by a rule." **Conflict**: an **exact** (not substring) case-insensitive match on the caller's existing keywords → **409** "A rule for this merchant keyword already exists."
**Business logic** — retroactive apply, synchronous, runs before save in the same request: candidate transactions are `transferPairId == null` (fund-transfer legs excluded), owned by the customer, and `ILIKE '%keyword%'` matched against `merchant` OR `description`. For each candidate, the winning rule among **all** the customer's rules (existing + new) is re-resolved by longest-keyword-wins substring match (tie-broken by newest `createdAt`); a transaction is only reassigned if the new rule actually wins and its current `categoryId` differs. Reassignment clears `isAiClassified`/`aiConfidence`. `appliedCount` = number of transactions actually changed, returned and persisted on the rule.

### DELETE `/{id}`
**Business logic**: Unknown id → 404. Belongs to a different customer → **403** `ForbiddenException` "You do not own this rule." (not 404, unlike most other ownership checks in this codebase). Otherwise hard-deleted.

---

## Notifications — `api/notifications` (role: Customer)

> No FluentValidation validators exist. `unread` binds as a plain query `bool`; `{id:guid}` route constraint rejects malformed ids at the routing layer.

| Method | Path | Request | Response |
|---|---|---|---|
| GET | `/?unread=` | query `unread: boolean` | `ApiResponse<NotificationResponse[]>` |
| PATCH | `/{id:guid}/read` | — | `ApiResponse<object?>` (404) |
| POST | `/read-all` | — | `ApiResponse<{ count: number }>` |

**NotificationResponse**: `{ notificationId, type, title, message?, entityType?, entityId?, isRead, sentAt? }`

**Business logic**:
- Ownership: `MarkAsReadAsync` filters by `customerId == callerId`, so another customer's notification 404s rather than 403s.
- **Creation triggers** (i.e. what actually writes to this table):
  - `BudgetService` → `type="budget_alert"` when spend crosses a customer-configured threshold (see Budgets section); has its own dedup via `lastAlertThreshold`/`lastAlertMonth`.
  - `SavingGoalService.NotifyMilestonesAsync` → `type="announcement"` on goal milestone crossings, `entityType="goal"`.
  - **Weekly AI reports do NOT write to this table** — they're persisted separately to `AiWeeklyReports` (own `isRead` flag, exposed only via `GET /api/ai/reports`), so a new weekly report is invisible to `GET /api/notifications`.
- **Push delivery is partial**: the generic `NotificationService.NotifyAsync` push path (used by goal milestones) is currently a **stub/no-op** — it only logs, since no device-token store exists yet. `BudgetService`'s budget-alert path bypasses that generic service and calls `FirebaseBudgetAlertNotifier` directly, which **is** a real FCM topic-send (`customer-{customerId}`) when `Firebase:ServiceAccountJsonPath`/`GOOGLE_APPLICATION_CREDENTIALS` is configured — otherwise it logs and no-ops. **`GET /api/notifications` (pull) is the only reliably populated channel** for the mobile client to rely on; treat push as best-effort/partial.
- `MarkAsReadAsync` is idempotent (no-op write if already read). `MarkAllAsReadAsync` returns the count actually flipped (0 if nothing was unread).

---

## Extract — `api/extract` (role: Customer) — parse-only, nothing persisted

> No FluentValidation validators or data annotations exist. All limits are `const`s checked inline in `ExtractController`. **Confirmed parse-only**: no `SaveChanges`/`Add` calls exist anywhere in the SMS/CSV/photo call paths (verified directly in `TransactionExtractService`, the parsers, and `AiCategorizationService.PreviewAsync`).

| Method | Path | Request | Response |
|---|---|---|---|
| POST | `/sms` | `{ text: string }` | `ApiResponse<ExtractResponse>` |
| POST | `/csv` | multipart: `file` + `maxRows?: number` | `ApiResponse<ExtractResponse>` |
| POST | `/photo` | multipart: `file` | `ApiResponse<ExtractResponse>` (503 `ocr_not_configured`) |

**ExtractResponse**: `{ rows: ExtractedTransactionItem[], totalScanned, skipped, errors: string[] }`
**ExtractedTransactionItem**: `{ amount, type, merchant?, description?, transactionDate, categoryId?, categoryName?, confidence? }`

### POST `/sms`
**Validation**: `MaxSmsTextLength = 20,000` chars (exact constant). Empty/whitespace → 400 "Vui lòng dán nội dung tin nhắn cần trích xuất." Over the limit → 400 "Nội dung quá dài (tối đa 20.000 ký tự). Hãy chia nhỏ và dán lại." — a hard length check on the raw string, not truncation.
**Business logic**: Pure regex parsing (`SmsTransactionParser`, no AI call for the parse itself). Splits on blank lines into individual messages; per message extracts amount (requires a VND/VNĐ/đ suffix — no match = skipped with a Vietnamese message counted in both `skipped` and `errors`), classifies income/expense via credit/debit keyword regex then sign-before-number fallback (defaults expense), extracts date via `dd/MM/yyyy[ HH:mm[:ss]]` (defaults to now), extracts a note after a "Nội dung/ND/Content" marker (falls back to the whole trimmed message). **Category population** (only for `EXPENSE` rows with a non-empty note): two-stage — (1) merchant-rule lookup (longest-keyword-first substring match) — **if a rule matches, `categoryId` IS populated** with `confidence=1.0`; (2) otherwise falls back to the AI preview call, which only returns `categoryName`/`confidence` (no id) — so `categoryId` is genuinely reserved/unset **only on the AI-fallback path**, not on the rule-match path. AI failures are caught/logged, leaving the row uncategorized (never fails the whole request).

### POST `/csv`
**Validation**: file required/non-empty → 400 "Vui lòng chọn tệp sao kê (.csv, .xlsx) để trích xuất."; size `> 5 MB` (`MaxCsvFileBytes`, exact) → 400 "Tệp quá lớn (tối đa 5 MB)."; extension must be `.csv`/`.xlsx`/`.xls` (filename check only, no content-type/magic-byte sniffing) → 400 otherwise; `maxRows`, if given, `< 1` → 400 "maxRows phải lớn hơn 0." (no upper bound).
**Business logic**: Parsed via `ExcelDataReader` (handles both xlsx and plain csv). **Columns mapped by fixed positional index, not header name**: col 1 = STT (row/header detector — non-numeric rows skipped silently), col 2 = date, col 5 = debit, col 6 = credit, col 11 = description, col 13 = correspondent/beneficiary. `maxRows` **truncates after parsing** (`Take(maxRows)`) — `totalScanned`/`skipped` still reflect the full file. No duplicate-detection logic exists (consistent with parse-only design).

### POST `/photo`
**Validation**: file required/non-empty → 400 "Vui lòng chọn ảnh hóa đơn để trích xuất."; size `> 8 MB` (`MaxPhotoFileBytes`, exact) → 400 "Ảnh quá lớn (tối đa 8 MB)."; extension must be `.jpg`/`.jpeg`/`.png`/`.heic` → 400 otherwise.
**Business logic**: Confirmed placeholder — `IReceiptOcrService` → `UnconfiguredReceiptOcrService` always throws `IntegrationUnavailableException` (503) `ocr_not_configured`, either "Receipt OCR is not configured on this server." or "OCR provider '{x}' has no implementation registered yet." No real OCR is wired.

---

## AI — `api/ai` (auth required)

> Customer AI endpoints are class-level Customer-only. The document-ingestion action is exposed separately as Admin-only. AI preference PATCH uses FluentValidation through MediatR; chat/session validation is enforced in the service.

| Method | Path | Role | Request | Response |
|---|---|---|---|---|
| POST | `/categorize/preview` | Customer | `CategorizePreviewRequest` | `ApiResponse<AiClassificationResult>` |
| POST | `/categorize/{transactionId:guid}` | Customer | — | `ApiResponse<CategorizationOutcome>` |
| POST | `/transactions/{transactionId:guid}/override` | Customer | `OverrideCategoryRequest` | `ApiResponse<CategorizationOutcome>` |
| GET | `/score?period=WEEKLY\|MONTHLY` | Customer | query `period` (default `WEEKLY`) | `ApiResponse<SpendingScoreResult>` |
| GET | `/reports` | Customer | — | `ApiResponse<WeeklyReportResponse[]>` |
| GET | `/reports/{reportId:guid}` | Customer | — | `ApiResponse<WeeklyReportResponse>` (404) |
| POST | `/reports/generate` | Customer | — | `ApiResponse<WeeklyReportResponse>` |
| POST | `/chat` | Customer | `ChatAskRequest` | `ApiResponse<ChatMessageResponse>` |
| GET | `/chat/history?sessionId=&limit=50` | Customer | optional session and limit | `ApiResponse<ChatMessageResponse[]>` |
| POST | `/chat/sessions` | Customer | `CreateChatSessionRequest` | `ApiResponse<ChatSessionResponse>` |
| GET | `/chat/sessions` | Customer | — | `ApiResponse<ChatSessionResponse[]>` |
| PATCH | `/chat/sessions/{sessionId:guid}` | Customer | `UpdateChatSessionRequest` | `ApiResponse<ChatSessionResponse>` |
| DELETE | `/chat/sessions/{sessionId:guid}` | Customer | — | 204 |
| POST | `/documents` | Admin | multipart: `file` (PDF, ≤20 MB) + `title?` | `ApiResponse<Guid>` (documentId) |

**CategorizePreviewRequest**: `{ input: string }` — no length limit anywhere.
**OverrideCategoryRequest**: `{ categoryId: string }` — no format validation.
**AiClassificationResult**: `{ categoryName?, confidence }`
**CategorizationOutcome**
```ts
{
  transactionId, categoryId?, categoryName?, confidence?, isAiClassified,
  queued: false,
  applied: boolean,
  suggestedCategoryId?, suggestedCategoryName?, reason?,
  source: "MANUAL" | "RULE" | "AI_AUTO" | "AI_SUGGESTION" | "OFF" | "FALLBACK"
}
```
**SpendingScoreResult**: `{ periodType, periodStart, periodEnd, finalScore, spikeScore?, budgetScore?, savingsScore?, weights, colorBadge: "GREEN"|"YELLOW"|"RED", comment? }`
**WeeklyReportResponse**: `{ reportId, periodStart, periodEnd, narrative, finalScore?, colorBadge?, generatedAt }`
**ChatAskRequest**: `{ sessionId?: uuid, question: string }` — question is trimmed and must contain 1–2,000 characters.
**ChatMessageResponse**: `{ messageId, sessionId, senderType: "USER"|"AI", content, timestamp?, dataPeriod?, citations[], limitations[] }`
**ChatSessionResponse**: `{ sessionId, title, historyEnabled, isDefault, createdAt, updatedAt, lastMessageAt? }`
**AiPreferenceDto**: `{ categorizationMode, autoCategorizationThreshold, defaultHistoryEnabled, weeklyReportEnabled, shareBalances, shareTransactions, shareBudgets, shareGoals, shareReports, ragEnabled }`

### POST `/categorize/preview`
**Authorization/validation**: Customer-only. The allowed category set contains system expense categories and only active custom categories owned by the caller; it excludes "Chưa phân loại" and `cat_savings_goal`. When categorization mode is `off`, the endpoint returns an unresolved result without calling Gemini.
**Business logic**: Calls the official Gemini SDK with a structured JSON schema, then validates the returned category against the backend's closed set and clamps confidence to `[0,1]`. Gemini/provider transport or parse failure maps to the AI provider-unavailable exception path.

### POST `/categorize/{transactionId}`
**Authorization/validation**: Customer-only; `transactionId` is route-constrained `:guid`. The query is scoped by both caller customer ID and transaction ID, so a missing or another customer's transaction returns the same 404.
**Business logic**:
- A transaction with manual provenance is locked against automatic overwrite.
- A matching customer merchant rule is resolved before Gemini and is applied only when its category is customer-visible (`source="RULE"`).
- `off`: no Gemini call and no category mutation (`source="OFF"`).
- `suggest_only` (safe default): stores suggestion/confidence/provenance but does not replace `categoryId` (`source="AI_SUGGESTION"`).
- `high_confidence_auto`: replaces `categoryId` only when the category is valid and `confidence >= customer threshold` (`source="AI_AUTO"`); an exact threshold match is accepted.
- Empty/unresolved/provider-unavailable results use `FALLBACK` without clearing a pre-existing category.
- `queued` remains `false`; no background retry is claimed.

### POST `/transactions/{transactionId}/override`
**Validation**: `categoryId` unvalidated in format. 404 if transaction/category missing; **403** `ForbiddenException` if the transaction's wallet isn't owned by the caller.
**Business logic — correction vs. the existing docs**: sets `categoryId`, `isAiClassified=false`, `aiConfidence=null`, and inserts a `CategoryCorrectionLog` row (`customerId`, `transactionId`, `correctedCategoryId`, `originalAiGuess`). **It does NOT create or update a beneficiary rule** — despite the interface being named `IBeneficiaryRuleService`, there is no mapped `BeneficiaryRule` entity in the current schema at all (the `beneficiary_rule` table only exists in a legacy migration, and that migration actually deletes its own rows during the v21 schema change). Treat override purely as "correct this one transaction + log it for later analysis," not as "teach the system a rule" — that's what `POST /rules` is for. Returns `source: "MANUAL"`.

### GET `/score?period=`
**Validation**: not rejecting — any value other than case-insensitive `"MONTHLY"` silently coerces to `"WEEKLY"` (no 400 for garbage input).
**Business logic** (`SpendingScoreService`):
- **spikeScore** (trailing 30 days, needs ≥7 distinct spending days else `null`): per-day z-score vs mean/stdev; a day counts as a spike if `z > 2.0` **and** `amount > 200,000₫`. `penalty = spikeDays*15 + Σ(z-2.0)*5`; `score = max(0, 100-penalty)` (100 if flat spending).
- **budgetScore**: per-bucket pacing vs `monthlyLimit × elapsedFraction`, weighted NEEDS 0.6 / WANTS 0.4 / SAVINGS 0 (excluded); `null` if no budgets exist.
- **savingsScore** (monthly only; needs income set and ≥3 months of history over a 6-month lookback, else `null`): `attainment = clamp(meanRate/0.20, 0, 1)`, `consistency = clamp(1-CV, 0, 1)`, `score = (attainment*0.6 + consistency*0.4)*100`.
- **weights** are hardcoded constants (not config-driven): WEEKLY = spike 50/budget 50; MONTHLY = spike 30/budget 40/savings 30. Missing metrics are dropped and remaining weights renormalized to 100; `finalScore=50` (neutral) if none are available.
- **colorBadge**: `>=80 → GREEN`, `>=50 → YELLOW`, else `RED`.
- **comment**: separate Gemini Flash generation call for 1–2 Vietnamese sentences; on provider unavailability the comment is `null` (never fails the request).
- Whenever the weekly job persists a score snapshot, it's idempotent per `(customer, view, periodStart)`.

### GET `/reports`, GET `/reports/{reportId}`, POST `/reports/generate`
**Business logic**: `WeeklyReportScheduler` sleeps until the next **Monday 07:00 Asia/Ho_Chi_Minh**, selects active customers whose AI preference row is absent or has `weeklyReportEnabled=true`, and isolates failures per customer. Generation is idempotent on `(customerId, weekStart)`, uses the durable `weekly_report` quota, and falls back to a deterministic narrative when quota/provider is unavailable. Budget overrun statements are based on backend-computed positive monthly overrun, not model arithmetic. Firebase delivery also enforces `CustomerSetting.NotifReport` (missing row defaults to enabled); RAG indexing is best-effort. Manual `POST /reports/generate` remains available even when scheduled reports are disabled.

### POST `/chat`, GET `/chat/history`
**Validation**: Customer-only. Question is trimmed and must have 1–2,000 characters. History `limit` is clamped to 1–100. An explicit session must belong to the caller; another customer's session is indistinguishable from a missing session (404).
**Business logic**: Uses durable PostgreSQL minute/day rate windows. Builds deterministic aggregate facts for balances, transactions/cash flow, category spending, true budget remaining/overrun, saving goals, score, and latest report; every group respects the corresponding customer data-scope preference. Responses include backend `dataPeriod`, citations and mandatory limitations. RAG is available only when global `Gemini:RagEnabled` and customer `ragEnabled` are both true; retrieval is customer/global scoped, filtered by `Gemini:RagMinimumSimilarity`, and failures degrade to deterministic context. Recent persisted history is limited to six turns. Provider unavailable/rate limit returns a friendly canned answer. Chat has no mutation command/service/tool execution.

### Chat session endpoints
- `POST /chat/sessions` — body `{ title?: string, historyEnabled?: boolean }`; title defaults to "Cuộc trò chuyện mới", max 120 characters.
- `GET /chat/sessions` — lists caller-owned, non-deleted sessions newest-active first.
- `PATCH /chat/sessions/{sessionId}` — partial title/history update, owner-scoped.
- `DELETE /chat/sessions/{sessionId}` — owner-scoped; deletes the session and cascades message content deletion.
- Omitting `sessionId` from chat/history uses or creates the backward-compatible default session.
- `historyEnabled=false` means neither question nor answer nor recent turns are persisted/sent as history; only non-content session metadata may update.

### AI preference endpoints (`/api/profile`)
- `GET /api/profile/ai-preferences` — returns persisted values or safe defaults.
- `PATCH /api/profile/ai-preferences` — partial first-write upsert.
- `categorizationMode`: `off | suggest_only | high_confidence_auto`.
- `autoCategorizationThreshold`: greater than 0 and at most 1.
- Remaining booleans control default chat persistence, scheduled reports and balances/transactions/budgets/goals/reports/RAG scopes.

### POST `/documents` (Admin)
**Authorization/validation**: Exposed by a separate Admin-only controller at the existing `/api/ai/documents` route, avoiding combined Customer+Admin authorization. `[RequestSizeLimit(20 MB)]`; null/empty file → 400. Empty extracted text → 400.
**Business logic**: PdfPig extracts pages and chunks text into 800-character windows with 150-character overlap. `gemini-embedding-001` generates exactly 768 dimensions through the official SDK; wrong/empty output fails as provider unavailable. Documents are global (`customerId=null`). Existing Ollama vectors must be re-indexed before `Gemini:RagEnabled=true`; see `docs/gemini-setup.md`.

---

## Common envelope types

```ts
ApiResponse<T>  = { success: boolean, message?: string, data?: T }
PagedResult<T>  = { page: number, pageSize: number, totalItems: number, totalPages: number, items: T[] }
```

---

## Error codes (all `BusinessRuleException.Code` values found in the codebase, HTTP 422 unless noted)

| Code string | HTTP | Feature | Trigger |
|---|---|---|---|
| `allocation_locked_use_schedule_endpoint` | 422 | Profile | Sending allocation fields on `PUT /profile` after onboarding is complete |
| `linked_wallet_read_only` | 422 | Transactions | Manual transaction creation attempted on a `sepay_linked` wallet |
| `goal_transaction_locked` | 422 | Transactions | `categoryId=cat_savings_goal` passed to manual transaction create |
| `transfer_managed` | 422 | Transactions | Client attempts to directly create/reclassify a `transfer_out`/`transfer_in` transaction |
| `category_type_mismatch` | 422 | Transactions | Category `type` doesn't match the transaction's income/expense type |
| `insufficient_balance` | 422 | Transactions, SavingGoals | Balance would go negative on create/contribute/amount-edit |
| `synced_transaction_locked` | 422 | Transactions | Delete attempted on a `sepay_sync`-sourced transaction |
| `synced_transaction_fields_locked` | 422 | Transactions | `PUT /transactions/{id}` with `amount`/`merchant`/`transactionDate` on a `sepay_linked`-wallet transaction (category alone is still editable) |
| `reversal_insufficient_balance` | 422 | Transactions | Deleting a transaction would drive the wallet negative on reversal |
| `transfer_pair_invalid` | 422 | Transactions | Deleting a transfer whose paired leg isn't a valid in/out pair |
| `transfer_wallet_missing` | 422 | Transactions | Deleting a transfer where a paired wallet is no longer owned by the customer |
| `transfer_reversal_insufficient_balance` | 422 | Transactions | Reversing a transfer leg would drive either wallet negative |
| `last_wallet` | 422 | Wallets | Attempting to delete the customer's only remaining active wallet |
| `sepay_wallet_read_only` | 422 | Wallets | Transfer using a SePay-linked wallet on either side |
| `withdraw_source_not_sepay` | 422 | Wallets | `POST /wallets/withdraw` with a non-SePay source wallet |
| `withdraw_target_sepay_read_only` | 422 | Wallets | `POST /wallets/withdraw` targeting a SePay-linked receiving wallet |
| `sepay_not_configured` | 503 | Wallets | SePay OAuth `ClientId`/`RedirectUri` not configured |
| `sepay_state_invalid` | 422 | Wallets | OAuth `state` param invalid, expired, or belongs to a different customer |
| `sepay_relink_required` | 422 | Wallets | OAuth token refresh failed during sync — customer must re-link |
| `sepay_webhook_requires_oauth` | 422 | Wallets | Webhook (un)registration attempted on a static-token-linked wallet |
| `sepay_webhook_disabled` | 422/503 | Wallets | `SePay:WebhookApiKey` not configured server-side |
| `sepay_webhook_url_missing` / `sepay_webhook_url_invalid` | 422 | Wallets | `SePay:WebhookUrl` missing, or not a public non-loopback http(s) URL |
| `goal_remaining_exceeded` | 422 | SavingGoals | Contribution amount exceeds `targetAmount - currentAmount` |
| `goal_ledger_invalid` | 422 | SavingGoals | Deleting a goal whose contribution ledger has an entry that isn't a `cat_savings_goal` `expense`/`income` transaction |
| `goal_wallet_missing` | 422 | SavingGoals | Deleting a goal whose funding wallet was deleted before refund could complete |
| `goal_ledger_reversal_insufficient_balance` | 422 | SavingGoals | Deleting a goal would need to undo a withdrawal's credit, but the destination wallet no longer has that balance (already spent) |
| `goal_funding_wallet_sepay_unsupported` | 422 | SavingGoals | `fundingWalletId` (create or contribute) resolves to a `sepay_linked` wallet |
| `goal_withdraw_exceeds_saved` | 422 | SavingGoals | `POST /saving-goals/{id}/withdraw` amount exceeds the goal's `currentAmount` |
| `goal_withdraw_target_sepay_unsupported` | 422 | SavingGoals | `POST /saving-goals/{id}/withdraw` targeting a `sepay_linked` wallet |
| `ocr_not_configured` | 503 | Extract | `POST /extract/photo` — no real OCR provider wired (`IReceiptOcrService` placeholder) |

Plain 400/404/409/403 errors (no `Code`, message-only) are noted inline in each endpoint's Validation/Business logic section above.

**Migration note**: V25 (`V25__gemini_safe_copilot.sql`) is also executed by `DbInitializer`'s additive startup path because externally provisioned v3 databases skip numbered migrations. Validate the script against the target PostgreSQL schema before production rollout; it is additive and does not drop/recreate AI data.
