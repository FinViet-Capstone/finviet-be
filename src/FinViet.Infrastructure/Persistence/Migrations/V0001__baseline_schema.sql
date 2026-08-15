-- FinViet PostgreSQL baseline schema.
-- Captured exactly from the stable local PostgreSQL 18.1 database on 2026-08-13.
-- Only pg_dump's psql-only directives, PostgreSQL-version-specific session settings,
-- extension comments, ownership, and ACLs were removed. Schema objects remain unchanged.
-- This migration is immutable after release. Requires pgcrypto and pgvector support.

--
-- PostgreSQL database dump
--


-- Dumped from database version 18.1
-- Dumped by pg_dump version 18.1

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: vector; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;


--
-- Name: app_language; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.app_language AS ENUM (
    'vi',
    'en'
);


--
-- Name: app_theme; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.app_theme AS ENUM (
    'light',
    'dark',
    'system'
);


--
-- Name: category_source; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.category_source AS ENUM (
    'persona',
    'system'
);


--
-- Name: category_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.category_type AS ENUM (
    'income',
    'expense'
);


--
-- Name: chat_role; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.chat_role AS ENUM (
    'user',
    'assistant'
);


--
-- Name: email_token_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.email_token_type AS ENUM (
    'verify_email',
    'reset_password'
);


--
-- Name: entry_method; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.entry_method AS ENUM (
    'manual',
    'photo',
    'sms_paste',
    'csv_import',
    'sepay_sync',
    'finverse_sync'
);


--
-- Name: gender; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.gender AS ENUM (
    'male',
    'female',
    'other'
);


--
-- Name: notification_entity_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.notification_entity_type AS ENUM (
    'budget',
    'goal',
    'report',
    'wallet',
    'system'
);


--
-- Name: notification_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.notification_type AS ENUM (
    'budget_alert',
    'weekly_report',
    'goal_milestone',
    'announcement',
    'sepay_sync_error'
);


--
-- Name: score_color; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.score_color AS ENUM (
    'green',
    'amber',
    'red'
);


--
-- Name: score_view; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.score_view AS ENUM (
    'weekly',
    'monthly'
);


--
-- Name: subscription_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.subscription_status AS ENUM (
    'active',
    'canceled',
    'expired',
    'past_due'
);


--
-- Name: transaction_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.transaction_type AS ENUM (
    'expense',
    'income',
    'transfer_out',
    'transfer_in'
);


--
-- Name: wallet_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.wallet_type AS ENUM (
    'basic',
    'sepay_linked',
    'finverse_linked'
);


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: admins; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admins (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    username character varying(50) NOT NULL,
    password_hash character varying(255) NOT NULL,
    email character varying(255) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ai_audit_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_audit_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid,
    session_id uuid,
    actor_type character varying(20) NOT NULL,
    event_type character varying(80) NOT NULL,
    correlation_id uuid,
    metadata jsonb DEFAULT '{}'::jsonb NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_ai_audit_actor_type CHECK (((actor_type)::text = ANY (ARRAY[('customer'::character varying)::text, ('system'::character varying)::text, ('admin'::character varying)::text])))
);


--
-- Name: ai_chat_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_chat_messages (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    role public.chat_role NOT NULL,
    content text NOT NULL,
    session_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: ai_chat_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_chat_sessions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    title character varying(120) DEFAULT 'Cuộc trò chuyện mới'::character varying NOT NULL,
    history_enabled boolean DEFAULT true NOT NULL,
    is_default boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    last_message_at timestamp with time zone,
    deleted_at timestamp with time zone
);


--
-- Name: ai_customer_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_customer_preferences (
    customer_id uuid NOT NULL,
    categorization_mode character varying(30) DEFAULT 'suggest_only'::character varying NOT NULL,
    auto_categorization_threshold numeric(5,4) DEFAULT 0.8500 NOT NULL,
    default_history_enabled boolean DEFAULT true NOT NULL,
    weekly_report_enabled boolean DEFAULT true NOT NULL,
    share_balances boolean DEFAULT true NOT NULL,
    share_transactions boolean DEFAULT true NOT NULL,
    share_budgets boolean DEFAULT true NOT NULL,
    share_goals boolean DEFAULT true NOT NULL,
    share_reports boolean DEFAULT true NOT NULL,
    rag_enabled boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_ai_preferences_categorization_mode CHECK (((categorization_mode)::text = ANY (ARRAY[('off'::character varying)::text, ('suggest_only'::character varying)::text, ('high_confidence_auto'::character varying)::text]))),
    CONSTRAINT ck_ai_preferences_threshold CHECK (((auto_categorization_threshold > (0)::numeric) AND (auto_categorization_threshold <= (1)::numeric)))
);


