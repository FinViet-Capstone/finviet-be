-- Versioned income/50-30-20 allocation history (be-revamp.md item 2). Replaces the implicit
-- assumption that Customer.monthly_income/needs_pct/wants_pct/savings_pct is the only source of
-- truth: those columns remain as the onboarding-time default (used when no history row exists
-- yet for a customer), but every edit made *after* onboarding is scheduled here for the next
-- calendar month instead of overwriting the customer row in place, so past/current-month bucket
-- summaries never move retroactively. Run manually before starting the API. Idempotent.

CREATE TABLE IF NOT EXISTS income_allocation_settings (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id     uuid NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    effective_month varchar(7) NOT NULL,
    monthly_income  numeric(15, 2) NOT NULL,
    needs_pct       integer NOT NULL,
    wants_pct       integer NOT NULL,
    savings_pct     integer NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT chk_income_allocation_pct_sum CHECK (needs_pct + wants_pct + savings_pct = 100),
    CONSTRAINT uq_income_allocation_customer_month UNIQUE (customer_id, effective_month)
);

CREATE INDEX IF NOT EXISTS idx_income_allocation_customer ON income_allocation_settings(customer_id);
