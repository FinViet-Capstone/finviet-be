-- ============================================================
-- Migration V6: Normalize category buckets for Budget Plan logic
-- Repairs legacy placeholder model_bucket values and seeds the
-- default budget categories required by the business logic.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

-- 1. Normalize valid singular/plural bucket values.
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
  AND model_bucket IS NOT NULL;

-- 2. Repair legacy invalid model_bucket values such as 'string'
--    by copying the normalized expense_class bucket.
UPDATE category
SET model_bucket = expense_class
WHERE type = 'EXPENSE'
  AND UPPER(COALESCE(expense_class, '')) IN ('NEEDS', 'WANTS', 'SAVINGS')
  AND (
      model_bucket IS NULL
      OR UPPER(model_bucket) NOT IN ('NEEDS', 'WANTS', 'SAVINGS')
  );

-- 3. Seed predefined categories used by the mobile Budget flow.
WITH seed_categories(category_name, type, expense_class, model_bucket) AS (
    VALUES
        ('Ăn uống', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Nhà ở & Tiện ích', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Di chuyển', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Sức khỏe & Y tế', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Giáo dục', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Gửi tiền gia đình', 'EXPENSE', 'NEEDS', 'NEEDS'),
        ('Giải trí', 'EXPENSE', 'WANTS', 'WANTS'),
        ('Quần áo & Thời trang', 'EXPENSE', 'WANTS', 'WANTS'),
        ('Mua sắm online', 'EXPENSE', 'WANTS', 'WANTS'),
        ('Ăn ngoài & Cà phê', 'EXPENSE', 'WANTS', 'WANTS'),
        ('Tiết kiệm', 'EXPENSE', 'SAVINGS', 'SAVINGS'),
        ('Đầu tư', 'EXPENSE', 'SAVINGS', 'SAVINGS'),
        ('Chưa phân loại', 'EXPENSE', NULL, NULL),
        ('Thu nhập', 'INCOME', NULL, NULL)
)
INSERT INTO category (category_id, category_name, type, is_mandatory, expense_class, model_bucket)
SELECT gen_random_uuid(), s.category_name, s.type, TRUE, s.expense_class, s.model_bucket
FROM seed_categories AS s
WHERE NOT EXISTS (
    SELECT 1
    FROM category AS c
    WHERE c.category_name = s.category_name
      AND c.type = s.type
);
