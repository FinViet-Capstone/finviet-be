-- Splitting one transaction across several categories / buckets.
--
-- Council review 1 (30-05-2026): "Phân tích và định nghĩa rõ việc phân chia một khoản
-- thu / chi phát sinh." One real-world payment often belongs to more than one category —
-- a supermarket receipt is part groceries, part household — and the schema had no way to
-- express that: transactions carry exactly one category_id and one amount.
--
-- Modelled as *replacement rather than nesting*: splitting deletes the original row and
-- writes N sibling rows whose amounts sum to it. That keeps every existing aggregation
-- (bucket spend, budget spent, spending score, weekly report — all of which group by
-- transactions.category_id) correct with no changes, and keeps the wallet balance
-- untouched because the signed total is unchanged. A parent row kept alongside its
-- children would have double-counted in every one of those queries.
--
-- split_group_id is what survives the replacement: the siblings from one split share a
-- group id, so the UI can show "tách từ một giao dịch" and the relationship stays
-- inspectable. Nullable — every transaction created before this, and every unsplit one
-- after it, has no group.

ALTER TABLE public.transactions ADD COLUMN split_group_id uuid;

-- Partial index: only split rows are ever looked up by group, and they are a small
-- minority of the table. Mirrors the existing idx_tx_pair / uq_tx_external convention.
CREATE INDEX idx_tx_split_group ON public.transactions USING btree (split_group_id)
    WHERE (split_group_id IS NOT NULL);
