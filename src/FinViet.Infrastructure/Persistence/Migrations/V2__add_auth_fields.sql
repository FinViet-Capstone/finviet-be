-- ============================================================
-- Migration V2: Add Authentication Fields & New Tables
-- Idempotent: re-runnable, skips ALTER if column already exists
-- (avoids "must be owner" error 42501 when columns are present).
-- FinViet Project
-- ============================================================

-- 1. Add new authentication columns to customer (only if missing)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='avatar_url') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN avatar_url VARCHAR(500)';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='monthly_income_expected') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN monthly_income_expected DECIMAL(15,2)';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='google_id') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN google_id VARCHAR(255)';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='is_email_verified') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN is_email_verified BOOLEAN NOT NULL DEFAULT FALSE';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='email_verified_at') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN email_verified_at TIMESTAMPTZ';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='is_active') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='customer' AND column_name='deleted_at') THEN
        EXECUTE 'ALTER TABLE customer ADD COLUMN deleted_at TIMESTAMPTZ';
    END IF;
END $$;

-- Unique index on google_id (only when not null)
CREATE UNIQUE INDEX IF NOT EXISTS idx_customer_google_id
    ON customer(google_id)
    WHERE google_id IS NOT NULL;

-- 2. Create refresh_token table
CREATE TABLE IF NOT EXISTS refresh_token
(
    token_id    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id UUID        NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    token       VARCHAR(512) UNIQUE NOT NULL,
    expires_at  TIMESTAMPTZ NOT NULL,
    is_revoked  BOOLEAN     NOT NULL DEFAULT FALSE,
    revoked_at  TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_refresh_token_token       ON refresh_token(token);
CREATE INDEX IF NOT EXISTS idx_refresh_token_customer_id ON refresh_token(customer_id);

-- 3. Create email_verification_token table
CREATE TABLE IF NOT EXISTS email_verification_token
(
    token_id    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id UUID        NOT NULL REFERENCES customer(customer_id) ON DELETE CASCADE,
    token       VARCHAR(512) UNIQUE NOT NULL,
    token_type  VARCHAR(50)  NOT NULL CHECK (token_type IN ('VERIFY_EMAIL', 'RESET_PASSWORD')),
    expires_at  TIMESTAMPTZ NOT NULL,
    used_at     TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_email_verification_token       ON email_verification_token(token);
CREATE INDEX IF NOT EXISTS idx_email_verification_customer_id ON email_verification_token(customer_id);

-- 4. Ensure unique email on customer (defense-in-depth against race condition in register)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'public' AND indexname = 'idx_customer_email_unique'
    ) THEN
        CREATE UNIQUE INDEX idx_customer_email_unique
            ON customer (LOWER(email));
    END IF;
END $$;