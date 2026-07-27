# Current Feature

<!-- Feature name and short description -->

Authenticated change-password endpoint. Item 4 of `context/be-revamp.md`'s build order. Today only unauthenticated `forgot-password`/`reset-password` (email-token based) exist — FE's `ChangePasswordSheet` already calls `useChangePassword({currentPassword, newPassword})` and is currently force-routed to a mock for lack of a real endpoint (per `be-notes.md`'s resolved Q&A).

## Status

<!-- Not Started | In Progress | Completed -->

Completed — awaiting commit approval

## Goals

<!-- Goals and requirements -->

- New `POST /api/auth/change-password` — `[Authorize(Roles = "Customer")]`, body `{ currentPassword, newPassword }`.
- New `ChangePasswordCommand`/handler under `Features/Auth/Commands/ChangePassword/`, following the existing per-command-subfolder pattern (`LoginCommand`/`ResetPasswordCommand`).
- Verify `currentPassword` against the stored BCrypt hash before updating; throw `BadRequestException` ("Current password is incorrect.") on mismatch — matches the existing exception-to-status-code convention (400).
- Reuse `ResetPasswordCommandValidator`'s exact password policy for `NewPassword` (min 8 chars, ≥1 uppercase, ≥1 digit) for consistency.
- Mirror `ResetPasswordCommandHandler`'s security practice: revoke all other active refresh tokens on successful change, forcing re-login on other devices/sessions.

## Notes

<!-- Any extra notes -->

- Full backend audit and decisions: `context/be-revamp.md` (item 4).
- Google-only accounts have a random, unguessable `PasswordHash` set at signup (see `GoogleLoginCommandHandler`) — they'll naturally get "Current password is incorrect" from this endpoint, which is acceptable; a "set initial password" flow for OAuth-only accounts is out of scope here.
- Not verified against a live database or integration test suite in this session.

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-07-26 — Started. Branch `feature/remove-finverse` created.
- 2026-07-26 — Implemented on branch `feature/remove-finverse`: deleted all Finverse-only files (entity, external-service client, wallet-sync service, DTOs, config example, unit test, docs page); removed the 4 Finverse actions from `WalletsController` and its DI registrations; removed the `FinverseLink` nav property/DbSet/entity config; added migration `V20__drop_finverse.sql` (drops `finverse_links` table only — kept `WalletType.FinverseLinked`/`EntryMethod.FinverseSync` CLR enum members per the `V15` precedent, since Postgres can't drop individual enum values); generalized `WalletService`'s withdraw/transfer read-only-linked-wallet checks and `WalletResponse`'s institution/mask/synced-at display fields from Finverse-only to SePay (they were never wired to SePay, which would have silently broken withdrawal and wallet-info display for the sole remaining provider); trimmed the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository`; updated `docs/api-reference.md`. `dotnet build` passed with 0 errors.
- 2026-07-27 — Item 1 superseded: a teammate independently implemented the same removal (plus SePay OAuth/webhook hardening and an AI-provider swap) on `origin/dev` (commits `8c4be9f`, `f95f2ab`) before this branch was committed upstream. Per user decision, `feature/remove-finverse`'s code changes were dropped in favor of the teammate's version — `khoi` was merged with `origin/dev` directly (merge commit `6fdc8ed`) instead. Only the `context/*.md` planning docs were carried over from the abandoned branch. `dotnet build` passes on `khoi` post-merge with 0 errors. Item 1 of `be-revamp.md` is done.
- 2026-07-27 — Item 2 (income-allocation history) implemented on branch `feature/income-allocation-history`, committed (`c31c392`), merged into `khoi` (fast-forward), branch deleted. New `income_allocation_settings` table/service/endpoints; `BudgetService` resolves allocation per requested month instead of reading `Customer` live; `UpdateProfileCommandHandler` blocks post-onboarding direct edits. 11 new unit tests (`TC-INCALLOC-01..08`), all 32 unit tests pass, `dotnet build` 0 errors.
- 2026-07-27 — Item 3 (customer settings endpoint) implemented; caught mid-way that it had been started directly on `khoi` instead of a branch — corrected by branching (`feature/customer-settings-endpoint`) from that state before committing. Committed (`216222d`, `f31ecee`), merged into `khoi` (fast-forward), branch deleted. New `PATCH /api/profile/settings`; `BudgetService` reads per-customer alert thresholds; defensive `V23` migration since no script ever created `customer_settings`. `dotnet build` 0 errors, all 32 unit tests pass.
- 2026-07-27 — Started item 4 (change-password endpoint). Branch `feature/change-password-endpoint` created.
- 2026-07-27 — Implemented: new `ChangePasswordCommand`/validator/handler (`Features/Auth/Commands/ChangePassword/`), new `POST /api/auth/change-password` (`[Authorize(Roles = "Customer")]`) on `AuthController`. Verifies current password via BCrypt, hashes and stores the new one, revokes all other active refresh tokens (mirroring `ResetPasswordCommandHandler`). Updated `docs/api-reference.md`. `dotnet build` 0 errors; all 32 unit tests pass. Awaiting commit approval.
