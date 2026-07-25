-- Removes the Finverse integration (SePay is now the only bank-linking provider) and extends
-- sepay_links with OAuth access-token expiry tracking. Run manually before starting the API.
-- Idempotent.
--
-- Note: the 'finverse_linked' / 'finverse_sync' labels remain in the shared wallet_type /
-- entry_method enums — Postgres cannot drop individual enum values. The CLR enums keep the
-- matching members so a row written before this migration still reads back, but nothing in the
-- application produces them any more: the statements below rewrite every existing row.

-- 1. Former Finverse wallets become ordinary manual wallets. Their balances and transaction
--    history are kept; the wallet simply stops being read-only because nothing syncs it now.
UPDATE wallets
SET type = 'basic', updated_at = now()
WHERE type = 'finverse_linked';

-- 2. Same reasoning for the transactions: without a provider to re-sync them they must become
--    editable, which is what entry_method = 'manual' means everywhere else in the app.
UPDATE transactions
SET entry_method = 'manual', updated_at = now()
WHERE entry_method = 'finverse_sync';

-- 3. The Finverse credential store is no longer read by anything.
DROP TABLE IF EXISTS finverse_links;

-- 4. SePay access tokens live ~1h. Recording the expiry lets a sync reuse a still-valid token
--    instead of spending a refresh-token rotation on every single call.
ALTER TABLE sepay_links
    ADD COLUMN IF NOT EXISTS access_token_expires_at timestamptz;

-- 5. The webhook receiver routes an incoming delivery by bank account number, so that lookup has
--    to be indexed rather than a sequential scan over every link in the system.
CREATE INDEX IF NOT EXISTS idx_sepay_links_account_number
    ON sepay_links (account_number)
    WHERE account_number IS NOT NULL;
