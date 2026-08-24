# AI Pipeline Usage Report — Before Rate-Limit Fix

Captured manually via Beekeeper Studio against the deployed Render database on
2026-08-25, reflecting cumulative `ai_usage_events` data from 2026-08-13 through
2026-08-19 (no filter — all-time as of capture). This predates the
`classification_preview` rate-limit fix (`fix/classification-preview-rate-limit`:
new `PreviewPerMinute`/`PreviewPerDay` tier in `AiLimitsOptions`, wired into
`PostgresAiRateLimiter`), which as of this capture has not been deployed.

| Feature | Outcome | Count | Avg ms | p50 ms | p95 ms | First seen (UTC) | Last seen (UTC) |
|---|---|---|---|---|---|---|---|
| chat | error | 4 | 149 | 153 | 168 | 2026-08-15 03:37 | 2026-08-15 04:43 |
| chat | rate_limited | 3 | 132 | 126 | 150 | 2026-08-15 03:37 | 2026-08-15 04:27 |
| chat | success | 18 | 2717 | 1523 | 6055 | 2026-08-14 10:16 | 2026-08-19 04:40 |
| classification | rate_limited | 2 | — | — | — | 2026-08-17 21:06 | 2026-08-17 21:06 |
| classification | success | 6 | 1748 | 1719 | 2307 | 2026-08-17 21:06 | 2026-08-17 21:06 |
| classification_batch | error | 1 | 1713 | 1713 | 1713 | 2026-08-18 17:28 | 2026-08-18 17:28 |
| classification_batch | success | 21 | 4527 | 4097 | 11320 | 2026-08-18 17:28 | 2026-08-19 04:18 |
| classification_preview | rate_limited | 1503 | — | — | — | 2026-08-17 21:03 | 2026-08-18 17:08 |
| classification_preview | success | 84 | 2698 | 1385 | 8023 | 2026-08-13 05:56 | 2026-08-19 04:15 |
| rag_document_embedding | success | 33 | 751 | 608 | 764 | 2026-08-18 04:19 | 2026-08-18 13:54 |
| score_comment | error | 13 | 575 | 185 | 2259 | 2026-08-14 17:49 | 2026-08-15 04:43 |

1,688 total recorded calls.

## Findings

- **`classification_preview`**: only 84/1587 calls succeeded (5.3%) — non-success
  outcomes outnumber successes by a wide margin. Breakdown: rate_limited=1503,
  success=84. Root cause: this feature shared the generic `PerUserPerMinute=6`
  cap with the unrelated `classification` (SePay auto-sync) feature, even
  though it's driven by interactive, bursty user taps (the mobile "Gợi ý danh
  mục bằng AI" button and photo-extraction categorization) rather than
  occasional automatic per-transaction calls. Nearly all throttling landed in
  one ~20-hour window (2026-08-17 21:03 → 2026-08-18 17:08), coinciding with
  testing of that feature. Fix: `classification_preview` now gets its own
  30/min, 300/day tier, independent of the standard 6/min cap.
- **`score_comment`**: only 0/13 calls succeeded (0.0%) — all 13 attempts in
  this window errored. Not addressed by this fix; flagged as a separate
  follow-up (see `context/current-feature.md` for status).

## Not yet regenerated

An "after" report should be captured the same way once this fix has been
deployed (merged to `dev`, then `dev` → `main`, which triggers
`deploy-render.yml`) and the "Gợi ý danh mục bằng AI" button has seen renewed
real usage — the automated `AiUsageReportTests.GenerateAiUsageReport`
(`tests/FinViet.Infrastructure.IntegrationTests`, `Category=Report`) can
generate it directly by pointing `FINVIET_REPORT_DB_CONNECTION` at the Render
database.
