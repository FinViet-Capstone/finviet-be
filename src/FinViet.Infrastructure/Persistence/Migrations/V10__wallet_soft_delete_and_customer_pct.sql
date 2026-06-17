-- ============================================================
-- Migration V10: Wallet soft-delete + customer 50/30/20 percentages
-- (Port phần wallet/budget của nhánh vinhtk lên nền slug-v2 của dunglt.)
--   - wallet.is_deleted / deleted_at: giữ lịch sử giao dịch khi xóa ví.
--   - customer.needs_pct / wants_pct / savings_pct: nguồn % hũ (cho flat budget V11).
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='wallet' AND column_name='is_deleted') THEN
        EXECUTE 'ALTER TABLE wallet ADD COLUMN is_deleted boolean NOT NULL DEFAULT false';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='wallet' AND column_name='deleted_at') THEN
        EXECUTE 'ALTER TABLE wallet ADD COLUMN deleted_at timestamptz';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='needs_pct') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN needs_pct integer NOT NULL DEFAULT 50';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='wants_pct') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN wants_pct integer NOT NULL DEFAULT 30';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='savings_pct') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN savings_pct integer NOT NULL DEFAULT 20';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_wallet_customer_active
    ON wallet(customer_id)
    WHERE is_deleted = false;
