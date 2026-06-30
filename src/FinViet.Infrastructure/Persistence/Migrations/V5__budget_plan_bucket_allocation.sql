-- ============================================================
-- Migration V5: 50-30-20 bucket allocation on budget_plan
-- Adds needs_pct / wants_pct / savings_pct with defaults 50/30/20.
-- The three bucket percentages must add up to 100.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

-- 1. Add bucket allocation percentage columns.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='budget_plan' AND column_name='needs_pct') THEN
        EXECUTE 'ALTER TABLE budget_plan ADD COLUMN needs_pct NUMERIC(5,2) NOT NULL DEFAULT 50';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='budget_plan' AND column_name='wants_pct') THEN
        EXECUTE 'ALTER TABLE budget_plan ADD COLUMN wants_pct NUMERIC(5,2) NOT NULL DEFAULT 30';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='budget_plan' AND column_name='savings_pct') THEN
        EXECUTE 'ALTER TABLE budget_plan ADD COLUMN savings_pct NUMERIC(5,2) NOT NULL DEFAULT 20';
    END IF;
END $$;

-- 2. CHECK total bucket allocation is 100, allowing tiny rounding error.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'budget_plan_bucket_sum_check') THEN
        EXECUTE 'ALTER TABLE budget_plan ADD CONSTRAINT budget_plan_bucket_sum_check '
              || 'CHECK (ABS((needs_pct + wants_pct + savings_pct) - 100) < 0.01)';
    END IF;
END $$;

-- 3. CHECK each percentage is in [0, 100].
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'budget_plan_bucket_range_check') THEN
        EXECUTE 'ALTER TABLE budget_plan ADD CONSTRAINT budget_plan_bucket_range_check '
              || 'CHECK (needs_pct BETWEEN 0 AND 100 AND wants_pct BETWEEN 0 AND 100 AND savings_pct BETWEEN 0 AND 100)';
    END IF;
END $$;
