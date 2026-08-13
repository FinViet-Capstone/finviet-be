-- Gemini safe-copilot persistence. This migration is intentionally additive and idempotent.
-- DbInitializer also executes this file from EnsureAdditiveTablesAsync because externally
-- provisioned v3 databases skip numbered migrations.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'chat_role') THEN
        CREATE TYPE chat_role AS ENUM ('user', 'assistant');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'score_view') THEN
        CREATE TYPE score_view AS ENUM ('weekly', 'monthly');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'score_color') THEN
        CREATE TYPE score_color AS ENUM ('green', 'amber', 'red');
    END IF;
END $$;

-- Repair known v3 naming drift without dropping production data.
DO $$
BEGIN
    IF to_regclass('public.ai_chat_messages') IS NULL
       AND to_regclass('public.chat_message') IS NOT NULL THEN
        ALTER TABLE chat_message RENAME TO ai_chat_messages;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ai_chat_messages (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    role        chat_role   NOT NULL,
    content     text        NOT NULL,
    session_id  uuid        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'message_id')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'id') THEN
        ALTER TABLE ai_chat_messages RENAME COLUMN message_id TO id;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'timestamp')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'created_at') THEN
        ALTER TABLE ai_chat_messages RENAME COLUMN "timestamp" TO created_at;
    END IF;
END $$;

ALTER TABLE ai_chat_messages
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS content text,
    ADD COLUMN IF NOT EXISTS session_id uuid,
    ADD COLUMN IF NOT EXISTS created_at timestamptz DEFAULT now();

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'role') THEN
        ALTER TABLE ai_chat_messages ADD COLUMN role chat_role;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'sender_type') THEN
        EXECUTE 'UPDATE ai_chat_messages SET role = CASE WHEN lower(sender_type::text) IN (''ai'', ''assistant'', ''bot'') THEN ''assistant''::chat_role ELSE ''user''::chat_role END WHERE role IS NULL';
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_chat_messages' AND column_name = 'message') THEN
        EXECUTE 'UPDATE ai_chat_messages SET content = message::text WHERE content IS NULL';
    END IF;
END $$;

UPDATE ai_chat_messages SET id = gen_random_uuid() WHERE id IS NULL;
UPDATE ai_chat_messages SET content = '' WHERE content IS NULL;
UPDATE ai_chat_messages SET role = 'user'::chat_role WHERE role IS NULL;
UPDATE ai_chat_messages SET session_id = customer_id WHERE session_id IS NULL;
UPDATE ai_chat_messages SET created_at = now() WHERE created_at IS NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_chat_messages WHERE customer_id IS NULL OR session_id IS NULL) THEN
        RAISE EXCEPTION
            'ai_chat_messages contains rows without customer_id/session_id. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_chat_messages GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_chat_messages contains duplicate ids. Back up and reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_chat_messages
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN role SET NOT NULL,
        ALTER COLUMN content SET NOT NULL,
        ALTER COLUMN session_id SET NOT NULL,
        ALTER COLUMN created_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_messages'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_chat_messages ADD CONSTRAINT ai_chat_messages_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_messages'::regclass
          AND conname = 'ai_chat_messages_customer_id_fkey') THEN
        ALTER TABLE ai_chat_messages
            ADD CONSTRAINT ai_chat_messages_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ai_chat_sessions (
    id              uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id     uuid         NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    title           varchar(120) NOT NULL DEFAULT 'Cuộc trò chuyện mới',
    history_enabled boolean      NOT NULL DEFAULT true,
    is_default      boolean      NOT NULL DEFAULT false,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now(),
    last_message_at timestamptz  NULL,
    deleted_at      timestamptz  NULL,
    CONSTRAINT uq_ai_chat_sessions_id_customer UNIQUE (id, customer_id)
);

