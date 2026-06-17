-- ============================================================
-- Migration V11: Transfer pairing (transfer_out / transfer_in)
-- Aligns internal transfers with schema v2.1 / APIs-List §4 + §12.6:
--   - mỗi lần chuyển quỹ = 2 record (transfer_out + transfer_in) cùng transfer_pair_id;
--   - category_id = null, loại khỏi mọi thống kê chi tiêu/score/budget;
--   - xóa 1 vế → xóa cả cặp + hoàn 2 ví (atomic).
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'transaction' AND column_name = 'transfer_pair_id'
    ) THEN
        EXECUTE 'ALTER TABLE transaction ADD COLUMN transfer_pair_id uuid';
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_transaction_transfer_pair
    ON transaction(transfer_pair_id)
    WHERE transfer_pair_id IS NOT NULL;
