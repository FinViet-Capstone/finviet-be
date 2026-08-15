-- VNPay auto-renewing subscription support.
--
-- Adds a soft-delete flag + billing cadence to subscription_plans, and the price-lock +
-- auto-renewal/dunning columns to customer_subscriptions that guarantee an admin editing
-- subscription_plans.price in place can never silently reprice an existing subscriber: every
-- renewal charge reads customer_subscriptions.locked_price, never subscription_plans.price.
-- Also adds the payments table, an audit trail of every VNPay charge attempt (initial + renewal).

CREATE TYPE public.payment_status AS ENUM ('pending', 'succeeded', 'failed', 'canceled');

ALTER TABLE public.subscription_plans
    ADD COLUMN is_active boolean NOT NULL DEFAULT true,
    ADD COLUMN billing_interval_months smallint NOT NULL DEFAULT 1;

ALTER TABLE public.subscription_plans
    ADD CONSTRAINT chk_subscription_plans_billing_interval CHECK (billing_interval_months > 0);

ALTER TABLE public.customer_subscriptions
    ADD COLUMN locked_price numeric(10,2),
    ADD COLUMN auto_renew boolean NOT NULL DEFAULT true,
    ADD COLUMN next_billing_date date,
    ADD COLUMN next_retry_at date,
    ADD COLUMN retry_count integer NOT NULL DEFAULT 0,
    ADD COLUMN renewal_claimed_at timestamp with time zone,
    ADD COLUMN vnpay_card_token character varying(100),
    ADD COLUMN canceled_at timestamp with time zone;

-- Backfill for safety if this ever runs against a database with pre-existing rows (none expected
-- on a fresh V0001-V0003 baseline, since no payment surface existed before this migration).
UPDATE public.customer_subscriptions cs
SET locked_price = sp.price
FROM public.subscription_plans sp
WHERE cs.plan_id = sp.id AND cs.locked_price IS NULL;

ALTER TABLE public.customer_subscriptions
    ALTER COLUMN locked_price SET NOT NULL;

-- subscription_id is nullable by design: the first payment for a brand-new subscriber is created
-- before any customer_subscriptions row exists (that row is only inserted once the IPN confirms
-- success). The IPN handler backfills subscription_id once the subscription row is created.
CREATE TABLE public.payments (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    subscription_id uuid REFERENCES public.customer_subscriptions(id) ON DELETE CASCADE,
    customer_id uuid NOT NULL REFERENCES public.customers(id) ON DELETE CASCADE,
    plan_id uuid NOT NULL REFERENCES public.subscription_plans(id) ON DELETE RESTRICT,
    amount numeric(10,2) NOT NULL,
    charge_type character varying(20) NOT NULL,
    status public.payment_status DEFAULT 'pending'::public.payment_status NOT NULL,
    vnp_txn_ref character varying(100) NOT NULL,
    vnp_transaction_no character varying(50),
    vnp_response_code character varying(2),
    vnp_transaction_status character varying(2),
    vnp_bank_code character varying(20),
    vnp_card_type character varying(20),
    vnp_pay_date character varying(14),
    paid_at timestamp with time zone,
    raw_ipn_payload jsonb,
    idempotency_key character varying(200),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT payments_charge_type_check CHECK (charge_type IN ('initial', 'renewal'))
);

CREATE UNIQUE INDEX uq_payments_vnp_txn_ref ON public.payments (vnp_txn_ref);

CREATE UNIQUE INDEX uq_payments_vnp_transaction_no ON public.payments (vnp_transaction_no)
    WHERE vnp_transaction_no IS NOT NULL;

CREATE INDEX idx_payments_subscription_created ON public.payments (subscription_id, created_at DESC);

-- Double-charge backstop for the renewal job: at most one in-flight payment per subscription.
CREATE UNIQUE INDEX uq_payments_one_pending_per_subscription
    ON public.payments (subscription_id)
    WHERE status = 'pending' AND subscription_id IS NOT NULL;
