-- ============================================================
-- Migration V6: Normalize category buckets for Budget Plan logic
-- Repairs legacy placeholder model_bucket values and seeds the
-- default budget categories required by the business logic.
-- Idempotent: re-runnable — including AFTER V7 drops category.model_bucket
-- (model_bucket touches are guarded by a column-existence check).
-- FinViet Project
-- ============================================================

-- 1. Normalize valid singular/plural bucket values (expense_class always exists).
UPDATE category
SET expense_class = CASE UPPER(expense_class)
    WHEN 'NEED' THEN 'NEEDS'
    WHEN 'NEEDS' THEN 'NEEDS'
    WHEN 'WANT' THEN 'WANTS'
    WHEN 'WANTS' THEN 'WANTS'
    WHEN 'SAVING' THEN 'SAVINGS'
    WHEN 'SAVINGS' THEN 'SAVINGS'
    ELSE expense_class
END
WHERE type = 'EXPENSE'
  AND expense_class IS NOT NULL;

-- 2. Seed predefined categories used by the mobile Budget flow.
--    Seed via expense_class only (source of truth post-V7); model_bucket is backfilled below
--    only while that column still exists.
WITH seed_categories(category_name, type, expense_class) AS (
    VALUES
        ('Ăn uống', 'EXPENSE', 'NEEDS'),
        ('Nhà ở & Tiện ích', 'EXPENSE', 'NEEDS'),
        ('Di chuyển', 'EXPENSE', 'NEEDS'),
        ('Sức khỏe & Y tế', 'EXPENSE', 'NEEDS'),
        ('Giáo dục', 'EXPENSE', 'NEEDS'),
        ('Gửi tiền gia đình', 'EXPENSE', 'NEEDS'),
        ('Giải trí', 'EXPENSE', 'WANTS'),
        ('Quần áo & Thời trang', 'EXPENSE', 'WANTS'),
        ('Mua sắm online', 'EXPENSE', 'WANTS'),
        ('Ăn ngoài & Cà phê', 'EXPENSE', 'WANTS'),
        ('Tiết kiệm', 'EXPENSE', 'SAVINGS'),
        ('Đầu tư', 'EXPENSE', 'SAVINGS'),
        ('Chưa phân loại', 'EXPENSE', NULL),
        ('Thu nhập', 'INCOME', NULL)
)
INSERT INTO category (category_id, category_name, type, is_mandatory, expense_class)
SELECT gen_random_uuid(), s.category_name, s.type, TRUE, s.expense_class
FROM seed_categories AS s
WHERE NOT EXISTS (
    SELECT 1
    FROM category AS c
    WHERE c.category_name = s.category_name
      AND c.type = s.type
)
-- Superseded by V9's slug library: once slug categories exist, stop seeding the
-- legacy UUID/uppercase rows (V6 still re-runs every startup but becomes a no-op).
AND NOT EXISTS (
    SELECT 1 FROM category AS slug WHERE slug.category_id LIKE 'cat\_%'
);

-- 3. Legacy model_bucket repair — ONLY while the column still exists (pre-V7).
--    After V7 drops category.model_bucket this block is a no-op, keeping V6 re-runnable.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'category' AND column_name = 'model_bucket'
    ) THEN
        -- Normalize singular/plural model_bucket values.
        EXECUTE $sql$
            UPDATE category
            SET model_bucket = CASE UPPER(model_bucket)
                WHEN 'NEED' THEN 'NEEDS'
                WHEN 'NEEDS' THEN 'NEEDS'
                WHEN 'WANT' THEN 'WANTS'
                WHEN 'WANTS' THEN 'WANTS'
                WHEN 'SAVING' THEN 'SAVINGS'
                WHEN 'SAVINGS' THEN 'SAVINGS'
                ELSE model_bucket
            END
            WHERE type = 'EXPENSE'
              AND model_bucket IS NOT NULL
        $sql$;

        -- Repair invalid/missing model_bucket from the normalized expense_class.
        EXECUTE $sql$
            UPDATE category
            SET model_bucket = expense_class
            WHERE type = 'EXPENSE'
              AND UPPER(COALESCE(expense_class, '')) IN ('NEEDS', 'WANTS', 'SAVINGS')
              AND (
                  model_bucket IS NULL
                  OR UPPER(model_bucket) NOT IN ('NEEDS', 'WANTS', 'SAVINGS')
              )
        $sql$;
    END IF;
END $$;
