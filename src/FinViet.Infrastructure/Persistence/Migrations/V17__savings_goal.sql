CREATE TABLE IF NOT EXISTS idempotency_keys (
    customer_id      uuid        NOT NULL,
    operation        text        NOT NULL,
    idempotency_key  varchar(200) NOT NULL,
    request_hash     text        NOT NULL,
    response_payload jsonb,
    completed_at     timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, operation, idempotency_key)
);

ALTER TABLE savings_goals ADD COLUMN IF NOT EXISTS icon_emoji varchar(16);