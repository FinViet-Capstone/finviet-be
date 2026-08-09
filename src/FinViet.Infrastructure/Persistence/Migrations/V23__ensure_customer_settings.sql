-- Defensive migration (be-revamp.md item 3): no migration or setup script in this repo actually
-- creates customer_settings, yet Customer.cs/FinVietDbContext.cs have mapped it since before this
-- change — it must exist only in whatever externally-provisioned baseline schema this DB started
-- from (same situation V14's comment describes for the wallet/transaction v3 schema). This
-- guarantees the table exists with the shape the EF mapping expects, without disturbing it if it's
-- already there. Run manually before starting the API. Idempotent.

CREATE TABLE IF NOT EXISTS customer_settings (
    customer_id             uuid PRIMARY KEY REFERENCES customers(id) ON DELETE CASCADE,
    default_currency        varchar(3) NOT NULL DEFAULT 'VND',
    language                app_language NOT NULL DEFAULT 'vi',
    theme                   app_theme NOT NULL DEFAULT 'system',
    notif_budget            boolean NOT NULL DEFAULT true,
    notif_report            boolean NOT NULL DEFAULT true,
    notif_goals             boolean NOT NULL DEFAULT true,
    notif_budget_thresholds integer[] NOT NULL DEFAULT '{80,100}',
    fcm_token               varchar(255),
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);
