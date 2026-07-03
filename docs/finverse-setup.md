# Finverse wallet and transaction setup

Finverse Data API is integrated as a read-only source. FinViet keeps the API
credentials and Login Identity tokens in the backend, mirrors bank accounts as
`finverse_linked` wallets, and stores imported records as `finverse_sync`
transactions.

## Database migration

Apply the migration to the same database configured in `ConnectionStrings:DefaultConnection`
before starting the API:

```powershell
psql -h localhost -U postgres -d <database> -f src/FinViet.Infrastructure/Persistence/Migrations/V14__finverse_wallet_transaction_integration.sql
```

The script adds the `finverse_linked` and `finverse_sync` enum values and creates
the one-to-one `finverse_links` table. It is idempotent and also migrates data
from the earlier `finverse_connections` / `finverse_wallet_links` draft before
removing those obsolete tables. Login Identity and refresh tokens are encrypted
with ASP.NET Data Protection before they are stored in the table's `access_token`
and `refresh_token` `text` columns.

Raw v3 SQL migrations are intentionally not replayed by `DbInitializer`: enum
changes must already exist when Npgsql initializes the API's enum mappings.

## Configuration

Create API credentials in the Finverse dashboard and register the exact redirect
URI used by the frontend. Keep secrets out of `appsettings.json` and Git:

```powershell
dotnet user-secrets set "Finverse:ClientId" "<client_id>" --project src/FinViet.Api
dotnet user-secrets set "Finverse:ClientSecret" "<client_secret>" --project src/FinViet.Api
dotnet user-secrets set "Finverse:RedirectUri" "https://localhost:7253/api/wallets/finverse/callback" --project src/FinViet.Api
```

For a deployed environment, set the equivalent environment variables:

```text
Finverse__ClientId
Finverse__ClientSecret
Finverse__RedirectUri
```

The API base URL defaults to `https://api.prod.finverse.net/` for both Finverse
test and live teams.

## Linking flow

1. Authenticated client calls `POST /api/wallets/finverse/link-token`.
2. Open the returned `linkUrl` in Finverse Link UI.
3. Finverse submits `code` and `state` as `application/x-www-form-urlencoded`
   to `POST /api/wallets/finverse/callback`. The callback is protected by an
   opaque 256-bit, expiring, single-use state value and therefore does not
   require the FinViet JWT in the Finverse browser session. Finverse's
   authorization code remains one-time-use.
4. If a frontend receives the form post through its own server route instead,
   it may forward both values with the user's JWT to
   `POST /api/wallets/finverse/complete-link`.
5. FinViet exchanges the code, fetches Accounts, and creates or restores one
   read-only wallet per non-parent account. `complete-link` accepts an optional
   `accounts` array to link a subset instead of every account — each entry may be
   a Finverse account id, the account name (case-insensitive, e.g. "Bitcoin"),
   or the masked number (account ids are minted per login identity, so name/mask
   is the practical selector for a client that only holds code+state).
6. Call `POST /api/wallets/{walletId}/finverse-sync` to import the account's
   posted transactions and refresh its authoritative balance. Newly imported
   expense transactions are sent through the existing merchant-based AI
   categorization flow; a categorization failure does not roll back the bank sync.

## Transaction date filters

Finverse `GET /transactions` and `GET /transactions/{account_id}` accept
`offset` and `limit`; they do not accept `from` or `to`. FinViet therefore pages
through Finverse by account, deduplicates using Finverse's deterministic
`transaction_id`, and applies date filters locally through
`GET /api/transactions?from=...&to=...` or
`GET /api/wallets/{walletId}/transactions?fromDate=...&toDate=...`.

Pending Finverse transactions are skipped until they are posted. Synced
transactions cannot be manually deleted, and linked wallet balances cannot be
changed by manual transactions or wallet transfers. Upserts use the partial
unique `transactions.external_id` index, preserve an existing category, and
store Finverse posted dates at midnight in the `Asia/Ho_Chi_Minh` business day.
