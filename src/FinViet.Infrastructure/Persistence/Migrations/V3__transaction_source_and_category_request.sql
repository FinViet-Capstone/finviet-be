-- ============================================================
-- Migration V3: Transaction source channel, wallet NOT NULL,
--               and category_request workflow
-- Idempotent: re-runnable, guards every ALTER/CREATE.
-- FinViet Project
-- ============================================================

-- 1. Add transaction.source_channel (how the transaction entered the system)
--    Allowed: SMS, MANUAL, CSV, LINKED. Backfill existing rows then enforce NOT NULL.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='transaction' AND column_name='source_channel') THEN
        EXECUTE 'ALTER TABLE transaction ADD COLUMN source_channel VARCHAR(20)';
    END IF;
END $$;

-- Backfill: rows that came from an import batch are CSV/SMS; the rest are MANUAL.
UPDATE transaction t
SET source_channel = CASE
        WHEN t.batch_id IS NOT NULL AND b.file_name = 'sms-paste' THEN 'SMS'
        WHEN t.batch_id IS NOT NULL THEN 'CSV'
        ELSE 'MANUAL'
    END
FROM (SELECT batch_id, file_name FROM import_batch) b
WHERE t.batch_id = b.batch_id AND t.source_channel IS NULL;

UPDATE transaction
SET source_channel = 'MANUAL'
WHERE source_channel IS NULL;

-- Enforce NOT NULL + allowed values
DO $$
BEGIN
    EXECUTE 'ALTER TABLE transaction ALTER COLUMN source_channel SET NOT NULL';

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'transaction_source_channel_check') THEN
        EXECUTE 'ALTER TABLE transaction ADD CONSTRAINT transaction_source_channel_check '
              || 'CHECK (source_channel IN (''SMS'', ''MANUAL'', ''CSV'', ''LINKED''))';
    END IF;
END $$;

-- 2. Enforce wallet_id NOT NULL on transaction (a transaction must belong to a wallet).
--    Only flip to NOT NULL if no orphan rows exist, to avoid failing on dirty data.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM transaction WHERE wallet_id IS NULL) THEN
        EXECUTE 'ALTER TABLE transaction ALTER COLUMN wallet_id SET NOT NULL';
    END IF;
END $$;

-- 3. category_request: users submit requests for new categories; admins approve/reject.
CREATE TABLE IF NOT EXISTS category_request
(
    request_id     UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id    UUID         NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    category_name  VARCHAR(100) NOT NULL,
    type           VARCHAR(50)  NOT NULL CHECK (type IN ('INCOME', 'EXPENSE')),
    expense_class  VARCHAR(50),
    note           VARCHAR(500),
    status         VARCHAR(20)  NOT NULL DEFAULT 'PENDING'
                   CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
    reviewed_by    UUID         REFERENCES admin(admin_id) ON DELETE SET NULL,
    review_note    VARCHAR(500),
    created_category_id UUID    REFERENCES category(category_id) ON DELETE SET NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    reviewed_at    TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_category_request_customer_id ON category_request(customer_id);
CREATE INDEX IF NOT EXISTS idx_category_request_status      ON category_request(status);
