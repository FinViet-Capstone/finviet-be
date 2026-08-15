CREATE TABLE public.notification_devices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    customer_id uuid NOT NULL,
    token character varying(255) NOT NULL,
    platform character varying(10) NOT NULL,
    installation_id character varying(100) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT notification_devices_pkey PRIMARY KEY (id),
    CONSTRAINT chk_notification_devices_platform CHECK (platform IN ('ios', 'android')),
    CONSTRAINT uq_notification_devices_customer_installation UNIQUE (customer_id, installation_id),
    CONSTRAINT uq_notification_devices_token UNIQUE (token),
    CONSTRAINT notification_devices_customer_id_fkey
        FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE CASCADE
);

CREATE INDEX idx_notification_devices_customer
    ON public.notification_devices USING btree (customer_id);
