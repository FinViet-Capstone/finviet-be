-- finviet-be#138: paid subscriptions never expired. payOS charges are one-off QR payments, so
-- each payment buys one billing interval; end_date is now the last covered day (inclusive) and
-- next_billing_date the first uncovered day (end_date + 1).

-- Give every existing paid-through subscription its end date, so the lifecycle job can expire
-- the ones whose paid period is already over.
UPDATE public.customer_subscriptions
SET end_date = next_billing_date - 1,
    updated_at = now()
WHERE end_date IS NULL
  AND next_billing_date IS NOT NULL;

-- The lifecycle job scans active rows by end date.
CREATE INDEX idx_customer_subscriptions_active_end_date
    ON public.customer_subscriptions (end_date)
    WHERE status = 'active'::public.subscription_status;

-- One row per resubscribe reminder sent; the primary key makes each reminder at-most-once per
-- paid period (a renewal moves end_date, re-arming the next period's reminders).
CREATE TABLE public.subscription_reminders (
    subscription_id uuid NOT NULL,
    kind character varying(20) NOT NULL,
    period_end_date date NOT NULL,
    sent_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT subscription_reminders_pkey PRIMARY KEY (subscription_id, kind, period_end_date),
    CONSTRAINT subscription_reminders_subscription_id_fkey FOREIGN KEY (subscription_id)
        REFERENCES public.customer_subscriptions(id) ON DELETE CASCADE,
    CONSTRAINT subscription_reminders_kind_check CHECK (kind IN ('ending_soon', 'end_day', 'expired'))
);
