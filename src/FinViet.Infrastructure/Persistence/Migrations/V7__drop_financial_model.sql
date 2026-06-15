-- ============================================================
-- Migration V7: Drop financial model in favor of fixed 50/30/20
-- - Removes financial_model and model_allocation tables.
-- - Drops budget_plan.model_id (FK to financial_model).
-- - Drops category.model_bucket (duplicate of expense_class).
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

-- 1. Drop budget_plan.model_id (and its FK) — model is now fixed 50/30/20.
ALTER TABLE budget_plan DROP CONSTRAINT IF EXISTS budget_plan_model_id_fkey;
ALTER TABLE budget_plan DROP COLUMN IF EXISTS model_id;

-- 2. Drop category.model_bucket — duplicated expense_class.
ALTER TABLE category DROP COLUMN IF EXISTS model_bucket;

-- 3. Drop model tables (model_allocation references financial_model).
DROP TABLE IF EXISTS model_allocation;
DROP TABLE IF EXISTS financial_model;