--
-- Name: ai_rate_limit_windows; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_rate_limit_windows (
    customer_id uuid NOT NULL,
    feature character varying(40) NOT NULL,
    window_type character varying(10) NOT NULL,
    window_start timestamp with time zone NOT NULL,
    request_count integer DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_ai_rate_limit_request_count CHECK ((request_count >= 0)),
    CONSTRAINT ck_ai_rate_limit_window_type CHECK (((window_type)::text = ANY (ARRAY[('minute'::character varying)::text, ('day'::character varying)::text])))
);


--
-- Name: ai_spending_scores; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_spending_scores (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    view public.score_view NOT NULL,
    score integer NOT NULL,
    spike_score integer,
    budget_score integer,
    savings_score integer,
    color public.score_color NOT NULL,
    verdict_vi character varying(120),
    reason_vi character varying(255),
    commentary_vi text,
    period_start date NOT NULL,
    generated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ai_spending_scores_score_check CHECK (((score >= 0) AND (score <= 100)))
);


--
-- Name: ai_usage_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_usage_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid,
    session_id uuid,
    feature character varying(40) NOT NULL,
    provider character varying(40) NOT NULL,
    model character varying(120),
    outcome character varying(30) NOT NULL,
    input_tokens integer,
    output_tokens integer,
    total_tokens integer,
    latency_ms integer,
    provider_request_id character varying(255),
    metadata jsonb DEFAULT '{}'::jsonb NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_ai_usage_outcome CHECK (((outcome)::text = ANY (ARRAY[('success'::character varying)::text, ('blocked'::character varying)::text, ('rate_limited'::character varying)::text, ('fallback'::character varying)::text, ('error'::character varying)::text]))),
    CONSTRAINT ck_ai_usage_token_counts CHECK ((((input_tokens IS NULL) OR (input_tokens >= 0)) AND ((output_tokens IS NULL) OR (output_tokens >= 0)) AND ((total_tokens IS NULL) OR (total_tokens >= 0)) AND ((latency_ms IS NULL) OR (latency_ms >= 0))))
);


--
-- Name: ai_weekly_reports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_weekly_reports (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    report_text_vi text NOT NULL,
    week_start date NOT NULL,
    generated_at timestamp with time zone DEFAULT now() NOT NULL,
    is_read boolean DEFAULT false NOT NULL
);


--
-- Name: buckets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.buckets (
    id character varying(20) NOT NULL,
    name_vi character varying(40) NOT NULL,
    name_en character varying(40) NOT NULL,
    color character varying(7),
    icon character varying(60),
    sort_order integer,
    is_locked boolean DEFAULT false NOT NULL
);


--
-- Name: budgets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.budgets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    category_id character varying(40) NOT NULL,
    wallet_id uuid,
    monthly_limit numeric(15,2) NOT NULL,
    last_alert_threshold numeric(5,2) DEFAULT 0 NOT NULL,
    last_alert_month character varying(7),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categories (
    id character varying(40) NOT NULL,
    name_vi character varying(80) NOT NULL,
    name_en character varying(80) NOT NULL,
    type public.category_type NOT NULL,
    icon character varying(60),
    color character varying(7),
    default_bucket character varying(20),
    sort_order integer
);


