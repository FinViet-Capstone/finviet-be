-- ============================================================
-- Migration V9: Restore category display columns
-- Re-adds the localized name / presentation columns that the
-- legacy project's CategoryResponse expects, so older queries
-- selecting these columns no longer fail against this database.
--   - name_vi    : Vietnamese display name
--   - name_en    : English display name
--   - icon       : icon key/name
--   - color      : hex color (e.g. #1A2B3C)
--   - sort_order : manual ordering for the category picker
-- All columns are nullable; no data backfill is required.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

ALTER TABLE category ADD COLUMN IF NOT EXISTS name_vi    varchar(80);
ALTER TABLE category ADD COLUMN IF NOT EXISTS name_en    varchar(80);
ALTER TABLE category ADD COLUMN IF NOT EXISTS icon       varchar(60);
ALTER TABLE category ADD COLUMN IF NOT EXISTS color      varchar(7);
ALTER TABLE category ADD COLUMN IF NOT EXISTS sort_order integer;

-- Optional convenience: seed name_vi from the existing category_name
-- for rows where it is still null, so the legacy UI has something to show.
UPDATE category
SET name_vi = category_name
WHERE name_vi IS NULL;
