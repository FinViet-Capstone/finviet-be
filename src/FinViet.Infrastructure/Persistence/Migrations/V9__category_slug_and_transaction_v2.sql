-- ============================================================
-- Migration V9: Category slug PK + Transaction v2 realignment
-- - Converts category.category_id from uuid -> varchar(40) slug
--   (e.g. 'cat_food'); re-points every category FK column to varchar(40).
-- - Adds new transaction columns (customer_id, merchant, entry_method,
--   transfer_pair_id, external_id, description, created_at, updated_at)
--   and lowercase type/entry_method CHECK constraints.
-- - Wipes & reseeds the global category library with 18 canonical slugs.
-- NO buckets table (expense_class stays the bucket indicator).
-- Idempotent: re-runnable (guards on type, IF NOT EXISTS, ON CONFLICT).
-- FinViet Project
-- ============================================================

-- ─────────────────────────────────────────────────────────────
-- 0. One-time wipe + slug conversion (guarded: only runs while
--    category_id is still uuid, so re-runs are no-ops).
-- ─────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'category'
          AND column_name = 'category_id'
          AND data_type = 'uuid'
    ) THEN
        -- Wipe & reseed approach: clear category + every dependent so the
        -- PK type can change without rewriting old uuid FK rows.
        DELETE FROM transaction;
        DELETE FROM category_budget;
        DELETE FROM category_correction_log;
        DELETE FROM beneficiary_rule;
        DELETE FROM user_category_buckets;
        UPDATE category_request SET created_category_id = NULL;
        DELETE FROM category;

        -- Drop FK constraints referencing category(category_id).
        ALTER TABLE transaction             DROP CONSTRAINT IF EXISTS transaction_category_id_fkey;
        ALTER TABLE transaction             DROP CONSTRAINT IF EXISTS transaction_ai_category_guess_fkey;
        ALTER TABLE category_budget         DROP CONSTRAINT IF EXISTS category_budget_category_id_fkey;
        ALTER TABLE category_correction_log DROP CONSTRAINT IF EXISTS category_correction_log_corrected_category_id_fkey;
        ALTER TABLE category_request        DROP CONSTRAINT IF EXISTS category_request_created_category_id_fkey;
        ALTER TABLE beneficiary_rule        DROP CONSTRAINT IF EXISTS beneficiary_rule_category_id_fkey;
        ALTER TABLE user_category_buckets   DROP CONSTRAINT IF EXISTS user_category_buckets_category_id_fkey;

        -- Convert PK column: drop uuid default, retype to slug.
        ALTER TABLE category ALTER COLUMN category_id DROP DEFAULT;
        ALTER TABLE category ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;

        -- Convert every FK column to varchar(40).
        ALTER TABLE transaction             ALTER COLUMN category_id          TYPE varchar(40) USING category_id::text;
        ALTER TABLE transaction             ALTER COLUMN ai_category_guess    TYPE varchar(40) USING ai_category_guess::text;
        ALTER TABLE category_budget         ALTER COLUMN category_id          TYPE varchar(40) USING category_id::text;
        ALTER TABLE category_correction_log ALTER COLUMN corrected_category_id TYPE varchar(40) USING corrected_category_id::text;
        ALTER TABLE category_request        ALTER COLUMN created_category_id   TYPE varchar(40) USING created_category_id::text;
        ALTER TABLE beneficiary_rule        ALTER COLUMN category_id          TYPE varchar(40) USING category_id::text;
        ALTER TABLE user_category_buckets   ALTER COLUMN category_id          TYPE varchar(40) USING category_id::text;

        -- Re-add FK constraints (same delete behavior as before).
        ALTER TABLE transaction
            ADD CONSTRAINT transaction_category_id_fkey
            FOREIGN KEY (category_id) REFERENCES category(category_id) ON DELETE SET NULL;
        ALTER TABLE transaction
            ADD CONSTRAINT transaction_ai_category_guess_fkey
            FOREIGN KEY (ai_category_guess) REFERENCES category(category_id) ON DELETE SET NULL;
        ALTER TABLE category_budget
            ADD CONSTRAINT category_budget_category_id_fkey
            FOREIGN KEY (category_id) REFERENCES category(category_id) ON DELETE CASCADE;
        ALTER TABLE category_correction_log
            ADD CONSTRAINT category_correction_log_corrected_category_id_fkey
            FOREIGN KEY (corrected_category_id) REFERENCES category(category_id) ON DELETE CASCADE;
        ALTER TABLE category_request
            ADD CONSTRAINT category_request_created_category_id_fkey
            FOREIGN KEY (created_category_id) REFERENCES category(category_id) ON DELETE SET NULL;
        ALTER TABLE beneficiary_rule
            ADD CONSTRAINT beneficiary_rule_category_id_fkey
            FOREIGN KEY (category_id) REFERENCES category(category_id) ON DELETE CASCADE;
        ALTER TABLE user_category_buckets
            ADD CONSTRAINT user_category_buckets_category_id_fkey
            FOREIGN KEY (category_id) REFERENCES category(category_id) ON DELETE CASCADE;
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────
-- 1. New category descriptive columns (slug library metadata).
-- ─────────────────────────────────────────────────────────────
ALTER TABLE category ADD COLUMN IF NOT EXISTS name_vi    varchar(80);
ALTER TABLE category ADD COLUMN IF NOT EXISTS name_en    varchar(80);
ALTER TABLE category ADD COLUMN IF NOT EXISTS icon       varchar(60);
ALTER TABLE category ADD COLUMN IF NOT EXISTS color      varchar(7);
ALTER TABLE category ADD COLUMN IF NOT EXISTS sort_order integer;

