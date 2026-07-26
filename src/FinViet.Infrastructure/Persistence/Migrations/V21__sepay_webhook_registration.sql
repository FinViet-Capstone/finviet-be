-- Stores the id of the webhook FinViet registers on SePay for a linked bank account, so the
-- registration can be adopted, reported and deleted again on unlink instead of being orphaned
-- in the user's SePay dashboard. Run manually before starting the API. Idempotent.

ALTER TABLE sepay_links
    ADD COLUMN IF NOT EXISTS sepay_webhook_id integer;