-- Complete a partially-applied V25 without replacing an existing session table.
ALTER TABLE ai_chat_sessions
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS title varchar(120) DEFAULT 'Cuộc trò chuyện mới',
    ADD COLUMN IF NOT EXISTS history_enabled boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS is_default boolean DEFAULT false,
    ADD COLUMN IF NOT EXISTS created_at timestamptz DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz DEFAULT now(),
    ADD COLUMN IF NOT EXISTS last_message_at timestamptz,
    ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
UPDATE ai_chat_sessions SET id = gen_random_uuid() WHERE id IS NULL;
UPDATE ai_chat_sessions SET title = 'Cuộc trò chuyện mới' WHERE title IS NULL;
UPDATE ai_chat_sessions SET history_enabled = true WHERE history_enabled IS NULL;
UPDATE ai_chat_sessions SET is_default = false WHERE is_default IS NULL;
UPDATE ai_chat_sessions SET created_at = now() WHERE created_at IS NULL;
UPDATE ai_chat_sessions SET updated_at = now() WHERE updated_at IS NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_chat_sessions WHERE customer_id IS NULL) THEN
        RAISE EXCEPTION
            'ai_chat_sessions contains rows without customer_id. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_chat_sessions GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_chat_sessions contains duplicate ids. Back up and reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_chat_sessions
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN title SET NOT NULL,
        ALTER COLUMN history_enabled SET NOT NULL,
        ALTER COLUMN is_default SET NOT NULL,
        ALTER COLUMN created_at SET NOT NULL,
        ALTER COLUMN updated_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_sessions'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_chat_sessions ADD CONSTRAINT ai_chat_sessions_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_sessions'::regclass
          AND conname = 'uq_ai_chat_sessions_id_customer') THEN
        ALTER TABLE ai_chat_sessions
            ADD CONSTRAINT uq_ai_chat_sessions_id_customer UNIQUE (id, customer_id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_sessions'::regclass
          AND conname = 'ai_chat_sessions_customer_id_fkey') THEN
        ALTER TABLE ai_chat_sessions
            ADD CONSTRAINT ai_chat_sessions_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ai_chat_messages
        GROUP BY session_id
        HAVING count(DISTINCT customer_id) > 1
    ) THEN
        RAISE EXCEPTION
            'A chat session id is shared by multiple customers. Back up and rekey legacy sessions before applying V25.';
    END IF;
END $$;

INSERT INTO ai_chat_sessions (
    id, customer_id, title, history_enabled, is_default, created_at, updated_at, last_message_at)
SELECT
    m.session_id,
    m.customer_id,
    'Lịch sử trò chuyện',
    true,
    (m.session_id = m.customer_id),
    min(m.created_at),
    now(),
    max(m.created_at)
FROM ai_chat_messages AS m
WHERE m.session_id IS NOT NULL AND m.customer_id IS NOT NULL
GROUP BY m.session_id, m.customer_id
ON CONFLICT (id) DO UPDATE
SET last_message_at = GREATEST(ai_chat_sessions.last_message_at, EXCLUDED.last_message_at),
    updated_at = now()
WHERE ai_chat_sessions.customer_id = EXCLUDED.customer_id;

DO $$
BEGIN
    IF to_regclass('public.ix_ai_chat_sessions_customer_recent') IS NOT NULL
       AND pg_get_indexdef(to_regclass('public.ix_ai_chat_sessions_customer_recent'))
           <> 'CREATE INDEX ix_ai_chat_sessions_customer_recent ON public.ai_chat_sessions USING btree (customer_id, last_message_at DESC, created_at DESC) WHERE (deleted_at IS NULL)' THEN
        DROP INDEX ix_ai_chat_sessions_customer_recent;
    END IF;
    IF to_regclass('public.ux_ai_chat_sessions_customer_default') IS NOT NULL
       AND pg_get_indexdef(to_regclass('public.ux_ai_chat_sessions_customer_default'))
           <> 'CREATE UNIQUE INDEX ux_ai_chat_sessions_customer_default ON public.ai_chat_sessions USING btree (customer_id) WHERE ((is_default = true) AND (deleted_at IS NULL))' THEN
        DROP INDEX ux_ai_chat_sessions_customer_default;
    END IF;
    IF to_regclass('public.ix_ai_chat_messages_customer_session_created') IS NOT NULL
       AND pg_get_indexdef(to_regclass('public.ix_ai_chat_messages_customer_session_created'))
           <> 'CREATE INDEX ix_ai_chat_messages_customer_session_created ON public.ai_chat_messages USING btree (customer_id, session_id, created_at)' THEN
        DROP INDEX ix_ai_chat_messages_customer_session_created;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_ai_chat_sessions_customer_recent
    ON ai_chat_sessions (customer_id, last_message_at DESC, created_at DESC)
    WHERE deleted_at IS NULL;