-- ─────────────────────────────────────────────────────────────
-- 2. Transaction v2 columns.
-- ─────────────────────────────────────────────────────────────
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS customer_id      uuid;
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS merchant         varchar(255);
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS entry_method     varchar(20);
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS transfer_pair_id uuid;
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS external_id      varchar(120);
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS description      text;
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS created_at       timestamptz NOT NULL DEFAULT now();
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS updated_at       timestamptz NOT NULL DEFAULT now();

-- Back-fill merchant from legacy beneficiary_name; description from note.
UPDATE transaction SET merchant    = beneficiary_name WHERE merchant    IS NULL AND beneficiary_name IS NOT NULL;
UPDATE transaction SET description  = note            WHERE description  IS NULL AND note             IS NOT NULL;

-- customer_id FK -> customer (rows are empty after wipe; safe to add nullable).
ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_customer_id_fkey;
ALTER TABLE transaction
    ADD CONSTRAINT transaction_customer_id_fkey
    FOREIGN KEY (customer_id) REFERENCES customer(customer_id) ON DELETE CASCADE;

-- SePay dedup: unique external_id when present.
CREATE UNIQUE INDEX IF NOT EXISTS uq_tx_external
    ON transaction(external_id) WHERE external_id IS NOT NULL;

-- Lowercase value-set CHECK constraints (drop-then-add for idempotency).
ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_type_check;
ALTER TABLE transaction
    ADD CONSTRAINT transaction_type_check
    CHECK (transaction_type IN ('expense', 'income', 'transfer_out', 'transfer_in'));

ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_entry_method_check;
ALTER TABLE transaction
    ADD CONSTRAINT transaction_entry_method_check
    CHECK (entry_method IS NULL OR entry_method IN ('manual', 'photo', 'sms_paste', 'csv_import', 'sepay_sync'));

-- Legacy source_channel is superseded by entry_method: relax NOT NULL + old value CHECK
-- so new rows (which leave it NULL) insert cleanly.
ALTER TABLE transaction ALTER COLUMN source_channel DROP NOT NULL;
ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_source_channel_check;

