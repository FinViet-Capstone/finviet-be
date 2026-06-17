-- ============================================================
-- Migration V9: Wallet soft delete support
-- Aligns wallet behavior with API/business logic:
-- - wallet rows are kept for historical transactions.
-- - active wallet queries filter is_deleted = false.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'wallet' AND column_name = 'is_deleted'
    ) THEN
        EXECUTE 'ALTER TABLE wallet ADD COLUMN is_deleted boolean NOT NULL DEFAULT false';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'wallet' AND column_name = 'deleted_at'
    ) THEN
        EXECUTE 'ALTER TABLE wallet ADD COLUMN deleted_at timestamptz';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_wallet_customer_active
    ON wallet(customer_id)
    WHERE is_deleted = false;
