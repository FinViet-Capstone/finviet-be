-- SePay OAuth2 integration. Stores OAuth tokens + bank account metadata per linked wallet.
-- Mirrors the finverse_links pattern. Run manually before starting the API. Idempotent.
--
-- Note: wallet_type 'sepay_linked' and entry_method 'sepay_sync' already exist in their
-- respective Postgres enums (added in an earlier migration, retained through V15).

CREATE TABLE IF NOT EXISTS sepay_links (
    wallet_id           uuid PRIMARY KEY REFERENCES wallets(id) ON DELETE CASCADE,
    -- 'oauth'  = OAuth2 authorization-code flow (access + refresh token)
    -- 'static' = personal SePay User API token (single long-lived token, no refresh)
    auth_mode           text NOT NULL DEFAULT 'oauth',
    sepay_user_id       text,
    sepay_bank_account_id integer NOT NULL DEFAULT 0,
    account_number      text,
    account_holder_name text,
    bank_short_name     text,
    access_token        text,                   -- encrypted via ASP.NET Data Protection
    refresh_token       text,                   -- encrypted via ASP.NET Data Protection (oauth only)
    last_synced_at      timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);