-- ─────────────────────────────────────────────────────────────
-- 3. Seed the canonical global category library (fixed slugs).
--    expense_class doubles as the bucket indicator (NEEDS/WANTS/SAVINGS).
--    income rows have expense_class = NULL.
-- ─────────────────────────────────────────────────────────────
INSERT INTO category (category_id, category_name, name_vi, name_en, type, is_mandatory, expense_class, sort_order) VALUES
    -- EXPENSE — Needs
    ('cat_food',      'Ăn uống',              'Ăn uống',              'Food & Drink',     'expense', TRUE, 'NEEDS',   1),
    ('cat_housing',   'Nhà ở & Tiện ích',     'Nhà ở & Tiện ích',     'Housing & Bills',  'expense', TRUE, 'NEEDS',   2),
    ('cat_transport', 'Di chuyển',            'Di chuyển',            'Transport',        'expense', TRUE, 'NEEDS',   3),
    ('cat_health',    'Sức khỏe & Y tế',      'Sức khỏe & Y tế',      'Health',           'expense', TRUE, 'NEEDS',   4),
    ('cat_education', 'Giáo dục',             'Giáo dục',             'Education',        'expense', TRUE, 'NEEDS',   5),
    ('cat_family',    'Gửi tiền gia đình',    'Gửi tiền gia đình',    'Family',           'expense', TRUE, 'NEEDS',   6),
    -- EXPENSE — Wants
    ('cat_entertain', 'Giải trí',             'Giải trí',             'Entertainment',    'expense', TRUE, 'WANTS',   7),
    ('cat_beauty',    'Quần áo & Thời trang', 'Quần áo & Thời trang', 'Fashion & Beauty', 'expense', TRUE, 'WANTS',   8),
    ('cat_shopping',  'Mua sắm online',       'Mua sắm online',       'Online Shopping',  'expense', TRUE, 'WANTS',   9),
    ('cat_dining',    'Ăn ngoài & Cà phê',    'Ăn ngoài & Cà phê',    'Dining Out',       'expense', TRUE, 'WANTS',  10),
    -- EXPENSE — Savings
    ('cat_savings',      'Tiết kiệm',         'Tiết kiệm',            'Savings',          'expense', TRUE, 'SAVINGS', 11),
    ('cat_invest',       'Đầu tư',            'Đầu tư',               'Investment',       'expense', TRUE, 'SAVINGS', 12),
    ('cat_savings_goal', 'Nạp mục tiêu',      'Nạp mục tiêu',         'Goal Funding',     'expense', TRUE, 'SAVINGS', 13),
    -- INCOME (expense_class = NULL)
    ('cat_salary',            'Lương',          'Lương',          'Salary',          'income', TRUE, NULL, 14),
    ('cat_freelance',         'Làm thêm',       'Làm thêm',       'Freelance',       'income', TRUE, NULL, 15),
    ('cat_investment_return', 'Lợi nhuận đầu tư','Lợi nhuận đầu tư','Investment Return','income', TRUE, NULL, 16),
    ('cat_gift',              'Quà tặng',       'Quà tặng',       'Gift',            'income', TRUE, NULL, 17),
    ('cat_income_other',      'Thu nhập khác',  'Thu nhập khác',  'Other Income',    'income', TRUE, NULL, 18)
ON CONFLICT (category_id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────
-- 4. Remove any legacy non-slug categories (UUID ids seeded by the
--    pre-v2 V6 migration). The new contract uses slug ids only.
--    Null out dependent references first so the delete never violates FKs.
-- ─────────────────────────────────────────────────────────────
UPDATE transaction              SET category_id = NULL       WHERE category_id IS NOT NULL AND category_id NOT LIKE 'cat\_%';
UPDATE transaction              SET ai_category_guess = NULL WHERE ai_category_guess IS NOT NULL AND ai_category_guess NOT LIKE 'cat\_%';
UPDATE category_correction_log  SET corrected_category_id = NULL WHERE corrected_category_id IS NOT NULL AND corrected_category_id NOT LIKE 'cat\_%';
UPDATE category_request         SET created_category_id = NULL   WHERE created_category_id IS NOT NULL AND created_category_id NOT LIKE 'cat\_%';
DELETE FROM category_budget     WHERE category_id IS NOT NULL AND category_id NOT LIKE 'cat\_%';
DELETE FROM beneficiary_rule    WHERE category_id NOT LIKE 'cat\_%';
DELETE FROM user_category_buckets WHERE category_id NOT LIKE 'cat\_%';
DELETE FROM category            WHERE category_id NOT LIKE 'cat\_%';
