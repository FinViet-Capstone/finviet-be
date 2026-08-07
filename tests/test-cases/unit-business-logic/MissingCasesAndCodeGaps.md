# Unit Business-Logic: Missing Cases and Code Gaps

## Scope and status policy

This companion document covers the unit-business-logic scope only: Auth, Profile, Category, and Wallet. The executable isolated cases cataloged in `unit-testcases.data.mjs` are marked **Pass** after the complete `FinViet.Application.UnitTests` run succeeded on 2026-07-31 (136 passed, 0 failed, 0 skipped). Cases needing a real database, API pipeline, provider contract, concurrency execution, or an agreed policy change remain **Deferred**.

The generated Word and Excel files contain Summary, Function Catalog, Unit Test Matrix, Code Gaps, and a separate Deferred Cases section.

## Deferred cases

| Area | Deferred case | Why it is not an isolated executable unit case | Required layer |
|---|---|---|---|
| Auth / Google | Reject Firebase identities whose email is unverified | Depends on intended provider-account policy; current handler only rejects absent email. | Firebase emulator or contract test after policy decision |
| Auth / Google | Return 401 for invalid Firebase credentials and 503 for Firebase outage | Needs translation across provider client, handler, and global exception middleware. | API and provider contract test |
| Auth input | Reject malformed Google, refresh, logout, and forgot-password payloads through FluentValidation | Dedicated validators are missing for the applicable commands. | Post-fix validator and API tests |
| Auth login | Trim an email before login lookup | Current behavior is not a documented/implemented contract. | Post-fix unit regression test |
| Auth refresh | Only one simultaneous refresh can rotate a token | Requires atomic persistence behavior and real PostgreSQL concurrency. | DB concurrency integration test |
| Auth token handling | Store refresh/reset secrets hashed and keep them out of logs | Spans token persistence and configured logging sinks. | Security integration/log review |
| Auth abuse/failure | Rate limit register/login/reset and preserve consistent state if email delivery fails | Middleware and provider-fault/persistence policy are outside a single handler. | API/provider integration test |
| Profile fields | Validate date of birth and gender enum values; intentionally clear optional values | Null currently means both absent and potentially-clear; policy/DTO semantics must be established. | Post-fix unit and API tests |
| Profile avatar | Reject oversize file before copying entire upload to memory | Controller buffers `IFormFile` before handler validates size. | API/performance test after design change |
| Profile avatar | Preserve old avatar and avoid orphaned new asset on upload/database failure | Requires storage and database fault injection plus compensation behavior. | Provider/DB integration test |
| Profile deactivation | Block already-issued access JWT after account deactivation | Cross-cutting authorization/token state behavior. | API security integration test |
| Category persistence | Persist `IsMandatory` through create/update/read | EF mapping currently ignores the property. | Post-fix database integration test |
| Category update | Retain valid type/default-bucket invariants after type change | Final aggregate validation is incomplete. | Post-fix service test |
| Category protection | Prevent unsafe rename/retype/delete of reserved savings-goal category | Existing protection is limited to customer bucket reassignment. | Post-fix service/API test |
| Category boundaries | Enforce field lengths and null/empty semantics matching schema | Comprehensive boundary validation is not defined. | Validation and DB integration test |
| Category/budget | Verify global bucket/type edits do not corrupt budget seeding/allocation semantics | Cross-service business policy and fixtures required. | Business-flow integration test |
| Wallet creation | Prevent concurrent duplicate name or 11th wallet | Read-before-write count/duplicate checks need a database-level strategy. | PostgreSQL concurrency integration test |
| Wallet opening balance | Create the approved opening-balance history/audit record | Current service assigns `Balance` directly; history policy is unconfirmed. | Post-fix DB business-flow test |
| Wallet deletion | Enforce policy for nonzero balances, transactions, and linked accounts | Current implementation only blocks deletion of the final active wallet. | Approved-policy integration test |

## Confirmed code gaps

