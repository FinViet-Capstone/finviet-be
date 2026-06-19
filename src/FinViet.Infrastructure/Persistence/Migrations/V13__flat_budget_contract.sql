-- ============================================================
-- Migration V13: Flat recurring budget contract (buckets / customer_categories / budgets)
-- Targets the v2.1 PLURAL schema produced by V11:
--   categories(id, default_bucket)  — NOT the legacy singular category/expense_class.
-- Core runtime tables (customer, wallet) remain singular in this codebase.
-- Idempotent: re-runnable.
-- ============================================================

-- buckets: code lookup table (spec §4). PK = slug id.
CREATE TABLE IF NOT EXISTS buckets (
    id          varchar(20) PRIMARY KEY,
    name_vi     varchar(40) NOT NULL,
    name_en     varchar(40) NOT NULL,
    color       varchar(7),
    icon        varchar(60),
    sort_order  integer,
    is_locked   boolean NOT NULL DEFAULT false
);

INSERT INTO buckets (id, name_vi, name_en, color, icon, sort_order, is_locked) VALUES
    ('needs',   'Thiết yếu', 'Needs',   '#d0bcff', 'home',         1, false),
    ('wants',   'Mong muốn', 'Wants',   '#ffb690', 'shopping_bag', 2, false),
    ('savings', 'Tiết kiệm', 'Savings', '#4edea3', 'savings',      3, true)
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS customer_categories (
    customer_id uuid NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    category_id varchar(40) NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    bucket_id varchar(20) NOT NULL REFERENCES buckets(id),
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
    category_id varchar(40) NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
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

-- Seed active customer-category rows from the global category library for existing
-- customers. Expense buckets derive from categories.default_bucket (already lowercase).
INSERT INTO customer_categories (customer_id, category_id, bucket_id, source, is_active)
SELECT
    c.customer_id,
    cat.id,
    cat.default_bucket,
    'system',
    true
FROM customer c
CROSS JOIN categories cat
WHERE cat.type = 'expense'
  AND cat.id <> 'cat_savings_goal'
  AND cat.default_bucket IN ('needs', 'wants', 'savings')
ON CONFLICT (customer_id, category_id) DO NOTHING;
