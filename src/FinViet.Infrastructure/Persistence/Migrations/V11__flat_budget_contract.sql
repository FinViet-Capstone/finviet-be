-- ============================================================
-- Migration V11: Flat recurring budget contract from finviet.zip
-- - Adds buckets, customer_categories, and budgets without removing
--   legacy budget_plan/category_budget tables.
-- - Uses existing singular core tables: customer, category, wallet.
-- Idempotent: re-runnable.
-- ============================================================

CREATE TABLE IF NOT EXISTS buckets (
    bucket_id varchar(20) PRIMARY KEY,
    name_vi varchar(80) NOT NULL,
    name_en varchar(80) NOT NULL,
    sort_order integer NOT NULL,
    is_locked boolean NOT NULL DEFAULT false
);

INSERT INTO buckets (bucket_id, name_vi, name_en, sort_order, is_locked) VALUES
    ('needs', 'Nhu cầu thiết yếu', 'Needs', 1, false),
    ('wants', 'Mong muốn', 'Wants', 2, false),
    ('savings', 'Tiết kiệm', 'Savings', 3, true)
ON CONFLICT (bucket_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS customer_categories (
    customer_id uuid NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    category_id varchar(40) NOT NULL REFERENCES category(category_id) ON DELETE CASCADE,
    bucket_id varchar(20) NOT NULL REFERENCES buckets(bucket_id),
    source varchar(20) NOT NULL DEFAULT 'system',
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, category_id)
);

CREATE INDEX IF NOT EXISTS idx_customer_categories_customer_active
    ON customer_categories(customer_id)
    WHERE is_active = true;

CREATE TABLE IF NOT EXISTS budgets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    category_id varchar(40) NOT NULL REFERENCES category(category_id) ON DELETE CASCADE,
    wallet_id uuid REFERENCES wallet(wallet_id) ON DELETE CASCADE,
    monthly_limit numeric(15,2) NOT NULL CHECK (monthly_limit > 0),
    last_alert_threshold numeric(5,2) NOT NULL DEFAULT 0,
    last_alert_month varchar(7),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_budget
    ON budgets(customer_id, category_id, COALESCE(wallet_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE INDEX IF NOT EXISTS idx_budgets_customer ON budgets(customer_id);
CREATE INDEX IF NOT EXISTS idx_budgets_wallet ON budgets(wallet_id);

-- Preserve data from the older singular budget table if it exists in a local
-- database created before the flat contract standardized on budgets.
DO $$
BEGIN
    IF to_regclass('public.budget') IS NOT NULL THEN
        INSERT INTO budgets (
            id,
            customer_id,
            category_id,
            wallet_id,
            monthly_limit,
            last_alert_threshold,
            last_alert_month,
            created_at,
            updated_at
        )
        SELECT
            b.budget_id,
            b.customer_id,
            b.category_id,
            b.wallet_id,
            b.monthly_limit,
            COALESCE(b.last_alert_threshold, 0),
            b.last_alert_month,
            COALESCE(b.created_at, now()),
            COALESCE(b.updated_at, now())
        FROM budget b
        WHERE EXISTS (
            SELECT 1 FROM customer c WHERE c.customer_id = b.customer_id
        )
          AND EXISTS (
            SELECT 1 FROM category cat WHERE cat.category_id = b.category_id
        )
          AND NOT EXISTS (
            SELECT 1
            FROM budgets existing
            WHERE existing.customer_id = b.customer_id
              AND existing.category_id = b.category_id
              AND COALESCE(existing.wallet_id, '00000000-0000-0000-0000-000000000000'::uuid)
                  = COALESCE(b.wallet_id, '00000000-0000-0000-0000-000000000000'::uuid)
        )
        ON CONFLICT (id) DO NOTHING;
    END IF;
END $$;

-- Seed active customer category rows from the global category library for
-- existing customers. Expense buckets are derived from category.expense_class.
INSERT INTO customer_categories (customer_id, category_id, bucket_id, source, is_active)
SELECT
    c.customer_id,
    cat.category_id,
    lower(cat.expense_class),
    'system',
    true
FROM customer c
CROSS JOIN category cat
WHERE cat.type = 'expense'
  AND cat.category_id <> 'cat_savings_goal'
  AND cat.expense_class IN ('NEEDS', 'WANTS', 'SAVINGS')
ON CONFLICT (customer_id, category_id) DO NOTHING;
