-- Reconcile runtime schema changes that existed in legacy V22/V24 code but were skipped by
-- DbInitializer whenever it detected the externally provisioned plural-table schema.

CREATE TABLE public.income_allocation_settings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    effective_month character varying(7) NOT NULL,
    monthly_income numeric(15,2) NOT NULL,
    needs_pct integer NOT NULL,
    wants_pct integer NOT NULL,
    savings_pct integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT income_allocation_settings_pkey PRIMARY KEY (id),
    CONSTRAINT chk_income_allocation_pct_sum CHECK ((((needs_pct + wants_pct) + savings_pct) = 100)),
    CONSTRAINT uq_income_allocation_customer_month UNIQUE (customer_id, effective_month),
    CONSTRAINT income_allocation_settings_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE
);

CREATE INDEX idx_income_allocation_customer
    ON public.income_allocation_settings USING btree (customer_id);

ALTER TABLE public.savings_goal_contributions
    ADD COLUMN type character varying(20) DEFAULT 'contribution'::character varying NOT NULL;

ALTER TABLE public.savings_goal_contributions
    ADD CONSTRAINT chk_saving_goal_contribution_type
        CHECK (((type)::text = ANY (ARRAY[
            ('contribution'::character varying)::text,
            ('withdrawal'::character varying)::text
        ])));
