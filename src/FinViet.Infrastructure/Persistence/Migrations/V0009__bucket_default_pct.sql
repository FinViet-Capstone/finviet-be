-- System-wide default budget allocation ratio (UC-15 "Update Budget Selection Value").
--
-- Admin-editable default Needs/Wants/Savings percentages, stored directly on the 3-row
-- public.buckets lookup table rather than a new table, since that table already is the
-- needs/wants/savings unit. Seeded to match the hard-coded 50/30/20 defaults that
-- Customer.NeedsPct/WantsPct/SavingsPct (C# property initializers) have always used —
-- RegisterCommandHandler now reads these columns instead of relying on those CLR defaults.
--
-- Scoped deliberately to *new* registrations only: changing a system default should not
-- silently reallocate existing customers' buckets, which they may have already customized
-- via POST /api/profile/income-allocation.

ALTER TABLE public.buckets ADD COLUMN default_pct numeric(5,2);

UPDATE public.buckets SET default_pct = 50 WHERE id = 'needs';
UPDATE public.buckets SET default_pct = 30 WHERE id = 'wants';
UPDATE public.buckets SET default_pct = 20 WHERE id = 'savings';