--
-- Name: category_correction_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.category_correction_log (
    log_id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid,
    transaction_id uuid,
    admin_id uuid,
    corrected_category_id character varying(40),
    original_ai_guess character varying(40),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: customer_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_categories (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    category_id character varying(40) NOT NULL,
    bucket_id character varying(20) NOT NULL,
    source public.category_source DEFAULT 'persona'::public.category_source NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: customer_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_settings (
    customer_id uuid NOT NULL,
    default_currency character varying(3) DEFAULT 'VND'::character varying NOT NULL,
    language public.app_language DEFAULT 'vi'::public.app_language NOT NULL,
    theme public.app_theme DEFAULT 'system'::public.app_theme NOT NULL,
    notif_budget boolean DEFAULT true NOT NULL,
    notif_report boolean DEFAULT true NOT NULL,
    notif_goals boolean DEFAULT true NOT NULL,
    notif_budget_thresholds integer[] DEFAULT '{80,100}'::integer[] NOT NULL,
    fcm_token character varying(255),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: customer_subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_subscriptions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    status public.subscription_status DEFAULT 'active'::public.subscription_status NOT NULL,
    start_date date NOT NULL,
    end_date date,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255),
    google_id character varying(255),
    display_name character varying(120) NOT NULL,
    avatar_url character varying(512),
    gender public.gender,
    date_of_birth date,
    monthly_income numeric(15,2),
    needs_pct integer DEFAULT 50 NOT NULL,
    wants_pct integer DEFAULT 30 NOT NULL,
    savings_pct integer DEFAULT 20 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    email_verified boolean DEFAULT false NOT NULL,
    email_verified_at timestamp with time zone,
    onboarding_done boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_buckets_sum CHECK ((((needs_pct + wants_pct) + savings_pct) = 100))
);


--
-- Name: email_verification_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.email_verification_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    token character varying(512) NOT NULL,
    token_type public.email_token_type NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: idempotency_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.idempotency_keys (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    operation character varying(80) NOT NULL,
    idempotency_key character varying(200) NOT NULL,
    request_hash character varying(64) NOT NULL,
    response_payload jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    completed_at timestamp with time zone
);


--
-- Name: merchant_rules; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_rules (
    rule_id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    merchant_keyword character varying(255) NOT NULL,
    category_id character varying(40) NOT NULL,
    applied_count integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    type public.notification_type NOT NULL,
    title character varying(255) NOT NULL,
    body text,
    entity_type public.notification_entity_type,
    entity_id uuid,
    is_read boolean DEFAULT false NOT NULL,
    sent_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: rag_chunk; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.rag_chunk (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    document_id uuid NOT NULL,
    customer_id uuid,
    content text NOT NULL,
    embedding public.vector(768) NOT NULL,
    metadata jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: rag_document; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.rag_document (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid,
    source_type character varying(20) NOT NULL,
    title character varying(255) NOT NULL,
    uri character varying(512),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refresh_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    token character varying(512) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    is_revoked boolean DEFAULT false NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: savings_goal_contributions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.savings_goal_contributions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    goal_id uuid NOT NULL,
    transaction_id uuid,
    amount numeric(15,2) NOT NULL,
    contributed_at timestamp with time zone DEFAULT now() NOT NULL,
    note character varying(255)
);


--
-- Name: savings_goals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.savings_goals (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    name character varying(120) NOT NULL,
    icon_emoji character varying(16),
    target_amount numeric(15,2) NOT NULL,
    current_amount numeric(15,2) DEFAULT 0 NOT NULL,
    deadline date NOT NULL,
    funding_wallet_id uuid,
    is_completed boolean DEFAULT false NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: scoring_criteria; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scoring_criteria (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(30) NOT NULL,
    name_vi character varying(100) NOT NULL,
    weight_weekly numeric(5,2) NOT NULL,
    weight_monthly numeric(5,2) NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: sepay_links; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sepay_links (
    wallet_id uuid NOT NULL,
    auth_mode text DEFAULT 'oauth'::text NOT NULL,
    sepay_user_id text,
    sepay_bank_account_id integer DEFAULT 0 NOT NULL,
    account_number text,
    account_holder_name text,
    bank_short_name text,
    access_token text,
    refresh_token text,
    last_synced_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    access_token_expires_at timestamp with time zone,
    sepay_webhook_id integer
);


--
-- Name: subscription_plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_plans (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(100) NOT NULL,
    price numeric(10,2) DEFAULT 0 NOT NULL,
    features_json jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: system_analytics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.system_analytics (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    admin_id uuid,
    metric_name character varying(100) NOT NULL,
    metric_value numeric(15,2) NOT NULL,
    recorded_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    wallet_id uuid NOT NULL,
    category_id character varying(40),
    amount numeric(15,2) NOT NULL,
    type public.transaction_type NOT NULL,
    description character varying(255),
    merchant character varying(255),
    transaction_date timestamp with time zone DEFAULT now() NOT NULL,
    entry_method public.entry_method NOT NULL,
    transfer_pair_id uuid,
    external_id character varying(120),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    is_ai_classified boolean DEFAULT false NOT NULL,
    ai_confidence numeric(5,4),
    ai_category_guess character varying(40),
    ai_classification_source character varying(30),
    ai_classified_at timestamp with time zone,
    CONSTRAINT ck_transactions_ai_confidence CHECK (((ai_confidence IS NULL) OR ((ai_confidence >= (0)::numeric) AND (ai_confidence <= (1)::numeric)))),
    CONSTRAINT ck_transactions_ai_source CHECK (((ai_classification_source IS NULL) OR ((ai_classification_source)::text = ANY (ARRAY[('manual'::character varying)::text, ('merchant_rule'::character varying)::text, ('ai_auto'::character varying)::text, ('ai_suggestion'::character varying)::text, ('fallback'::character varying)::text]))))
);


--
-- Name: wallets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.wallets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    name character varying(120) NOT NULL,
    type public.wallet_type DEFAULT 'basic'::public.wallet_type NOT NULL,
    balance numeric(15,2) DEFAULT 0 NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: admins admins_email_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admins
    ADD CONSTRAINT admins_email_key UNIQUE (email);


--
-- Name: admins admins_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admins
    ADD CONSTRAINT admins_pkey PRIMARY KEY (id);


--
-- Name: admins admins_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admins
    ADD CONSTRAINT admins_username_key UNIQUE (username);


--
-- Name: ai_audit_events ai_audit_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_audit_events
    ADD CONSTRAINT ai_audit_events_pkey PRIMARY KEY (id);


--
-- Name: ai_chat_messages ai_chat_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_messages
    ADD CONSTRAINT ai_chat_messages_pkey PRIMARY KEY (id);


--
-- Name: ai_chat_sessions ai_chat_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_sessions
    ADD CONSTRAINT ai_chat_sessions_pkey PRIMARY KEY (id);


--
-- Name: ai_customer_preferences ai_customer_preferences_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_customer_preferences
    ADD CONSTRAINT ai_customer_preferences_pkey PRIMARY KEY (customer_id);


--
-- Name: ai_rate_limit_windows ai_rate_limit_windows_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_rate_limit_windows
    ADD CONSTRAINT ai_rate_limit_windows_pkey PRIMARY KEY (customer_id, feature, window_type, window_start);


--
-- Name: ai_spending_scores ai_spending_scores_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_spending_scores
    ADD CONSTRAINT ai_spending_scores_pkey PRIMARY KEY (id);


--
-- Name: ai_usage_events ai_usage_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_usage_events
    ADD CONSTRAINT ai_usage_events_pkey PRIMARY KEY (id);


--
-- Name: ai_weekly_reports ai_weekly_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_weekly_reports
    ADD CONSTRAINT ai_weekly_reports_pkey PRIMARY KEY (id);


--
-- Name: buckets buckets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.buckets
    ADD CONSTRAINT buckets_pkey PRIMARY KEY (id);


--
-- Name: budgets budgets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.budgets
    ADD CONSTRAINT budgets_pkey PRIMARY KEY (id);


--
-- Name: categories categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (id);


--
-- Name: category_correction_log category_correction_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.category_correction_log
    ADD CONSTRAINT category_correction_log_pkey PRIMARY KEY (log_id);


--
-- Name: customer_categories customer_categories_customer_id_category_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_categories
    ADD CONSTRAINT customer_categories_customer_id_category_id_key UNIQUE (customer_id, category_id);


--
-- Name: customer_categories customer_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_categories
    ADD CONSTRAINT customer_categories_pkey PRIMARY KEY (id);


--
-- Name: customer_settings customer_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_settings
    ADD CONSTRAINT customer_settings_pkey PRIMARY KEY (customer_id);


--
-- Name: customer_subscriptions customer_subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: customers customers_email_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_email_key UNIQUE (email);


--
-- Name: customers customers_google_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_google_id_key UNIQUE (google_id);


--
-- Name: customers customers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_pkey PRIMARY KEY (id);


--
-- Name: email_verification_tokens email_verification_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_verification_tokens
    ADD CONSTRAINT email_verification_tokens_pkey PRIMARY KEY (id);


--
-- Name: email_verification_tokens email_verification_tokens_token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_verification_tokens
    ADD CONSTRAINT email_verification_tokens_token_key UNIQUE (token);


--
-- Name: idempotency_keys idempotency_keys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency_keys
    ADD CONSTRAINT idempotency_keys_pkey PRIMARY KEY (id);


--
-- Name: merchant_rules merchant_rules_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_rules
    ADD CONSTRAINT merchant_rules_pkey PRIMARY KEY (rule_id);


--
-- Name: notifications notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_pkey PRIMARY KEY (id);


--
-- Name: rag_chunk rag_chunk_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rag_chunk
    ADD CONSTRAINT rag_chunk_pkey PRIMARY KEY (id);


--
-- Name: rag_document rag_document_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rag_document
    ADD CONSTRAINT rag_document_pkey PRIMARY KEY (id);


--
-- Name: refresh_tokens refresh_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id);


--
-- Name: refresh_tokens refresh_tokens_token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_token_key UNIQUE (token);


--
-- Name: savings_goal_contributions savings_goal_contributions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goal_contributions
    ADD CONSTRAINT savings_goal_contributions_pkey PRIMARY KEY (id);


--
-- Name: savings_goals savings_goals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goals
    ADD CONSTRAINT savings_goals_pkey PRIMARY KEY (id);


--
-- Name: scoring_criteria scoring_criteria_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scoring_criteria
    ADD CONSTRAINT scoring_criteria_code_key UNIQUE (code);


--
-- Name: scoring_criteria scoring_criteria_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scoring_criteria
    ADD CONSTRAINT scoring_criteria_pkey PRIMARY KEY (id);


--
-- Name: sepay_links sepay_links_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sepay_links
    ADD CONSTRAINT sepay_links_pkey PRIMARY KEY (wallet_id);


--
-- Name: subscription_plans subscription_plans_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_plans
    ADD CONSTRAINT subscription_plans_code_key UNIQUE (code);


--
-- Name: subscription_plans subscription_plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_plans
    ADD CONSTRAINT subscription_plans_pkey PRIMARY KEY (id);


--
-- Name: system_analytics system_analytics_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.system_analytics
    ADD CONSTRAINT system_analytics_pkey PRIMARY KEY (id);


--
-- Name: transactions transactions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.transactions
    ADD CONSTRAINT transactions_pkey PRIMARY KEY (id);


--
-- Name: ai_chat_sessions uq_ai_chat_sessions_id_customer; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_sessions
    ADD CONSTRAINT uq_ai_chat_sessions_id_customer UNIQUE (id, customer_id);


--
-- Name: idempotency_keys uq_idempotency_keys_customer_operation_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency_keys
    ADD CONSTRAINT uq_idempotency_keys_customer_operation_key UNIQUE (customer_id, operation, idempotency_key);


--
-- Name: wallets wallets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.wallets
    ADD CONSTRAINT wallets_pkey PRIMARY KEY (id);


--
-- Name: idx_chat_session; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_chat_session ON public.ai_chat_messages USING btree (customer_id, session_id, created_at);


--
-- Name: idx_contrib_goal; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_contrib_goal ON public.savings_goal_contributions USING btree (goal_id);


--
-- Name: idx_correction_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_correction_customer ON public.category_correction_log USING btree (customer_id, created_at DESC);


--
-- Name: idx_custcat_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_custcat_customer ON public.customer_categories USING btree (customer_id) WHERE (is_active = true);


--
-- Name: idx_idempotency_keys_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_idempotency_keys_created_at ON public.idempotency_keys USING btree (created_at);


--
-- Name: idx_notif_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notif_customer ON public.notifications USING btree (customer_id, sent_at DESC);


--
-- Name: idx_scores_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_scores_customer ON public.ai_spending_scores USING btree (customer_id, view, period_start DESC);


--
-- Name: idx_sepay_links_account_number; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sepay_links_account_number ON public.sepay_links USING btree (account_number) WHERE (account_number IS NOT NULL);


--
-- Name: idx_tx_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tx_category ON public.transactions USING btree (category_id);


--
-- Name: idx_tx_customer_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tx_customer_date ON public.transactions USING btree (customer_id, transaction_date DESC, id DESC);


--
-- Name: idx_tx_pair; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tx_pair ON public.transactions USING btree (transfer_pair_id) WHERE (transfer_pair_id IS NOT NULL);


--
-- Name: idx_tx_wallet; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tx_wallet ON public.transactions USING btree (wallet_id);


--
-- Name: idx_wallets_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_wallets_customer ON public.wallets USING btree (customer_id) WHERE (is_deleted = false);


--
-- Name: ix_ai_audit_events_correlation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_audit_events_correlation ON public.ai_audit_events USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_ai_audit_events_customer_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_audit_events_customer_time ON public.ai_audit_events USING btree (customer_id, occurred_at DESC);


--
-- Name: ix_ai_chat_messages_customer_session_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_chat_messages_customer_session_created ON public.ai_chat_messages USING btree (customer_id, session_id, created_at);


--
-- Name: ix_ai_chat_sessions_customer_recent; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_chat_sessions_customer_recent ON public.ai_chat_sessions USING btree (customer_id, last_message_at DESC, created_at DESC) WHERE (deleted_at IS NULL);


--
-- Name: ix_ai_rate_limit_windows_start; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_rate_limit_windows_start ON public.ai_rate_limit_windows USING btree (window_start);


--
-- Name: ix_ai_usage_events_customer_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_usage_events_customer_time ON public.ai_usage_events USING btree (customer_id, occurred_at DESC);


--
-- Name: ix_ai_usage_events_feature_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_ai_usage_events_feature_time ON public.ai_usage_events USING btree (feature, occurred_at DESC);


--
-- Name: ix_merchant_rules_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_merchant_rules_customer ON public.merchant_rules USING btree (customer_id);


--
-- Name: ix_rag_chunk_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_rag_chunk_customer ON public.rag_chunk USING btree (customer_id);


--
-- Name: ix_rag_chunk_embedding; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_rag_chunk_embedding ON public.rag_chunk USING hnsw (embedding public.vector_cosine_ops);


--
-- Name: ix_transactions_customer_ai_pending; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_transactions_customer_ai_pending ON public.transactions USING btree (customer_id, transaction_date DESC) WHERE (is_ai_classified = false);


--
-- Name: uq_active_subscription; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_active_subscription ON public.customer_subscriptions USING btree (customer_id) WHERE (status = 'active'::public.subscription_status);


--
-- Name: uq_budget; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_budget ON public.budgets USING btree (customer_id, category_id, COALESCE(wallet_id, '00000000-0000-0000-0000-000000000000'::uuid));


--
-- Name: uq_tx_external; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_tx_external ON public.transactions USING btree (external_id) WHERE (external_id IS NOT NULL);


--
-- Name: ux_ai_chat_sessions_customer_default; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_ai_chat_sessions_customer_default ON public.ai_chat_sessions USING btree (customer_id) WHERE ((is_default = true) AND (deleted_at IS NULL));


--
-- Name: ux_ai_spending_scores_customer_period; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_ai_spending_scores_customer_period ON public.ai_spending_scores USING btree (customer_id, view, period_start);


--
-- Name: ux_ai_weekly_reports_customer_period; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_ai_weekly_reports_customer_period ON public.ai_weekly_reports USING btree (customer_id, week_start);


--
-- Name: ux_merchant_rules_customer_keyword; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_merchant_rules_customer_keyword ON public.merchant_rules USING btree (customer_id, lower((merchant_keyword)::text));


--
-- Name: ai_audit_events ai_audit_events_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_audit_events
    ADD CONSTRAINT ai_audit_events_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE SET NULL;


--
-- Name: ai_chat_messages ai_chat_messages_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_messages
    ADD CONSTRAINT ai_chat_messages_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: ai_chat_messages ai_chat_messages_session_customer_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_messages
    ADD CONSTRAINT ai_chat_messages_session_customer_fkey FOREIGN KEY (session_id, customer_id) REFERENCES public.ai_chat_sessions(id, customer_id) ON DELETE CASCADE;


--
-- Name: ai_chat_sessions ai_chat_sessions_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_chat_sessions
    ADD CONSTRAINT ai_chat_sessions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: ai_customer_preferences ai_customer_preferences_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_customer_preferences
    ADD CONSTRAINT ai_customer_preferences_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: ai_rate_limit_windows ai_rate_limit_windows_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_rate_limit_windows
    ADD CONSTRAINT ai_rate_limit_windows_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: ai_spending_scores ai_spending_scores_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_spending_scores
    ADD CONSTRAINT ai_spending_scores_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: ai_usage_events ai_usage_events_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_usage_events
    ADD CONSTRAINT ai_usage_events_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE SET NULL;


--
-- Name: ai_weekly_reports ai_weekly_reports_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_weekly_reports
    ADD CONSTRAINT ai_weekly_reports_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: budgets budgets_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.budgets
    ADD CONSTRAINT budgets_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: budgets budgets_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.budgets
    ADD CONSTRAINT budgets_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: budgets budgets_wallet_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.budgets
    ADD CONSTRAINT budgets_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES public.wallets(id);


--
-- Name: categories categories_default_bucket_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_default_bucket_fkey FOREIGN KEY (default_bucket) REFERENCES public.buckets(id);


--
-- Name: category_correction_log category_correction_log_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.category_correction_log
    ADD CONSTRAINT category_correction_log_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admins(id) ON DELETE SET NULL;


--
-- Name: category_correction_log category_correction_log_corrected_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.category_correction_log
    ADD CONSTRAINT category_correction_log_corrected_category_id_fkey FOREIGN KEY (corrected_category_id) REFERENCES public.categories(id);


--
-- Name: category_correction_log category_correction_log_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.category_correction_log
    ADD CONSTRAINT category_correction_log_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: category_correction_log category_correction_log_transaction_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.category_correction_log
    ADD CONSTRAINT category_correction_log_transaction_id_fkey FOREIGN KEY (transaction_id) REFERENCES public.transactions(id) ON DELETE CASCADE;


--
-- Name: customer_categories customer_categories_bucket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_categories
    ADD CONSTRAINT customer_categories_bucket_id_fkey FOREIGN KEY (bucket_id) REFERENCES public.buckets(id);


--
-- Name: customer_categories customer_categories_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_categories
    ADD CONSTRAINT customer_categories_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: customer_categories customer_categories_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_categories
    ADD CONSTRAINT customer_categories_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: customer_settings customer_settings_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_settings
    ADD CONSTRAINT customer_settings_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: customer_subscriptions customer_subscriptions_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: customer_subscriptions customer_subscriptions_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_subscriptions
    ADD CONSTRAINT customer_subscriptions_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.subscription_plans(id) ON DELETE RESTRICT;


--
-- Name: email_verification_tokens email_verification_tokens_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_verification_tokens
    ADD CONSTRAINT email_verification_tokens_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: idempotency_keys idempotency_keys_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.idempotency_keys
    ADD CONSTRAINT idempotency_keys_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: merchant_rules merchant_rules_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_rules
    ADD CONSTRAINT merchant_rules_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: merchant_rules merchant_rules_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_rules
    ADD CONSTRAINT merchant_rules_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: notifications notifications_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notifications
    ADD CONSTRAINT notifications_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: rag_chunk rag_chunk_document_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rag_chunk
    ADD CONSTRAINT rag_chunk_document_id_fkey FOREIGN KEY (document_id) REFERENCES public.rag_document(id) ON DELETE CASCADE;


--
-- Name: rag_document rag_document_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rag_document
    ADD CONSTRAINT rag_document_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: refresh_tokens refresh_tokens_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: savings_goal_contributions savings_goal_contributions_goal_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goal_contributions
    ADD CONSTRAINT savings_goal_contributions_goal_id_fkey FOREIGN KEY (goal_id) REFERENCES public.savings_goals(id) ON DELETE CASCADE;


--
-- Name: savings_goal_contributions savings_goal_contributions_transaction_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goal_contributions
    ADD CONSTRAINT savings_goal_contributions_transaction_id_fkey FOREIGN KEY (transaction_id) REFERENCES public.transactions(id) ON DELETE SET NULL;


--
-- Name: savings_goals savings_goals_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goals
    ADD CONSTRAINT savings_goals_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: savings_goals savings_goals_funding_wallet_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.savings_goals
    ADD CONSTRAINT savings_goals_funding_wallet_id_fkey FOREIGN KEY (funding_wallet_id) REFERENCES public.wallets(id) ON DELETE SET NULL;


--
-- Name: sepay_links sepay_links_wallet_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sepay_links
    ADD CONSTRAINT sepay_links_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES public.wallets(id) ON DELETE CASCADE;


--
-- Name: system_analytics system_analytics_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.system_analytics
    ADD CONSTRAINT system_analytics_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admins(id) ON DELETE SET NULL;


--
-- Name: transactions transactions_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.transactions
    ADD CONSTRAINT transactions_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(id);


--
-- Name: transactions transactions_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.transactions
    ADD CONSTRAINT transactions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- Name: transactions transactions_wallet_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.transactions
    ADD CONSTRAINT transactions_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES public.wallets(id);


--
-- Name: wallets wallets_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.wallets
    ADD CONSTRAINT wallets_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--
