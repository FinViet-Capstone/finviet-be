-- ============================================================
-- Migration V8: AI feature suite
-- - Adds AI classification fields to transaction.
-- - Adds closed-period score snapshots (ai_spending_scores).
-- - Adds weekly narrative reports (ai_weekly_reports).
-- - Adds fallback re-process queue (ai_classification_queue).
-- - Adds AI usage log for rate limiting (ai_usage_log).
-- - Adds beneficiary_rule (retroactive rules + recurring exclusion).
-- - Adds user_category_buckets (Needs/Wants drag-drop override).
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

-- 1. transaction: AI classification fields.
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS beneficiary_name  text;
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS is_ai_classified  boolean NOT NULL DEFAULT false;
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS ai_confidence     numeric(5,4);
ALTER TABLE transaction ADD COLUMN IF NOT EXISTS ai_category_guess uuid;

ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_ai_category_guess_fkey;
ALTER TABLE transaction
    ADD CONSTRAINT transaction_ai_category_guess_fkey
    FOREIGN KEY (ai_category_guess) REFERENCES category (category_id) ON DELETE SET NULL;

-- 2. ai_spending_scores: closed-period score snapshot.
CREATE TABLE IF NOT EXISTS ai_spending_scores (
    score_id      uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id   uuid         NOT NULL,
    period_type   varchar(10)  NOT NULL,
    period_start  date         NOT NULL,
    period_end    date         NOT NULL,
    final_score   numeric(5,2) NOT NULL,
    spike_score   numeric(5,2),
    budget_score  numeric(5,2),
    savings_score numeric(5,2),
    weights_json  jsonb,
    color_badge   varchar(20),
    comment       text,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ai_spending_scores_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_spending_scores_customer_period
    ON ai_spending_scores (customer_id, period_type, period_start);

-- 3. ai_weekly_reports: Vietnamese narrative report.
CREATE TABLE IF NOT EXISTS ai_weekly_reports (
    report_id    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id  uuid        NOT NULL,
    score_id     uuid,
    period_start date        NOT NULL,
    period_end   date        NOT NULL,
    narrative    text        NOT NULL,
    generated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ai_weekly_reports_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE,
    CONSTRAINT ai_weekly_reports_score_id_fkey
        FOREIGN KEY (score_id) REFERENCES ai_spending_scores (score_id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_weekly_reports_customer_period
    ON ai_weekly_reports (customer_id, period_start);

-- 4. ai_classification_queue: durable fallback re-process queue.
CREATE TABLE IF NOT EXISTS ai_classification_queue (
    queue_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    transaction_id  uuid        NOT NULL,
    customer_id     uuid        NOT NULL,
    raw_input       text        NOT NULL,
    status          varchar(20) NOT NULL DEFAULT 'PENDING',
    attempt_count   int         NOT NULL DEFAULT 0,
    last_error      text,
    enqueued_at     timestamptz NOT NULL DEFAULT now(),
    processed_at    timestamptz,
    next_attempt_at timestamptz,
    CONSTRAINT ai_classification_queue_transaction_id_fkey
        FOREIGN KEY (transaction_id) REFERENCES transaction (transaction_id) ON DELETE CASCADE,
    CONSTRAINT ai_classification_queue_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_ai_classification_queue_status_next
    ON ai_classification_queue (status, next_attempt_at);

-- 5. ai_usage_log: per-user AI call accounting for rate limiting.
CREATE TABLE IF NOT EXISTS ai_usage_log (
    usage_id    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid        NOT NULL,
    feature     varchar(30) NOT NULL,
    called_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ai_usage_log_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_ai_usage_log_customer_called
    ON ai_usage_log (customer_id, called_at);

-- 6. beneficiary_rule: retroactive category rules + recurring (fixed-bill) exclusion.
CREATE TABLE IF NOT EXISTS beneficiary_rule (
    rule_id      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id  uuid        NOT NULL,
    match_text   text        NOT NULL,
    category_id  uuid        NOT NULL,
    is_recurring boolean     NOT NULL DEFAULT false,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT beneficiary_rule_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE,
    CONSTRAINT beneficiary_rule_category_id_fkey
        FOREIGN KEY (category_id) REFERENCES category (category_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_beneficiary_rule_customer_match
    ON beneficiary_rule (customer_id, match_text);

-- 7. user_category_buckets: per-user Needs/Wants override for a category.
CREATE TABLE IF NOT EXISTS user_category_buckets (
    customer_id uuid        NOT NULL,
    category_id uuid        NOT NULL,
    bucket      varchar(10) NOT NULL,
    CONSTRAINT user_category_buckets_pkey PRIMARY KEY (customer_id, category_id),
    CONSTRAINT user_category_buckets_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE,
    CONSTRAINT user_category_buckets_category_id_fkey
        FOREIGN KEY (category_id) REFERENCES category (category_id) ON DELETE CASCADE,
    CONSTRAINT user_category_buckets_bucket_check
        CHECK (bucket IN ('NEEDS', 'WANTS'))
);
