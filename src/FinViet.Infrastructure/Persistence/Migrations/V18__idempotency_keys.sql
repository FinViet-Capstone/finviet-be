-- Idempotency store for money-mutating operations (transaction create, saving-goal
-- create/contribute, wallet transfer/withdraw). IdempotencyStore.cs reads/writes this
-- table; it belongs to the base v3 schema but was missing from some provisioned
-- databases. Run manually before starting the API. Idempotent.

CREATE TABLE IF NOT EXISTS idempotency_keys (
    customer_id      uuid         NOT NULL,
    operation        text         NOT NULL,
    idempotency_key  varchar(200) NOT NULL,
    request_hash     text         NOT NULL,
    response_payload jsonb,
    completed_at     timestamptz,
    created_at       timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, operation, idempotency_key)
);
