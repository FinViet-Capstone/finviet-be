-- ============================================================
-- Migration V19: Drop category_requests (admin-approval flow removed)
-- - Customers now reassign a category's bucket directly via
--   PUT /api/categories/{id}/bucket, writing straight to
--   customer_categories with no admin review step.
-- - Removes the category_requests table and its status enum.
-- Idempotent: re-runnable.
-- FinViet Project
-- ============================================================

DROP TABLE IF EXISTS category_requests;
DROP TYPE IF EXISTS category_request_status;
