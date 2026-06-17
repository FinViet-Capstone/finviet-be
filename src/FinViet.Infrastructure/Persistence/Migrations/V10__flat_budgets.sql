-- ============================================================
-- Migration V10: Flat recurring budgets (migrate off budget_plan model)
-- Aligns with binding schema v2.1 (db_schema.sql §5) + BUSINESS_LOGIC §6/§9/§10:
--   - 50/30/20 % lives on `customer` (source of truth), not on a plan.
--   - `budget` is flat + recurring: one row per (customer, category, wallet),
--     `monthly_limit`; `spent` is DERIVED per viewed month (ICT), never stored.
--   - Alert dedup via last_alert_threshold + last_alert_month (reset each month).
-- Best-effort copies existing data from the latest budget_plan/category_budget.
-- Old budget_plan/category_budget tables are LEFT in place (dropped in a later
-- migration once verified) to avoid touching the notification FK and data loss.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

-- 1. Customer bucket percentages (source of truth for 50-30-20).
DO $$
BEGIN
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

-- 2. Flat budget table (recurring; spent computed dynamically per month).
CREATE TABLE IF NOT EXISTS budget (
    budget_id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id          uuid NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    category_id          uuid NOT NULL REFERENCES category(category_id),
    wallet_id            uuid REFERENCES wallet(wallet_id),
    monthly_limit        numeric(15,2) NOT NULL,
    last_alert_threshold numeric(5,2) NOT NULL DEFAULT 0,
    last_alert_month     varchar(7),                      -- 'YYYY-MM' (ICT) → reset alert flag each month
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now()
);

-- One budget per (customer, category, wallet); NULL wallet = all-wallet scope.
CREATE UNIQUE INDEX IF NOT EXISTS uq_budget_scope
    ON budget(customer_id, category_id, COALESCE(wallet_id, '00000000-0000-0000-0000-000000000000'));
CREATE INDEX IF NOT EXISTS idx_budget_customer ON budget(customer_id);

-- 3. Best-effort: copy 50/30/20 from each customer's most-recent plan → customer columns.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='budget_plan') THEN
        UPDATE customer c
        SET needs_pct   = p.needs_pct::integer,
            wants_pct   = p.wants_pct::integer,
            savings_pct = p.savings_pct::integer
        FROM (
            SELECT DISTINCT ON (customer_id) customer_id, needs_pct, wants_pct, savings_pct
            FROM budget_plan
            ORDER BY customer_id, start_date DESC
        ) p
        WHERE p.customer_id = c.customer_id
          AND (p.needs_pct + p.wants_pct + p.savings_pct) = 100;
    END IF;
END $$;

-- 4. Best-effort: copy the latest plan's category budgets → flat budget rows.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='category_budget') THEN
        INSERT INTO budget (customer_id, category_id, wallet_id, monthly_limit, last_alert_threshold)
        SELECT p.customer_id, cb.category_id, cb.wallet_id, cb.amount_limit, COALESCE(cb.last_alert_threshold, 0)
        FROM category_budget cb
        JOIN (
            SELECT DISTINCT ON (customer_id) plan_id, customer_id
            FROM budget_plan
            ORDER BY customer_id, start_date DESC
        ) p ON p.plan_id = cb.plan_id
        WHERE cb.category_id IS NOT NULL
        ON CONFLICT DO NOTHING;
    END IF;
END $$;
