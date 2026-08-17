# Saving Goals ↔ Budgets ↔ AI Score — open questions for finviet-be

Written after a cross-repo inspection of how Saving Goals, Budgets bucket pacing, and the
AI Spending Score relate to each other (`finviet-mobile` branch
`fix/savings-goal-budget-score-integration`, this repo's branch
`fix/savings-bucket-goal-netting`). The inspection was thorough enough to implement one fix
directly here — see below — so this list is deliberately short: it's what's left as genuine
product/design questions, not implementation gaps. **Please verify each item against this
codebase's actual intent before acting on any of them** — the mobile side's reading of
"why" a piece of backend behavior exists may be wrong, since it was inferred from code and
docs, not from whoever made the original call.

## Already implemented, not a question

`ComputeBucketSpentAsync`'s Savings bucket now nets `cat_savings_goal` contributions minus
withdrawals into `Spent` (floored at 0), instead of excluding the category outright — see
`BudgetService.cs` (`ComputeGoalNetSavingsAsync`) and `BudgetServiceTests.cs`. Reasoning: a
goal contribution is functionally the customer fulfilling their Savings allocation, and
excluding it made the bucket effectively unfillable for anyone who uses Goals. Flagging here
in case this reasoning turns out to be wrong for a reason not visible from the code —
easy to revert if so.

## Open questions

### 1. Score colour thresholds — final calibration or provisional?

`SpendingScoreService.cs:87`: `finalScore >= 80 → GREEN`, `>= 50 → YELLOW`, else `RED`.

`finviet-mobile` previously assumed 70/40 cutoffs everywhere (a documentation guess, not
read from this codebase) and has now been corrected to always display the backend's real
`colorBadge` instead of re-deriving a band from the number. That means any future change to
these thresholds now shows up immediately, app-wide, with no client-side lag — worth knowing
before touching them. Is 80/50 the intended final calibration, or was it a placeholder?

### 2. `savingsScore`'s hardcoded 20%-of-income target

`SpendingScoreService.cs` (`ComputeSavingsAsync`): `const double targetRate = 0.20`. Every
customer is scored against the same flat 20% savings-rate target regardless of their own
income, goals, or configured Savings allocation percentage — which can itself be any value
the customer sets (`Customer.SavingsPct`, no longer even defaulting to 20 necessarily).

Is a flat 20% the intended long-term design, or would this eventually make sense as a
per-customer target — e.g. reading the customer's own `SavingsPct` instead of a constant —
mirroring how `scoring_criteria` weights are already admin-editable? Not urgent, just
flagging so the mobile side's UI copy describing this sub-score (which now surfaces
`savingsScore` directly, see `SpendingScoreCard`/`score.tsx`) doesn't need to change again if
this becomes configurable.

### 3. Goal withdrawal is an `income`-typed transaction on an `expense`-typed category

`SavingGoalService.cs`'s `WithdrawAsync` creates a transaction with
`TransactionType = "income"`, tagged with `CategoryId = "cat_savings_goal"` — whose seeded
`Category.Type` is `"expense"` (`V0002__baseline_reference_data.sql`). Contributions are the
matching, consistent pair (`TransactionType = "expense"` on the same category), so this
isn't a bug in the sense of anything breaking today — the transaction's own `TransactionType`
column is authoritative for sign/direction, not the category's `Type`. Flagging only in case
any backend-side analytics, reporting, or a future feature ends up filtering/aggregating by
`Category.Type` instead of `Transaction.TransactionType` — that would mis-sign or
double-bucket these specific withdrawal rows.

## Context, if useful

Full inspection notes (both repos, with exact file:line citations) are recorded in
`finviet-mobile`'s `context/current-feature.md`, under this same feature name, if more detail
than the above is ever needed.
