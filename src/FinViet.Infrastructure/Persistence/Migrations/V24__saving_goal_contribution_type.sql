-- Saving-goals ledger rework (docs/10-08-2026-be-todos.md §2a-2d): the contribution ledger
-- (savings_goal_contributions) needs a `type` column to distinguish contributions (money moving
-- from a wallet into the goal) from withdrawals (POST /saving-goals/{id}/withdraw, money moving
-- back out to a wallet) — both are stored as positive amounts, direction comes from `type`, not
-- sign. Existing rows backfill as 'contribution' (the only kind that existed before this change).
-- `note` already exists in the v3 baseline schema and is already EF-mapped; no column needed
-- for it, it was just never written to until this change.
--
-- Like V22/V23, DbInitializer.ApplyMigrationsAsync skips all numbered migrations once the v3
-- baseline schema is detected (`customers` table present) — run this manually against the
-- target database before starting the API. Idempotent: safe to run repeatedly.

ALTER TABLE savings_goal_contributions
    ADD COLUMN IF NOT EXISTS type varchar(20) NOT NULL DEFAULT 'contribution';

DO $$
BEGIN
    IF to_regclass('public.savings_goal_contributions') IS NULL THEN
        RETURN;
    END IF;

    ALTER TABLE savings_goal_contributions DROP CONSTRAINT IF EXISTS chk_saving_goal_contribution_type;
    ALTER TABLE savings_goal_contributions ADD CONSTRAINT chk_saving_goal_contribution_type
        CHECK (type IN ('contribution', 'withdrawal'));
END $$;

CREATE INDEX IF NOT EXISTS idx_saving_goal_contributions_goal ON savings_goal_contributions(goal_id);
