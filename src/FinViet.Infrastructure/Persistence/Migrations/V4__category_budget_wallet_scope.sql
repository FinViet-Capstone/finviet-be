-- ============================================================
-- Migration V4: Per-wallet budget scope + alert tracking
-- Adds category_budget.wallet_id (NULL = applies to all wallets)
-- and last_alert_threshold to support 80%/100% milestone alerts.
-- Idempotent: re-runnable, guards every ALTER/CREATE.
-- FinViet Project
-- ============================================================

-- 1. category_budget.wallet_id
--    NULL  = budget applies to every wallet for the category.
--    value = budget applies to one specific wallet/category pair.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='category_budget' AND column_name='wallet_id') THEN
        EXECUTE 'ALTER TABLE category_budget ADD COLUMN wallet_id UUID';
    END IF;
END $$;

-- FK to wallet, cascade delete when the wallet is deleted.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'category_budget_wallet_id_fkey') THEN
        EXECUTE 'ALTER TABLE category_budget ADD CONSTRAINT category_budget_wallet_id_fkey '
              || 'FOREIGN KEY (wallet_id) REFERENCES wallet(wallet_id) ON DELETE CASCADE';
    END IF;
END $$;

-- 2. last_alert_threshold: latest sent alert milestone (0, 80, 100).
--    Used to send each alert milestone only once.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='category_budget' AND column_name='last_alert_threshold') THEN
        EXECUTE 'ALTER TABLE category_budget ADD COLUMN last_alert_threshold NUMERIC(5,2) NOT NULL DEFAULT 0';
    END IF;
END $$;

-- 3. Replace old unique constraint (plan_id, category_id), if present,
--    with uniqueness scoped by plan/category/wallet.
--    Rows (plan, category, NULL) and (plan, category, wallet_x) are distinct.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'category_budget_plan_id_category_id_key') THEN
        EXECUTE 'ALTER TABLE category_budget DROP CONSTRAINT category_budget_plan_id_category_id_key';
    END IF;
END $$;

-- Unique index treats NULL wallet_id as a single sentinel value.
CREATE UNIQUE INDEX IF NOT EXISTS ux_category_budget_plan_category_wallet
    ON category_budget (plan_id, category_id, COALESCE(wallet_id, '00000000-0000-0000-0000-000000000000'::uuid));

-- 4. Index supporting wallet scoped lookups.
CREATE INDEX IF NOT EXISTS idx_category_budget_wallet_id ON category_budget(wallet_id);
