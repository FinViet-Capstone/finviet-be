-- Allow fractional 50-30-20 bucket allocations.
--
-- customers.needs_pct/wants_pct/savings_pct and income_allocation_settings.needs_pct/
-- wants_pct/savings_pct were `integer`, forcing every bucket share to a whole percent.
-- The mobile app's numpad now lets a customer type an exact VND amount for a bucket
-- (e.g. 15.300.000đ on a 100.000.000đ income = 15.3%) — storing only the nearest whole
-- percent would silently discard that precision on every save. numeric(5,2) matches the
-- monthly_income numeric(15,2) convention already used on both tables and comfortably
-- covers the 0.00-100.00 range with 2 decimal places.
--
-- Safe, non-lossy widening conversion for existing rows: every integer 0-100 already
-- fits exactly in numeric(5,2). Both chk_buckets_sum and chk_income_allocation_pct_sum
-- keep working unchanged — summing three numeric columns against the integer literal
-- 100 is still valid.

ALTER TABLE public.customers
    ALTER COLUMN needs_pct TYPE numeric(5,2) USING needs_pct::numeric(5,2),
    ALTER COLUMN wants_pct TYPE numeric(5,2) USING wants_pct::numeric(5,2),
    ALTER COLUMN savings_pct TYPE numeric(5,2) USING savings_pct::numeric(5,2);

ALTER TABLE public.customers
    ALTER COLUMN needs_pct SET DEFAULT 50,
    ALTER COLUMN wants_pct SET DEFAULT 30,
    ALTER COLUMN savings_pct SET DEFAULT 20;

ALTER TABLE public.income_allocation_settings
    ALTER COLUMN needs_pct TYPE numeric(5,2) USING needs_pct::numeric(5,2),
    ALTER COLUMN wants_pct TYPE numeric(5,2) USING wants_pct::numeric(5,2),
    ALTER COLUMN savings_pct TYPE numeric(5,2) USING savings_pct::numeric(5,2);
