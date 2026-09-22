# Subscription checkout with PayOS

This integration creates a PayOS payment order and returns an EMV/VietQR data string.
The client renders that string into a scannable QR code itself; PayOS does not host a checkout page for this flow, and the API does not fetch or proxy a QR image.

## Client flow

1. With a Customer bearer token, load `GET /api/subscriptions/plans` (active plans only).
2. Send `POST /api/subscriptions/create-payment` with the bearer token, optionally with a unique `Idempotency-Key` header:

   ```json
   { "planId": "<plan UUID>" }
   ```

   The `Idempotency-Key` header is optional.
   When present, a repeated key with the same request body replays the original response instead of creating a second order.
3. Read `data.orderCode`, `data.qrCode`, `data.amount` (VND), and `data.expiresAt` (UTC, 15 minutes after creation). Render `qrCode` locally as a QR code. The amount is taken from the server's plan price, never the client.
4. Poll `GET /api/subscriptions/payment-status/{orderCode}` with the same customer's bearer token, for example every 3 seconds while the checkout is visible. Stop on a terminal status or when leaving the screen. Another customer's order code returns 404. Show success only for `data.status == "succeeded"`; `data.subscriptionId` then identifies the activated subscription.
5. At checkout expiry, stop offering the old QR. A pending payment remains pending until the webhook arrives or the next status poll triggers reconciliation; time passing alone is not proof that payment failed or succeeded. Recheck status before offering another attempt. Use a new idempotency key for a new purchase attempt; reuse the original key and identical body only for network retries.
6. `GET /api/subscriptions/current` returns the signed-in customer's `active`/`past_due` subscription, or null. Use it to avoid offering a second purchase while a current subscription exists.

A `create-payment` call fails with `already_subscribed` (422) if the customer already holds an `active` subscription, or `plan_discontinued` (422) if the plan is no longer offered.

## Server-side confirmation: webhook plus self-healing reconciliation

`POST /api/webhooks/payos` is the primary confirmation path.
It carries no `[Authorize]` attribute, since PayOS calls it directly; verification instead relies on the payOS SDK's signature check over the raw request body.
It returns 200 for all verified calls, including duplicates and unknown order codes, so PayOS stops retrying.
It returns 400 only when signature verification fails.

Because payOS only reliably fires the webhook on success, `GET /api/subscriptions/payment-status/{orderCode}` also reconciles directly against payOS whenever the stored payment is still `pending`.
It calls the same provider status lookup the webhook path uses internally, and on a terminal result applies it through the same outcome handler.
This means an abandoned or expired QR resolves to `failed` the next time the owning customer polls status, without needing a separate background job.

Both paths share one outcome handler that:

- Row-locks the `Payment` (`FOR UPDATE`) before applying a result, and is a no-op if the payment is no longer `pending` — so a webhook retry, or a race between the webhook and a status poll, is always safe.
- Treats a reported amount that doesn't match the order's stored amount as a failure regardless of the provider's success flag, as defense-in-depth against a plan-price change or a mis-provisioned order.
- On a genuine success for an initial charge, creates the `CustomerSubscription` (`active`, `NextBillingDate` = today + the plan's billing interval) and links it back onto the `Payment`.

## Merchant setup and verification

Configure `PayOS__ClientId`, `PayOS__ApiKey`, and `PayOS__ChecksumKey` using environment variables or user-secrets.
Never commit credentials.
Optionally set `PayOS__WebhookUrl` to the publicly reachable HTTPS webhook URL as a default.

`POST /api/admin/payos/confirm-webhook` (Admin bearer token, optional `{ "webhookUrl": "..." }` body) is a one-time-per-environment call that registers the webhook URL with payOS — payOS sends no webhooks until a URL has been confirmed this way.
Omitting the body field falls back to the configured `PayOS:WebhookUrl`.
It returns the confirmed `webhookUrl`, `accountName`, and `accountNumber`, so the caller can verify it registered the intended merchant account.

Verify success, cancel/expiry, delayed and duplicate webhook delivery, and a status poll that arrives before the webhook, before relying on this in production.

Reference: https://payos.vn/docs/

## Client integration status

This document describes the backend contract only.
Web and mobile checkout UI live outside this repository; check their own docs/issue tracker for current integration status against the endpoints above.
