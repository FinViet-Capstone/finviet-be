# Subscription checkout with VNPAY QR

This integration opens VNPay's hosted QR payment screen. VNPay generates the bank-app-scannable payment QR; the API does not manufacture a QR from the redirect URL or return a QR image.

## Client flow

1. With a Customer bearer token, load `GET /api/subscriptions/plans` (active plans only).
2. Send `POST /api/subscriptions/subscribe` with the bearer token and a unique `Idempotency-Key` header:

   ```json
   { "planId": "<plan UUID>", "returnUrl": "https://your-client.example/payment-return", "bankCode": "VNPAYQR" }
   ```

   `bankCode` also accepts `VNBANK`, `INTCARD`, or omission for VNPay's payment-method selector. Existing clients may omit it.
3. Read `data.redirectUrl`, `data.paymentId`, `data.amount` (VND), and `data.expiresAt` (UTC). Open `redirectUrl` to display VNPay's QR. Checkout expires after 15 minutes. The amount is taken from the server's plan, never the client.
4. Poll `GET /api/subscriptions/payments/{paymentId}` with the same customer's bearer token, for example every 3 seconds while the checkout is visible. Stop on a terminal status or when leaving the screen. Other users receive 404. Show success only for `data.status == "succeeded"`; `subscriptionId` then identifies the activated subscription.
5. At checkout expiry, stop offering the old URL. A pending payment remains pending until IPN arrives; time passing alone is not proof that payment failed. Recheck status before offering another attempt. Use a new idempotency key for a new purchase attempt; reuse the original key and identical body only for network retries.

The browser return endpoint remains informational. Only the verified server-to-server IPN updates payment/subscription state. The new polling endpoint handles scanning on another device without requiring the original browser to return.

## Merchant setup and verification

Configure `VNPay__TmnCode` and `VNPay__HashSecret` using environment variables or user-secrets. Never commit credentials. Set `VNPay__PaymentUrl` for the correct environment. Register the publicly reachable HTTPS `/api/subscriptions/vnpay/ipn` URL with VNPay and ensure the merchant terminal supports QR payments.

Offline tests cover signed method/amount/expiry fields, accepted methods, and payment ownership. A real end-to-end sandbox check still requires merchant credentials, a reachable IPN endpoint, and a QR-enabled terminal. Verify success, cancel/failure, delayed and duplicate IPN, and return-before-IPN before production.

Reference: https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html

## Web and mobile entry points

- Web: `/subscription`, also linked from the admin **System configuration → Plans** tab. This is a separate customer login (email/password), not an admin purchase. `FINVIET_API_BASE_URL` must point to the backend origin, without `/api`. The server proxies customer requests with a scoped HttpOnly cookie; the browser never receives the access token. When the access-token session expires, sign in again. Checkout state survives refresh in the same tab, scoped to the customer.
- Mobile: **Settings → Gói đăng ký**. Uses the existing authenticated API client (`EXPO_PUBLIC_API_BASE_URL`, including `/api`) and opens the hosted VNPay page in the system browser. Checkout state is saved in SecureStore before submission and restored on returning/reopening. The default browser return URL is the backend informational return endpoint; return to FinViet manually to see status. An HTTPS return page can be supplied with `EXPO_PUBLIC_VNPAY_RETURN_URL`.
- `GET /api/subscriptions/current` returns the signed-in customer's active/past-due subscription or null. Both clients use it to avoid offering a second purchase while a current subscription exists.
- Polling pauses in the background and after checkout expiry; manual status checks remain available. Closing the VNPay page does not cancel a payment. At expiry, the customer must check status and confirm they have not paid before starting another attempt.
- QR is generated and displayed by VNPay, not embedded as a fabricated bank-transfer code. Actual merchant QR availability depends on the terminal configuration.

Frontend checks: web `node tests/subscription-api.test.cjs`; mobile `node node_modules/jest/bin/jest.js subscriptions.test.ts subscription-screen.test.tsx --runInBand`; TypeScript and ESLint on both projects. Live merchant settlement and native device/browser switching require sandbox/device verification.
