-- ============================================================
-- One-time setup: grant ownership of all public tables to the
-- application user (admin). Run this ONCE as a superuser
-- (typically `postgres`) to fix error 42501 "must be owner of table".
--
-- Usage (PowerShell):
--   psql -U postgres -d FinViet -f V0__grant_ownership.sql
--
-- Or via psql shell:
--   \c FinViet postgres
--   \i V0__grant_ownership.sql
-- ============================================================

-- Re-assign all tables, sequences, and views in public schema to user "admin".
-- Replace 'admin' below if your application user has a different name.
DO $$
DECLARE
    r RECORD;
    target_user TEXT := 'admin';
BEGIN
    -- Tables
    FOR r IN
        SELECT schemaname, tablename
        FROM pg_tables
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('ALTER TABLE %I.%I OWNER TO %I', r.schemaname, r.tablename, target_user);
    END LOOP;

    -- Sequences
    FOR r IN
        SELECT sequence_schema, sequence_name
        FROM information_schema.sequences
        WHERE sequence_schema = 'public'
    LOOP
        EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO %I', r.sequence_schema, r.sequence_name, target_user);
    END LOOP;

    -- Views
    FOR r IN
        SELECT table_schema, table_name
        FROM information_schema.views
        WHERE table_schema = 'public'
    LOOP
        EXECUTE format('ALTER VIEW %I.%I OWNER TO %I', r.table_schema, r.table_name, target_user);
    END LOOP;

    -- Schema itself
    EXECUTE format('ALTER SCHEMA public OWNER TO %I', target_user);
END $$;

-- Belt-and-suspenders: also grant full privileges
GRANT ALL PRIVILEGES ON ALL TABLES    IN SCHEMA public TO admin;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO admin;
GRANT ALL PRIVILEGES ON SCHEMA public TO admin;

-- Make sure future tables created by other users are also accessible
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES    TO admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO admin;