-- Repair duplicate active defaults before enforcing the partial unique index. Keep the
-- deterministic oldest row; legacy sessions remain accessible as ordinary sessions.
WITH ranked_defaults AS (
    SELECT id,
           row_number() OVER (PARTITION BY customer_id ORDER BY created_at, id) AS rn
    FROM ai_chat_sessions
    WHERE is_default = true AND deleted_at IS NULL
)
UPDATE ai_chat_sessions AS s
SET is_default = false,
    updated_at = now()
FROM ranked_defaults AS ranked
WHERE s.id = ranked.id AND ranked.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_chat_sessions_customer_default
    ON ai_chat_sessions (customer_id)
    WHERE is_default = true AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_ai_chat_messages_customer_session_created
    ON ai_chat_messages (customer_id, session_id, created_at);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ai_chat_messages AS m
        LEFT JOIN ai_chat_sessions AS s
          ON s.id = m.session_id AND s.customer_id = m.customer_id
        WHERE s.id IS NULL
    ) THEN
        RAISE EXCEPTION
            'ai_chat_messages contains orphaned session/customer pairs. Back up and reconcile them before applying V25.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_chat_messages'::regclass
          AND conname = 'ai_chat_messages_session_customer_fkey') THEN
        ALTER TABLE ai_chat_messages
            ADD CONSTRAINT ai_chat_messages_session_customer_fkey
            FOREIGN KEY (session_id, customer_id)
            REFERENCES ai_chat_sessions(id, customer_id)
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ai_customer_preferences (
    customer_id                  uuid          PRIMARY KEY REFERENCES customers(id) ON DELETE CASCADE,
    categorization_mode          varchar(30)   NOT NULL DEFAULT 'suggest_only',
    auto_categorization_threshold numeric(5,4) NOT NULL DEFAULT 0.8500,
    default_history_enabled      boolean       NOT NULL DEFAULT true,
    weekly_report_enabled        boolean       NOT NULL DEFAULT true,
    share_balances               boolean       NOT NULL DEFAULT true,
    share_transactions           boolean       NOT NULL DEFAULT true,
    share_budgets                boolean       NOT NULL DEFAULT true,
    share_goals                  boolean       NOT NULL DEFAULT true,
    share_reports                boolean       NOT NULL DEFAULT true,
    rag_enabled                  boolean       NOT NULL DEFAULT true,
    created_at                   timestamptz   NOT NULL DEFAULT now(),
    updated_at                   timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT ck_ai_preferences_categorization_mode
        CHECK (categorization_mode IN ('off', 'suggest_only', 'high_confidence_auto')),
    CONSTRAINT ck_ai_preferences_threshold
        CHECK (auto_categorization_threshold > 0 AND auto_categorization_threshold <= 1)
);
ALTER TABLE ai_customer_preferences
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS categorization_mode varchar(30) DEFAULT 'suggest_only',
    ADD COLUMN IF NOT EXISTS auto_categorization_threshold numeric(5,4) DEFAULT 0.8500,
    ADD COLUMN IF NOT EXISTS default_history_enabled boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS weekly_report_enabled boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS share_balances boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS share_transactions boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS share_budgets boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS share_goals boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS share_reports boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS rag_enabled boolean DEFAULT true,
    ADD COLUMN IF NOT EXISTS created_at timestamptz DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz DEFAULT now();
