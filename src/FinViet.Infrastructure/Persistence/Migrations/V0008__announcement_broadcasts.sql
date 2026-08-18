-- Admin announcement broadcast history.
--
-- Lets an admin fan out one Notification row (type='announcement') to every active customer in a
-- single action. The notifications themselves reuse the existing public.notifications table
-- (customer_id, type, title, body) — no schema change needed there since notification_type already
-- has an 'announcement' member. This table only records one row per broadcast for the admin
-- "Announcement history" screen; recipient_count is captured at send time rather than recomputed
-- later from notifications, since the active-customer count can drift after the fact.
--
-- target_segment is constrained to 'all' for now — no segment-targeting criteria has been decided
-- yet, so the check constraint intentionally has a single allowed value until that's chosen.

CREATE TABLE public.announcement_broadcasts (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    admin_id uuid NOT NULL REFERENCES public.admins(id) ON DELETE RESTRICT,
    title character varying(200) NOT NULL,
    message character varying(500) NOT NULL,
    target_segment character varying(20) NOT NULL DEFAULT 'all',
    target_label character varying(100) NOT NULL,
    recipient_count integer NOT NULL DEFAULT 0,
    sent_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_announcement_broadcasts_target_segment CHECK (target_segment IN ('all'))
);

CREATE INDEX idx_announcement_broadcasts_sent_at ON public.announcement_broadcasts (sent_at DESC);
