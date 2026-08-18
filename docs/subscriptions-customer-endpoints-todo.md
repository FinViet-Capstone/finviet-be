# Subscriptions — missing customer-facing endpoints for finviet-be

Written while removing `finviet-mobile`'s mock service layer (branch
`feature/remove-mock-layer`) and auditing every domain for real-backend coverage.
Subscriptions turned out to be further along than expected — a real, VNPay-integrated
subscribe flow already exists — but the mobile app has no way to read plan/subscription
data as a logged-in customer, so it can't build a working "Gói dịch vụ" screen yet.

## What already exists (confirmed by reading this repo's source, not guessed)

- `POST /api/subscriptions/subscribe` (`SubscriptionsController.cs`) — starts a VNPay
  checkout for a given `PlanId`, real and wired end to end.
- `GET /api/subscriptions/vnpay/return` and `GET /api/subscriptions/vnpay/ipn` — the VNPay
  browser-return and server callback legs.
- `GET/POST/PATCH /api/admin/subscription-plans` (`AdminSubscriptionPlansController.cs`) —
  full plan CRUD, but `[Authorize(Roles = "Admin")]` only.

## What's missing

There is no `Customer`-role endpoint for either of these two reads, both needed before a
real subscription screen is buildable client-side:

1. **List available plans.** A customer picking a plan to subscribe to needs to see the
   catalog (name, monthly/annual price, feature list, `isPopular`) without admin rights —
   e.g. a `[Authorize(Roles = "Customer")]` `GET` that returns the same
   `SubscriptionPlanDto` shape `AdminSubscriptionPlansController` already uses, filtered to
   non-discontinued plans.
2. **Read my current subscription.** After subscribing (or on any later app open), the
   customer needs to know what they currently have — plan, billing cycle, status,
   `currentPeriodEnd`, `cancelAtPeriodEnd` — to render "current plan" state and decide
   whether to show "Subscribe" vs. "Manage"/"Cancel". `CustomerSubscription` already has
   this data (per `SubscriptionRenewalScheduler.cs` and the entity itself); there's just no
   customer-facing read of it yet.

Cancel (`cancelAtPeriodEnd` toggle or similar) isn't listed as missing here since it wasn't
audited as thoroughly — worth checking whether an equivalent already exists or should be
scoped alongside these two.

## Mobile-side status

The Settings → "Gói dịch vụ" entry point has been removed from the mobile app entirely
(rather than half-wire it against a real `subscribe` endpoint with no way to show what the
customer actually has). Its mock implementation (`src/services/mock/subscriptions.ts`,
`src/hooks/useSubscription.ts`, `app/settings/subscription.tsx`) is being deleted outright
as part of the same mock-layer removal, including the hook's old doc-comment describing an
aspirational `GET/POST/DELETE /customers/me/subscription` contract — that doesn't match how
subscribing actually works here (VNPay redirect, not a direct upgrade/cancel call), so it
shouldn't be used as the spec for whatever gets built. When the two reads above land, the
mobile screen will need a fresh real integration, not a mock swap — there's nothing left
mocked to swap out.
