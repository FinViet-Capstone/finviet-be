-- Replace VNPay-specific columns on the payments table with PayOS-specific ones.
-- VNPay was never activated (no business registration), so all existing payment rows
-- are test data at most. PayOS uses a numeric order code instead of a string txn ref,
-- and confirms via webhook (POST) rather than IPN (GET).

-- Drop VNPay indexes first (they reference columns being dropped).
DROP INDEX IF EXISTS public.uq_payments_vnp_txn_ref;
DROP INDEX IF EXISTS public.uq_payments_vnp_transaction_no;

-- Drop VNPay-specific columns.
ALTER TABLE public.payments
    DROP COLUMN IF EXISTS vnp_txn_ref,
    DROP COLUMN IF EXISTS vnp_transaction_no,
    DROP COLUMN IF EXISTS vnp_response_code,
    DROP COLUMN IF EXISTS vnp_transaction_status,
    DROP COLUMN IF EXISTS vnp_bank_code,
    DROP COLUMN IF EXISTS vnp_card_type,
    DROP COLUMN IF EXISTS vnp_pay_date;

-- Rename raw_ipn_payload to raw_webhook_payload (same column, provider-neutral name).
ALTER TABLE public.payments
    RENAME COLUMN raw_ipn_payload TO raw_webhook_payload;

-- Add PayOS-specific columns.
ALTER TABLE public.payments
    ADD COLUMN order_code bigint,
    ADD COLUMN payos_transaction_id character varying(100);

-- Unique index on order_code (PayOS's numeric order identifier).
CREATE UNIQUE INDEX uq_payments_order_code ON public.payments (order_code)
    WHERE order_code IS NOT NULL;

-- Index for webhook lookup by order_code.
CREATE INDEX idx_payments_order_code ON public.payments (order_code)
    WHERE order_code IS NOT NULL;
