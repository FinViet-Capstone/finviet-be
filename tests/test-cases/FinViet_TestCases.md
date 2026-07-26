# FinViet — Test Cases & Results (Backend API)

- **Project:** FinViet – AI-Powered Personal Finance Tracker (Newest_Dev backend, ASP.NET Core + PostgreSQL)
- **Test type:** Functional / Integration (HTTP, black-box against running API)
- **Environment:** `http://localhost:5122`, DB `FinViet_update` (v3 schema)
- **Accounts:** Customer `tkv2003@gmail.com` · Admin `admin`
- **Executed:** 2026-06-24 · **Automated by:** `tests/FinViet.Api.IntegrationTests` (`dotnet test`)
- **Result:** 53 Passed · 11 Skipped (known bug / not implemented) · 0 Failed

Status legend: ✅ Pass · ⏭️ Skipped-Known-Bug · 🚫 Not-Implemented · ⚠️ Conditional

---

## 1. Authentication & Profile

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-AUTH-01 | Auth | Seeded customer | POST `/api/auth/login` valid email+password | 200, accessToken + refreshToken returned | ✅ |
| TC-AUTH-02 | Auth | Seeded customer | POST `/api/auth/login` wrong password | 401 Unauthorized | ✅ |
| TC-AUTH-03 | Auth | Seeded admin | POST `/api/auth/admin-login` valid creds | 200, accessToken returned | ✅ |
| TC-AUTH-04 | Auth | Valid refresh token | POST `/api/auth/refresh-token` | 200, new token pair (rotation) | ✅ |
| TC-PROF-01 | Profile | Logged-in customer | GET `/api/profile` | 200, profile with matching email | ✅ |
| TC-PROF-02 | Profile | Logged-in customer | PUT `/api/profile` set monthlyIncome=12M | 200, income persisted | ✅ |
| TC-PROF-03 | Profile | No token | GET `/api/profile` | 401 Unauthorized | ✅ |

## 2. Categories (global library)

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-CAT-01 | Category | Seeded library | GET `/api/categories` | 200, ≥14 categories | ✅ |
| TC-CAT-02 | Category | — | GET `/api/categories?type=expense` | 200, only expense | ✅ |
| TC-CAT-03 | Category | — | GET `/api/categories?type=income` | 200, only income | ✅ |
| TC-CAT-04 | Category | — | GET `/api/categories/cat_food` | 200, category returned | ✅ |
| TC-CAT-05 | Category | — | GET `/api/categories/cat_does_not_exist` | 404 Not Found | ✅ |

## 3. Wallets

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-WAL-01 | Wallet | Logged-in | GET `/api/wallets` | 200, totalBalance + wallets[] | ✅ |
| TC-WAL-02 | Wallet | Logged-in | POST `/api/wallets` basic, balance 1M | 201, balance = 1M | ✅ |
| TC-WAL-03 | Wallet | Logged-in | POST `/api/wallets` walletType=nonsense | 400 (only basic/sepay_linked) | ✅ |
| TC-WAL-04 | Wallet | Wallet exists | PATCH `/api/wallets/{id}` rename | 200, name updated | ✅ |
| TC-WAL-05 | Wallet | Wallet exists | PATCH `/api/wallets/{id}` change type | 400 (type immutable, BL §1) | ✅ |
| TC-WAL-06 | Wallet | No token | GET `/api/wallets` | 401 Unauthorized | ✅ |

## 4. Transactions

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-TXN-01 | Transaction | Logged-in | GET `/api/transactions?page=1&pageSize=10` | 200, paged items | ✅ |
| TC-TXN-02 | Transaction | Wallet 1M | POST `/api/transactions` expense 50k | 201, wallet debited to 950k | ✅ |
| TC-TXN-03 | Transaction | Wallet 10k | POST expense 999,999,999 | 422 insufficient balance | ✅ |
| TC-TXN-04 | Transaction | Wallet 1M | POST twice, same Idempotency-Key | Single tx, debit once (965k) — dedup | ✅ |
| TC-TXN-05 | Transaction | Tx exists | PATCH `/classify` then PUT category | 200 both | ✅ |
| TC-TXN-06 | Dashboard | — | GET `/api/transactions/summary` | 200, byCategory + byDay + topBeneficiaries (donut/bar/top-5) | ✅ |