UPDATE ai_customer_preferences SET categorization_mode = 'suggest_only' WHERE categorization_mode IS NULL;
UPDATE ai_customer_preferences SET auto_categorization_threshold = 0.8500 WHERE auto_categorization_threshold IS NULL;
UPDATE ai_customer_preferences SET default_history_enabled = true WHERE default_history_enabled IS NULL;
UPDATE ai_customer_preferences SET weekly_report_enabled = true WHERE weekly_report_enabled IS NULL;
UPDATE ai_customer_preferences SET share_balances = true WHERE share_balances IS NULL;
UPDATE ai_customer_preferences SET share_transactions = true WHERE share_transactions IS NULL;
UPDATE ai_customer_preferences SET share_budgets = true WHERE share_budgets IS NULL;
UPDATE ai_customer_preferences SET share_goals = true WHERE share_goals IS NULL;
UPDATE ai_customer_preferences SET share_reports = true WHERE share_reports IS NULL;
UPDATE ai_customer_preferences SET rag_enabled = true WHERE rag_enabled IS NULL;
UPDATE ai_customer_preferences SET created_at = now() WHERE created_at IS NULL;
UPDATE ai_customer_preferences SET updated_at = now() WHERE updated_at IS NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_customer_preferences WHERE customer_id IS NULL) THEN
        RAISE EXCEPTION
            'ai_customer_preferences contains rows without customer_id. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_customer_preferences GROUP BY customer_id HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_customer_preferences contains duplicate customer rows. Reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_customer_preferences
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN categorization_mode SET NOT NULL,
        ALTER COLUMN auto_categorization_threshold SET NOT NULL,
        ALTER COLUMN default_history_enabled SET NOT NULL,
        ALTER COLUMN weekly_report_enabled SET NOT NULL,
        ALTER COLUMN share_balances SET NOT NULL,
        ALTER COLUMN share_transactions SET NOT NULL,
        ALTER COLUMN share_budgets SET NOT NULL,
        ALTER COLUMN share_goals SET NOT NULL,
        ALTER COLUMN share_reports SET NOT NULL,
        ALTER COLUMN rag_enabled SET NOT NULL,
        ALTER COLUMN created_at SET NOT NULL,
        ALTER COLUMN updated_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_customer_preferences'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_customer_preferences
            ADD CONSTRAINT ai_customer_preferences_pkey PRIMARY KEY (customer_id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_customer_preferences'::regclass
          AND conname = 'ai_customer_preferences_customer_id_fkey') THEN
        ALTER TABLE ai_customer_preferences
            ADD CONSTRAINT ai_customer_preferences_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_customer_preferences'::regclass
          AND conname = 'ck_ai_preferences_categorization_mode') THEN
        ALTER TABLE ai_customer_preferences ADD CONSTRAINT ck_ai_preferences_categorization_mode
            CHECK (categorization_mode IN ('off', 'suggest_only', 'high_confidence_auto'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_customer_preferences'::regclass
          AND conname = 'ck_ai_preferences_threshold') THEN
        ALTER TABLE ai_customer_preferences ADD CONSTRAINT ck_ai_preferences_threshold
            CHECK (auto_categorization_threshold > 0 AND auto_categorization_threshold <= 1);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS ai_rate_limit_windows (
    customer_id  uuid         NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    feature      varchar(40)  NOT NULL,
    window_type  varchar(10)  NOT NULL,
    window_start timestamptz  NOT NULL,
    request_count integer     NOT NULL DEFAULT 0,
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, feature, window_type, window_start),
    CONSTRAINT ck_ai_rate_limit_window_type CHECK (window_type IN ('minute', 'day')),
    CONSTRAINT ck_ai_rate_limit_request_count CHECK (request_count >= 0)
);
ALTER TABLE ai_rate_limit_windows
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS feature varchar(40),
    ADD COLUMN IF NOT EXISTS window_type varchar(10),
    ADD COLUMN IF NOT EXISTS window_start timestamptz,
    ADD COLUMN IF NOT EXISTS request_count integer DEFAULT 0,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz DEFAULT now();