| ID | Severity | Area | Gap and observed impact | Recommended action |
|---|---|---|---|---|
| GAP-AUTH-01 | High | Google sign-in | `GoogleLoginCommandHandler` checks that email exists but does not require `firebaseUser.EmailVerified`; an unverified Google identity may receive local tokens. | Require a verified Firebase email before account match/create, or specify a distinct local verification flow. |
| GAP-AUTH-02 | Medium | Google sign-in | Invalid-token and provider-unavailable behavior do not have a verified 401/503 contract across Firebase client, handler, and middleware. | Translate invalid credentials to `UnauthorizedException` and outage to `IntegrationUnavailableException`; add contract tests. |
| GAP-AUTH-03 | Medium | Auth validation | Dedicated validators are absent for relevant forgot-password, Google-login, refresh-token, and logout commands. | Add FluentValidation validators and cover them directly. |
| GAP-AUTH-04 | Low | Password login | Login email trimming is not established; whitespace-padded valid input can follow a different path from normalized registration data. | Normalize consistently at request boundary/handler and add regression coverage. |
| GAP-AUTH-05 | High | Token lifecycle | Refresh rotation needs race protection; raw persisted tokens and logging exposure remain a security concern. | Use atomic conditional rotation, hash persisted secrets, redact logs, and add concurrency/security tests. |
| GAP-AUTH-06 | Medium | Abuse and email failure | No demonstrated rate limits or atomic/compensating policy when register/reset email delivery fails. | Add endpoint rate limiting and explicit transactional/failure semantics. |
| GAP-PROF-01 | Medium | Profile fields | DOB and gender validation is incomplete, and nullable update fields cannot distinguish omitted from an intentional clear. | Define clear semantics and validate dates/defined enum values. |
| GAP-PROF-02 | Medium | Avatar buffering | `ProfileController` copies the complete upload into a `MemoryStream` before handler size validation. | Enforce bounded request/file size before buffering or use bounded streaming. |
| GAP-PROF-03 | High | Avatar replacement | Handler deletes the old asset before uploading/saving the new URL, so failures can lose old data or create orphan assets. | Upload first, persist safely, then remove old asset with cleanup/compensation. |
| GAP-PROF-04 | High | Deactivation security | Refresh tokens are revoked, but existing access JWT validity after deactivation is not demonstrably enforced. | Check active account/token version on protected access or implement access-token revocation policy. |
| GAP-CAT-01 | High | `IsMandatory` | Category service accepts/maps `IsMandatory`, while the EF context ignores it, so it cannot round-trip reliably. | Align schema and EF mapping; add persistence test. |
| GAP-CAT-02 | High | Update invariants | Changing category type does not validate the final income/expense default-bucket state. | Validate the complete final aggregate before saving. |
| GAP-CAT-03 | Medium | Savings-goal category | Reserved category protection exists for customer bucket override but not clearly for global update/delete. | Reserve it from unsafe admin mutations or define controlled migration behavior. |
| GAP-CAT-04 | Medium | Boundaries | Length/null/empty requirements are not fully validated before persistence errors can surface. | Add DTO/FluentValidation rules matched to schema limits and null semantics. |
| GAP-CAT-05 | Medium | Budget interaction | Effect of global category bucket/type edits on budget seeding and allocation is unspecified. | Decide policy and verify with cross-service business-flow tests. |
| GAP-WAL-01 | High | Create race | Max-wallet and case-insensitive name checks are read-before-write and can be bypassed by concurrency. | Add database constraints/atomic enforcement and PostgreSQL race tests. |
| GAP-WAL-02 | Medium | Opening balance | Wallet creation writes initial balance directly with no defined opening-balance transaction/history. | Decide audit model and create the history entry if required. |
| GAP-WAL-03 | Medium | Delete policy | Deletion checks only the last active wallet; policies for balance/history/linked accounts remain ambiguous. | Approve and enforce explicit deletion invariants. |

## Source locations used for the catalog

- `src/FinViet.Infrastructure/Features/Auth/Commands/`
- `src/FinViet.Infrastructure/Features/Profile/Commands/` and `Queries/`
- `src/FinViet.Infrastructure/Services/CategoryService.cs`
- `src/FinViet.Infrastructure/Services/WalletService.cs`
- `src/FinViet.Api/Controllers/AuthController.cs`
- `src/FinViet.Api/Controllers/ProfileController.cs`
- `src/FinViet.Api/Controllers/AccountController.cs`
- `src/FinViet.Api/Controllers/CategoriesController.cs`
- `src/FinViet.Api/Controllers/WalletsController.cs`