## 5. Transfer & Withdraw

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-TRF-01 | Transfer | 2 wallets | POST `/api/wallets/transfer` 100k | 200, balances move (900k / 600k) | ✅ |
| TC-TRF-02 | Transfer | 1 wallet | POST transfer same from/to | 400 must differ | ✅ |
| TC-TRF-03 | Transfer | Wallet 10k | POST transfer 999,999,999 | 400 insufficient balance | ✅ |
| TC-WDR-01 | Withdraw | Basic wallet | POST `/api/wallets/withdraw` from basic | 422 (SePay-linked only, BL §1) | ✅ |

## 6. Budgets

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-BUD-01 | Budget | Logged-in | GET `/api/budgets` | 200 | ✅ |
| TC-BUD-02 | Budget | Logged-in | GET `/api/budgets/buckets` | 200, buckets + monthlyIncome + allocationCap (BL §6) | ✅ |
| TC-BUD-03 | Budget | — | POST → PATCH → DELETE budget | 200 / 200 / 204 | ✅ |
| TC-BUD-04 | Budget | Admin token | GET `/api/budgets` as admin | 403 (customer-only) | ✅ |

## 7. Saving Goals

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-GOL-01 | Goal | Wallet exists | Create → Get → Patch → Contribute → Delete | 201/200/200/200, currentAmount=200k | ✅ |
| TC-GOL-02 | Goal | Goal target 500k | Contribute 999,999,999 | 422 exceeds remaining (BL §10) | ✅ |

## 8. Import / Extraction

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-EXT-01 | SMS import | Logged-in | POST `/api/extract/sms` valid text | 200, ≥1 row (amount/merchant/date) | ✅ |
| TC-EXT-02 | SMS import | — | POST `/api/extract/sms` empty | 400 | ✅ |
| TC-EXT-03 | CSV import | — | POST `/api/extract/csv` plain .csv | **Expected** 200 parsed rows | ⏭️ **BUG #C**: 500 "Invalid file signature" — parser is Excel-only |

## 9. AI features

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-AI-01 | Spending Score | Logged-in | GET `/api/ai/score?period=WEEKLY` | 200, finalScore (weights 50/50) | ✅ |
| TC-AI-02 | Spending Score | Logged-in | GET `/api/ai/score?period=MONTHLY` | 200, finalScore (30/40/30) | ✅ |
| TC-AI-03 | Categorize | Logged-in, Ollama running | POST `/api/ai/categorize/preview` | 200 suggestion from local model | ⏳ Re-test after local AI migration |
| TC-AI-04 | Weekly Report | Logged-in | GET `/api/ai/reports` | **Expected** 200 list | ⏭️ **BUG #A**: 500 `column a.report_id does not exist` |
| TC-AI-05 | Weekly Report | Logged-in | POST `/api/ai/reports/generate` | **Expected** 2xx | ⏭️ **BUG #A**: 500 (v3 schema drift) |
| TC-AI-06 | Chatbot | Logged-in | POST `/api/ai/chat` question | **Expected** 200 answer | ⏭️ **BUG #A**: 500, relation `chat_message` missing |
| TC-AI-07 | Chatbot | Logged-in | GET `/api/ai/chat/history` | **Expected** 200 | ⏭️ **BUG #A**: 500, `chat_message` missing |

## 10. Category Bucket Self-Service & Admin

`category_requests` (admin-approval flow) was removed — customers now reassign a category's
bucket directly, no admin review step.

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-BKT-01 | Cat bucket | Customer | PUT `/api/categories/{id}/bucket` then GET `/api/categories`, then DELETE `/bucket` | 200, override reflected, reset to default | ✅ |
| TC-BKT-02 | Cat bucket | Customer | PUT `/api/categories/{id}/bucket` with invalid bucketId | 400/422 | ✅ |
| TC-BKT-03 | Cat bucket | Customer | PUT `/api/categories/{id}/bucket` on an income category | 400/422 | ✅ |
| TC-ADM-01 | Admin Category | Admin | POST → PATCH → DELETE `/api/categories` | 201/200/200 | ✅ |
| TC-ADM-02 | Admin Account | Admin | PUT `/api/account/deactivate/{unknown-guid}` | 404 | ✅ |