UPDATE ai_rate_limit_windows SET request_count = 0 WHERE request_count IS NULL;
UPDATE ai_rate_limit_windows SET updated_at = now() WHERE updated_at IS NULL;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM ai_rate_limit_windows
        WHERE customer_id IS NULL OR feature IS NULL OR window_type IS NULL OR window_start IS NULL) THEN
        RAISE EXCEPTION
            'ai_rate_limit_windows contains incomplete keys. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ai_rate_limit_windows
        GROUP BY customer_id, feature, window_type, window_start
        HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_rate_limit_windows contains duplicate windows. Reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_rate_limit_windows
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN feature SET NOT NULL,
        ALTER COLUMN window_type SET NOT NULL,
        ALTER COLUMN window_start SET NOT NULL,
        ALTER COLUMN request_count SET NOT NULL,
        ALTER COLUMN updated_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_rate_limit_windows'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_rate_limit_windows ADD CONSTRAINT ai_rate_limit_windows_pkey
            PRIMARY KEY (customer_id, feature, window_type, window_start);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_rate_limit_windows'::regclass
          AND conname = 'ai_rate_limit_windows_customer_id_fkey') THEN
        ALTER TABLE ai_rate_limit_windows ADD CONSTRAINT ai_rate_limit_windows_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_rate_limit_windows'::regclass
          AND conname = 'ck_ai_rate_limit_window_type') THEN
        ALTER TABLE ai_rate_limit_windows ADD CONSTRAINT ck_ai_rate_limit_window_type
            CHECK (window_type IN ('minute', 'day'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_rate_limit_windows'::regclass
          AND conname = 'ck_ai_rate_limit_request_count') THEN
        ALTER TABLE ai_rate_limit_windows ADD CONSTRAINT ck_ai_rate_limit_request_count
            CHECK (request_count >= 0);
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_ai_rate_limit_windows_start ON ai_rate_limit_windows (window_start);

CREATE TABLE IF NOT EXISTS ai_usage_events (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id         uuid        NULL REFERENCES customers(id) ON DELETE SET NULL,
    session_id          uuid        NULL,
    feature             varchar(40) NOT NULL,
    provider            varchar(40) NOT NULL,
    model               varchar(120) NULL,
    outcome             varchar(30) NOT NULL,
    input_tokens        integer     NULL,
    output_tokens       integer     NULL,
    total_tokens        integer     NULL,
    latency_ms          integer     NULL,
    provider_request_id varchar(255) NULL,
    metadata            jsonb       NOT NULL DEFAULT '{}'::jsonb,
    occurred_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_ai_usage_outcome CHECK (outcome IN ('success', 'blocked', 'rate_limited', 'fallback', 'error')),
    CONSTRAINT ck_ai_usage_token_counts CHECK (
        (input_tokens IS NULL OR input_tokens >= 0) AND
        (output_tokens IS NULL OR output_tokens >= 0) AND
        (total_tokens IS NULL OR total_tokens >= 0) AND
        (latency_ms IS NULL OR latency_ms >= 0))
);
ALTER TABLE ai_usage_events
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS session_id uuid,
    ADD COLUMN IF NOT EXISTS feature varchar(40),
    ADD COLUMN IF NOT EXISTS provider varchar(40),
    ADD COLUMN IF NOT EXISTS model varchar(120),
    ADD COLUMN IF NOT EXISTS outcome varchar(30),
    ADD COLUMN IF NOT EXISTS input_tokens integer,
    ADD COLUMN IF NOT EXISTS output_tokens integer,
    ADD COLUMN IF NOT EXISTS total_tokens integer,
    ADD COLUMN IF NOT EXISTS latency_ms integer,
    ADD COLUMN IF NOT EXISTS provider_request_id varchar(255),
    ADD COLUMN IF NOT EXISTS metadata jsonb DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS occurred_at timestamptz DEFAULT now();
UPDATE ai_usage_events SET id = gen_random_uuid() WHERE id IS NULL;
UPDATE ai_usage_events SET metadata = '{}'::jsonb WHERE metadata IS NULL;
UPDATE ai_usage_events SET occurred_at = now() WHERE occurred_at IS NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_usage_events WHERE feature IS NULL OR provider IS NULL OR outcome IS NULL) THEN
        RAISE EXCEPTION 'ai_usage_events contains incomplete operational rows. Reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_usage_events GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'ai_usage_events contains duplicate ids. Reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_usage_events
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN feature SET NOT NULL,
        ALTER COLUMN provider SET NOT NULL,
        ALTER COLUMN outcome SET NOT NULL,
        ALTER COLUMN metadata SET NOT NULL,
        ALTER COLUMN occurred_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_usage_events'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_usage_events ADD CONSTRAINT ai_usage_events_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_usage_events'::regclass
          AND conname = 'ai_usage_events_customer_id_fkey') THEN
        ALTER TABLE ai_usage_events ADD CONSTRAINT ai_usage_events_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_usage_events'::regclass
          AND conname = 'ck_ai_usage_outcome') THEN
        ALTER TABLE ai_usage_events ADD CONSTRAINT ck_ai_usage_outcome
            CHECK (outcome IN ('success', 'blocked', 'rate_limited', 'fallback', 'error'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_usage_events'::regclass
          AND conname = 'ck_ai_usage_token_counts') THEN
        ALTER TABLE ai_usage_events ADD CONSTRAINT ck_ai_usage_token_counts CHECK (
            (input_tokens IS NULL OR input_tokens >= 0) AND
            (output_tokens IS NULL OR output_tokens >= 0) AND
            (total_tokens IS NULL OR total_tokens >= 0) AND
            (latency_ms IS NULL OR latency_ms >= 0));
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_ai_usage_events_customer_time ON ai_usage_events (customer_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_ai_usage_events_feature_time ON ai_usage_events (feature, occurred_at DESC);

CREATE TABLE IF NOT EXISTS ai_audit_events (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id    uuid        NULL REFERENCES customers(id) ON DELETE SET NULL,
    session_id     uuid        NULL,
    actor_type     varchar(20) NOT NULL,
    event_type     varchar(80) NOT NULL,
    correlation_id uuid        NULL,
    metadata       jsonb       NOT NULL DEFAULT '{}'::jsonb,
    occurred_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_ai_audit_actor_type CHECK (actor_type IN ('customer', 'system', 'admin'))
);
ALTER TABLE ai_audit_events
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS session_id uuid,
    ADD COLUMN IF NOT EXISTS actor_type varchar(20),
    ADD COLUMN IF NOT EXISTS event_type varchar(80),
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS metadata jsonb DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS occurred_at timestamptz DEFAULT now();
UPDATE ai_audit_events SET id = gen_random_uuid() WHERE id IS NULL;
UPDATE ai_audit_events SET metadata = '{}'::jsonb WHERE metadata IS NULL;
UPDATE ai_audit_events SET occurred_at = now() WHERE occurred_at IS NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_audit_events WHERE actor_type IS NULL OR event_type IS NULL) THEN
        RAISE EXCEPTION 'ai_audit_events contains incomplete operational rows. Reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_audit_events GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'ai_audit_events contains duplicate ids. Reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_audit_events
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN actor_type SET NOT NULL,
        ALTER COLUMN event_type SET NOT NULL,
        ALTER COLUMN metadata SET NOT NULL,
        ALTER COLUMN occurred_at SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_audit_events'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_audit_events ADD CONSTRAINT ai_audit_events_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_audit_events'::regclass
          AND conname = 'ai_audit_events_customer_id_fkey') THEN
        ALTER TABLE ai_audit_events ADD CONSTRAINT ai_audit_events_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_audit_events'::regclass
          AND conname = 'ck_ai_audit_actor_type') THEN
        ALTER TABLE ai_audit_events ADD CONSTRAINT ck_ai_audit_actor_type
            CHECK (actor_type IN ('customer', 'system', 'admin'));
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_ai_audit_events_customer_time ON ai_audit_events (customer_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_ai_audit_events_correlation ON ai_audit_events (correlation_id) WHERE correlation_id IS NOT NULL;

-- Persist categorization provenance that the entity previously kept only in memory.
ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS is_ai_classified boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS ai_confidence numeric(5,4) NULL,
    ADD COLUMN IF NOT EXISTS ai_category_guess varchar(40) NULL,
    ADD COLUMN IF NOT EXISTS ai_classification_source varchar(30) NULL,
    ADD COLUMN IF NOT EXISTS ai_classified_at timestamptz NULL;
CREATE INDEX IF NOT EXISTS ix_transactions_customer_ai_pending
    ON transactions (customer_id, transaction_date DESC)
    WHERE is_ai_classified = false;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_transactions_ai_confidence') THEN
        ALTER TABLE transactions ADD CONSTRAINT ck_transactions_ai_confidence
            CHECK (ai_confidence IS NULL OR (ai_confidence >= 0 AND ai_confidence <= 1));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_transactions_ai_source') THEN
        ALTER TABLE transactions ADD CONSTRAINT ck_transactions_ai_source
            CHECK (ai_classification_source IS NULL OR ai_classification_source IN
                ('manual', 'merchant_rule', 'ai_auto', 'ai_suggestion', 'fallback'));
    END IF;
END $$;

-- Additive repairs for v3 AI report/score tables. CREATE TABLE handles absent tables;
-- ALTER TABLE fills columns missing from older provisioned variants.
CREATE TABLE IF NOT EXISTS ai_weekly_reports (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id    uuid        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    report_text_vi text        NOT NULL,
    week_start     date        NOT NULL,
    is_read        boolean     NOT NULL DEFAULT false,
    generated_at   timestamptz NOT NULL DEFAULT now()
);
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_weekly_reports' AND column_name = 'report_id')
       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_weekly_reports' AND column_name = 'id') THEN
        ALTER TABLE ai_weekly_reports RENAME COLUMN report_id TO id;
    END IF;
END $$;
ALTER TABLE ai_weekly_reports
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS report_text_vi text,
    ADD COLUMN IF NOT EXISTS week_start date,
    ADD COLUMN IF NOT EXISTS is_read boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS generated_at timestamptz NOT NULL DEFAULT now();
UPDATE ai_weekly_reports SET id = gen_random_uuid() WHERE id IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM ai_weekly_reports
        WHERE customer_id IS NULL OR report_text_vi IS NULL OR week_start IS NULL) THEN
        RAISE EXCEPTION
            'ai_weekly_reports contains incomplete rows. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_weekly_reports GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_weekly_reports contains duplicate ids. Back up and reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_weekly_reports
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN report_text_vi SET NOT NULL,
        ALTER COLUMN week_start SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_weekly_reports'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_weekly_reports ADD CONSTRAINT ai_weekly_reports_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_weekly_reports'::regclass
          AND conname = 'ai_weekly_reports_customer_id_fkey') THEN
        ALTER TABLE ai_weekly_reports
            ADD CONSTRAINT ai_weekly_reports_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
