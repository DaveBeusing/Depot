# Budgeting and Variance Analysis

Finance Budgeting is Depot's controlled planning workspace. Budgets are planning evidence; they never replace the General Ledger and do not post accounting entries.

## Budget versions

A budget is scoped to a legal entity, accounting book, fiscal year and reporting currency. Versions move through **Draft → Pending Approval → Approved → Locked**. Approved and Locked versions are immutable. Changes after approval use a new amendment/version so approved history remains intact.

## Budget lines

Budget lines are signed planned amounts by account and accounting period with optional existing accounting dimensions. Annual spread allocates equally and assigns the deterministic rounding remainder to the final period.

## CSV import and export

Preview validates account, period, dimension references and duplicate keys before writes. Apply is atomic; replace mode is explicit. Export is bounded and deterministic.

## Approval and locking

Submission resolves and stores the Finance Budget approval-policy snapshot. Pending budgets cannot be edited. `FinanceBudgeting.View`, `FinanceBudgeting.Manage`, `FinanceBudgeting.Approve` and `FinanceBudgeting.Lock` separate viewing, preparation, approval and locking.

The Finance role manages and locks but does not approve. The Approver role can approve but does not manage drafts.

## Actual vs Budget

**Actual values always originate from current General Ledger/Financial Reporting paths.** Variance is `Actual - Budget`; Budget is planning evidence only.

## My Work

Owned drafts appear in **My Drafts**, submitted owned budgets in **Waiting**, and policy-eligible pending approvals in **Needs My Action**.

## Database providers

Budget persistence is Finance feature schema **11** and remains subject to the real-provider acceptance matrix.
