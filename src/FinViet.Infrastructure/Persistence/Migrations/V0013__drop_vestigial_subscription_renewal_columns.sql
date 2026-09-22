-- Drop vestigial VNPay recurring-billing / renewal-scheduler columns on customer_subscriptions.
-- These were added in V0006 for a VNPay auto-renewal job that no longer exists: PayOS replaced
-- VNPay (V0012) without a renewal scheduler, and these columns have been dead ever since
-- (retry_count/next_retry_at only ever written to their defaults; renewal_claimed_at and
-- vnpay_card_token never read or written by any live code).
ALTER TABLE public.customer_subscriptions
    DROP COLUMN IF EXISTS next_retry_at,
    DROP COLUMN IF EXISTS retry_count,
    DROP COLUMN IF EXISTS renewal_claimed_at,
    DROP COLUMN IF EXISTS vnpay_card_token;