END $$;

-- Do not silently discard financial reports during startup. Duplicate periods require an
-- explicit operator reconciliation so the deployment fails closed and preserves every row.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ai_weekly_reports
        WHERE customer_id IS NOT NULL AND week_start IS NOT NULL
        GROUP BY customer_id, week_start
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Duplicate ai_weekly_reports periods detected. Back up and reconcile them before applying V25.';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.ux_ai_weekly_reports_customer_period') IS NOT NULL
       AND pg_get_indexdef(to_regclass('public.ux_ai_weekly_reports_customer_period'))
           <> 'CREATE UNIQUE INDEX ux_ai_weekly_reports_customer_period ON public.ai_weekly_reports USING btree (customer_id, week_start)' THEN
        DROP INDEX ux_ai_weekly_reports_customer_period;
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_weekly_reports_customer_period
    ON ai_weekly_reports (customer_id, week_start);

CREATE TABLE IF NOT EXISTS ai_spending_scores (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id    uuid        NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    view           score_view  NOT NULL,
    score          integer     NOT NULL,
    spike_score    integer     NULL,
    budget_score   integer     NULL,
    savings_score  integer     NULL,
    color          score_color NOT NULL,
    verdict_vi     varchar(120) NULL,
    reason_vi      varchar(255) NULL,
    commentary_vi  text        NULL,
    period_start   date        NOT NULL,
    generated_at   timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE ai_spending_scores
    ADD COLUMN IF NOT EXISTS id uuid DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS view score_view,
    ADD COLUMN IF NOT EXISTS score integer,
    ADD COLUMN IF NOT EXISTS spike_score integer,
    ADD COLUMN IF NOT EXISTS budget_score integer,
    ADD COLUMN IF NOT EXISTS savings_score integer,
    ADD COLUMN IF NOT EXISTS color score_color,
    ADD COLUMN IF NOT EXISTS verdict_vi varchar(120),
    ADD COLUMN IF NOT EXISTS reason_vi varchar(255),
    ADD COLUMN IF NOT EXISTS commentary_vi text,
    ADD COLUMN IF NOT EXISTS period_start date,
    ADD COLUMN IF NOT EXISTS generated_at timestamptz NOT NULL DEFAULT now();
UPDATE ai_spending_scores SET id = gen_random_uuid() WHERE id IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM ai_spending_scores
        WHERE customer_id IS NULL OR view IS NULL OR score IS NULL
           OR color IS NULL OR period_start IS NULL) THEN
        RAISE EXCEPTION
            'ai_spending_scores contains incomplete rows. Back up and reconcile them before applying V25.';
    END IF;
    IF EXISTS (SELECT 1 FROM ai_spending_scores GROUP BY id HAVING count(*) > 1) THEN
        RAISE EXCEPTION
            'ai_spending_scores contains duplicate ids. Back up and reconcile them before applying V25.';
    END IF;

    ALTER TABLE ai_spending_scores
        ALTER COLUMN id SET NOT NULL,
        ALTER COLUMN customer_id SET NOT NULL,
        ALTER COLUMN view SET NOT NULL,
        ALTER COLUMN score SET NOT NULL,
        ALTER COLUMN color SET NOT NULL,
        ALTER COLUMN period_start SET NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_spending_scores'::regclass AND contype = 'p') THEN
        ALTER TABLE ai_spending_scores ADD CONSTRAINT ai_spending_scores_pkey PRIMARY KEY (id);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ai_spending_scores'::regclass
          AND conname = 'ai_spending_scores_customer_id_fkey') THEN
        ALTER TABLE ai_spending_scores
            ADD CONSTRAINT ai_spending_scores_customer_id_fkey
            FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE;
    END IF;
END $$;

-- Preserve every score snapshot. Duplicate periods require explicit operator reconciliation
-- rather than irreversible deletion during application startup.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ai_spending_scores
        WHERE customer_id IS NOT NULL AND view IS NOT NULL AND period_start IS NOT NULL
        GROUP BY customer_id, view, period_start
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Duplicate ai_spending_scores periods detected. Back up and reconcile them before applying V25.';
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.ux_ai_spending_scores_customer_period') IS NOT NULL
       AND pg_get_indexdef(to_regclass('public.ux_ai_spending_scores_customer_period'))
           <> 'CREATE UNIQUE INDEX ux_ai_spending_scores_customer_period ON public.ai_spending_scores USING btree (customer_id, view, period_start)' THEN
        DROP INDEX ux_ai_spending_scores_customer_period;
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_spending_scores_customer_period
    ON ai_spending_scores (customer_id, view, period_start);
