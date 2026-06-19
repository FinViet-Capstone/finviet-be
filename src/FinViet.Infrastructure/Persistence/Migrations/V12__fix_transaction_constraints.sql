-- Repairs check constraints on transactions after the v2.1 rename.
-- The legacy `transaction_source_channel_check` survived the table rename and
-- still demanded uppercase entry-method values (MANUAL/SMS/CSV/LINKED), which
-- rejects the lowercase v2.1 values the application writes (manual, sms_paste, ...).
-- Idempotent: safe to run repeatedly.
DO $$
BEGIN
    IF to_regclass('public.transactions') IS NULL THEN
        RETURN;
    END IF;

    -- Drop any stale entry_method / type check constraints from the legacy schema.
    ALTER TABLE transactions DROP CONSTRAINT IF EXISTS transaction_source_channel_check;
    ALTER TABLE transactions DROP CONSTRAINT IF EXISTS transaction_transaction_type_check;
    ALTER TABLE transactions DROP CONSTRAINT IF EXISTS transactions_entry_method_check;
    ALTER TABLE transactions DROP CONSTRAINT IF EXISTS transactions_type_check;

    -- Normalize any pre-existing rows to the v2.1 lowercase vocabulary.
    UPDATE transactions SET entry_method = CASE upper(entry_method)
        WHEN 'MANUAL' THEN 'manual'
        WHEN 'SMS'    THEN 'sms_paste'
        WHEN 'CSV'    THEN 'csv_import'
        WHEN 'LINKED' THEN 'sepay_sync'
        ELSE lower(entry_method)
    END
    WHERE entry_method IS NOT NULL;

    UPDATE transactions SET type = lower(type) WHERE type IS NOT NULL;

    ALTER TABLE transactions ADD CONSTRAINT transactions_entry_method_check
        CHECK (entry_method IN ('manual', 'sms_paste', 'csv_import', 'sepay_sync'));

    ALTER TABLE transactions ADD CONSTRAINT transactions_type_check
        CHECK (type IN ('income', 'expense', 'transfer_out', 'transfer_in'));
END $$;
