# Current Feature

<!-- Feature name and short description -->

Remove Finverse integration, keep SePay as the sole bank-linking provider. Item 1 of `context/be-revamp.md`'s build order. Finverse never went live (blocked on BankHub approval per `fe-plan-2026-07-revamp.md`); FE is removing all Finverse UI in the companion repo.

## Status

<!-- Not Started | In Progress | Completed -->

Completed — superseded by a teammate's independent implementation, merged into `khoi` (see History)

## Goals

<!-- Goals and requirements -->

- Delete the Finverse entity, external-service client, wallet-sync service, DTOs, DI registrations, config, docs, and unit test.
- Remove the four Finverse-specific actions from `WalletsController` (`CreateFinverseLinkToken`, `CompleteFinverseLink`, `FinverseCallback`, `SyncFinverseWallet`) without touching the SePay actions in the same controller.
- Drop the `finverse_links` table via a new migration. Per the existing precedent in `V15__drop_sepay_linked_wallets.sql`, do **not** attempt to drop the `finverse_linked`/`finverse_sync` Postgres enum values (Postgres can't drop individual enum values without recreating the type) — keep the corresponding CLR enum members (`WalletType.FinverseLinked`, `EntryMethod.FinverseSync`) so any legacy rows still deserialize.
- Generalize two business rules that were hardcoded to `finverse_linked` only and never covered `sepay_linked`, so they keep working for the one remaining provider instead of silently breaking:
  - `WalletService.WithdrawAsync`/`ExecuteWalletTransferAsync`'s read-only-linked-wallet checks (source-only withdrawal, transfer block) — currently only recognize `finverse_linked`, never `sepay_linked`.
  - `WalletResponse`'s `InstitutionName`/`AccountMask`/`LastSyncedAt` display fields in `WalletService.GetWalletsAsync`/`ToResponse` — currently only ever sourced from `wallet.FinverseLink`, never `wallet.SepayLink`, so SePay-linked wallets show blank bank info in the generic wallet list/detail endpoints today.
- Trim the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository.cs` (read-only-wallet check, synced-transaction-delete-lock check), leaving the `sepay_linked`/`sepay_sync` checks intact.

## Notes

<!-- Any extra notes -->

- Full backend audit and decisions: `context/be-revamp.md` (item 1).
- Finverse is structurally isolated from SePay (mirrors it 1:1) except for the two shared-logic gaps above, which this feature also fixes as a necessary side effect of "keep SePay only" actually working correctly.
- No live `finverse_linked` wallet rows are expected (Finverse never went live) — not verified against a live database in this session; flagged for awareness before running the migration against any real environment.

## History

<!-- Keep this updated. Earliest to latest -->

- 2026-07-26 — Started. Branch `feature/remove-finverse` created.
- 2026-07-26 — Implemented on branch `feature/remove-finverse`: deleted all Finverse-only files (entity, external-service client, wallet-sync service, DTOs, config example, unit test, docs page); removed the 4 Finverse actions from `WalletsController` and its DI registrations; removed the `FinverseLink` nav property/DbSet/entity config; added migration `V20__drop_finverse.sql` (drops `finverse_links` table only — kept `WalletType.FinverseLinked`/`EntryMethod.FinverseSync` CLR enum members per the `V15` precedent, since Postgres can't drop individual enum values); generalized `WalletService`'s withdraw/transfer read-only-linked-wallet checks and `WalletResponse`'s institution/mask/synced-at display fields from Finverse-only to SePay (they were never wired to SePay, which would have silently broken withdrawal and wallet-info display for the sole remaining provider); trimmed the now-dead `finverse_linked`/`finverse_sync` branches in `TransactionRepository`; updated `docs/api-reference.md`. `dotnet build` passed with 0 errors.
- 2026-07-27 — Superseded: a teammate independently implemented the same removal (plus SePay OAuth/webhook hardening and an AI-provider swap) on `origin/dev` (commits `8c4be9f`, `f95f2ab`) before this branch was committed upstream. Per user decision, `feature/remove-finverse`'s code changes were dropped in favor of the teammate's version — `khoi` was merged with `origin/dev` directly (merge commit `6fdc8ed`) instead. Only this file and the other `context/*.md` planning docs were carried over from the abandoned branch. `dotnet build` passes on `khoi` post-merge with 0 errors. Item 1 of `be-revamp.md` is done; next up is item 2 (income-allocation history).