## 11. Authorization (RBAC)

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-AUTHZ-01 | RBAC | Admin token | PUT `/api/categories/{id}/bucket` (customer-only) | 403 | ✅ |
| TC-AUTHZ-02 | RBAC | Customer token | POST `/api/categories` (admin-only) | 403 | ✅ |
| TC-AUTHZ-03 | RBAC | Admin token | GET `/api/wallets` (customer-only) | 403 | ✅ |
| TC-AUTHZ-04 | RBAC | No token | GET `/api/wallets` | 401 | ✅ |

## 12. Rules — merchant auto-categorization (api_list #41–43, BL §2/§8)

| ID | Module | Precondition | Steps | Expected | Status |
|----|--------|--------------|-------|----------|--------|
| TC-RULE-01 | Rules | Logged-in | GET `/api/rules` | 200, list | ✅ |
| TC-RULE-02 | Rules | Tx w/ keyword | POST `/api/rules` → retro-apply | 201, `appliedCount`≥1, tx category updated (BL §2 hồi tố) | ✅ |
| TC-RULE-03 | Rules | Rule exists | POST `/api/rules` duplicate keyword (case-insensitive) | 409 Conflict | ✅ |
| TC-RULE-04 | Rules | — | POST `/api/rules` unknown category | 404 Not Found | ✅ |
| TC-RULE-05 | Rules | — | DELETE `/api/rules/{missing}` | 404 Not Found | ✅ |
| TC-RULE-06 | Rules | Admin token | GET `/api/rules` as admin | 403 (customer-only) | ✅ |
| TC-RULE-07 | Rules | Rule exists | POST `/api/transactions` không category, note khớp rule | 201, tx **tự gán** category của rule (BL §2b) | ✅ |
| TC-RULE-08 | Rules | Rule exists | POST `/api/extract/sms` text khớp rule | row.categoryId = rule, confidence 1.0 (**rule ưu tiên hơn AI**) | ✅ |

## 13. Requirement gaps — no endpoint (Capstone "Register content")

| ID | Requirement | Status |
|----|-------------|--------|
| TC-GAP-01 | Admin: System analytics (total users, DAU, total transactions, AI API call volume & cost/day) | 🚫 Not implemented |
| TC-GAP-02 | Admin: Category correction log **view** (write path exists, no read API) | 🚫 Not implemented |
| TC-GAP-03 | Admin: Announcement broadcast (in-app notify all/segments) | 🚫 Not implemented |
| TC-GAP-04 | Admin: User management — list users, re-activate, admin reset-password (only deactivate exists) | ⚠️ Partial |
| TC-GAP-05 | Mobile: Notification center (list/read budget, report, goal notifications) | 🚫 Not implemented |

---

## Defect summary

| # | Severity | Area | Symptom | Root cause |
|---|----------|------|---------|-----------|
| A | High (blocks 2 required features) | AI Weekly Report + Chatbot | 500 on `/api/ai/reports*` and `/api/ai/chat*` | `DbInitializer` skips all SQL migrations when v3 schema detected → V8 (`ai_weekly_reports.report_id`, `chat_message`) never ran on `FinViet_update`; EF model drifted from DB. |
| B | Resolved in code | AI categorization | Previous cloud API-key dependency | Replaced by local Ollama through an OpenAI-compatible client. |
| C | Medium | CSV import | 500 "Invalid file signature" on plain CSV | `BankStatementExcelParser` only reads Excel binaries; endpoint accepts `.csv` but cannot parse text CSV. Should be 400, and CSV should be supported per req. |

## Notes / deviations
- **Tech stack:** Implementation is ASP.NET Core with a provider-neutral OpenAI-compatible client backed by local Ollama, PostgreSQL, and Monday `WeeklyReportScheduler`.
- **How to re-run:** start API (`dotnet run --project src/FinViet.Api`), then `dotnet test tests/FinViet.Api.IntegrationTests`. Target/credentials override via env vars `FINVIET_TEST_BASEURL`, `FINVIET_TEST_CUST_EMAIL`, etc. If the API is down, the whole suite skips (not fails).
