-- Finverse integration for the externally provisioned v3 schema.
-- Run this migration manually before starting the API. It is idempotent.

ALTER TYPE wallet_type ADD VALUE IF NOT EXISTS 'finverse_linked';
ALTER TYPE entry_method ADD VALUE IF NOT EXISTS 'finverse_sync';

CREATE TABLE IF NOT EXISTS finverse_links (
    wallet_id           uuid PRIMARY KEY REFERENCES wallets(id) ON DELETE CASCADE,
    login_identity_id   text NOT NULL,
    access_token        text,
    refresh_token       text,
    finverse_account_id text,
    institution_name    text,
    account_mask        text,
    last_synced_at      timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now()
);

-- Preserve data from the earlier three-table implementation when upgrading an
-- environment that already ran a previous V14 draft.
DO $$
BEGIN
    IF to_regclass('public.finverse_connections') IS NOT NULL
       AND to_regclass('public.finverse_wallet_links') IS NOT NULL THEN
        INSERT INTO finverse_links (
            wallet_id,
            login_identity_id,
            access_token,
            refresh_token,
            finverse_account_id,
            institution_name,
            account_mask,
            last_synced_at,
            created_at,
            updated_at)
        SELECT
            wallet_link.wallet_id,
            connection.login_identity_id,
            connection.access_token_protected,
            connection.refresh_token_protected,
            wallet_link.account_id,
            wallet_link.institution_name,
            wallet_link.account_number_masked,
            wallet_link.last_synced_at,
            wallet_link.created_at,
            wallet_link.updated_at
        FROM finverse_wallet_links AS wallet_link
        INNER JOIN finverse_connections AS connection
            ON connection.id = wallet_link.connection_id
        ON CONFLICT (wallet_id) DO UPDATE SET
            login_identity_id = EXCLUDED.login_identity_id,
            access_token = EXCLUDED.access_token,
            refresh_token = EXCLUDED.refresh_token,
            finverse_account_id = EXCLUDED.finverse_account_id,
            institution_name = EXCLUDED.institution_name,
            account_mask = EXCLUDED.account_mask,
            last_synced_at = EXCLUDED.last_synced_at,
            updated_at = EXCLUDED.updated_at;
    END IF;
END $$;

DROP TABLE IF EXISTS finverse_link_sessions;
DROP TABLE IF EXISTS finverse_wallet_links;
DROP TABLE IF EXISTS finverse_connections;
