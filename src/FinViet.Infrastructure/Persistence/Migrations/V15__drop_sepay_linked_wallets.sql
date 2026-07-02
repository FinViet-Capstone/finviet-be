-- Removes the SePay-backed linked-wallet storage now that FinViet links banks through Finverse
-- only (Finverse data lives in finverse_links, added by V14). Run manually before starting the API.
-- Idempotent.
--
-- Note: the 'sepay_linked' / 'sepay_sync' / 'sepay_sync_error' labels remain in the shared
-- wallet_type / entry_method / notification_type enums — Postgres cannot drop individual enum
-- values, and the CLR enums keep the matching members so any legacy rows still read back. Only the
-- standalone sepay_sync_status enum (used exclusively by wallet_links) is dropped here.

DROP TABLE IF EXISTS wallet_links;
DROP TYPE IF EXISTS sepay_sync_status;
