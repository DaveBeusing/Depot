# My Work Performance Evidence

## Scope

My Work provider diagnostics record structural performance evidence only: provider name, eligibility, elapsed time, returned row count, failure state and database command count. Business record identifiers, document numbers, customer/supplier names, amounts and other business data are not logged.

Runtime evidence is written to `performance.log`. Database command counts are captured with an asynchronous provider-local scope so parallel providers remain independently measurable.

## Structural baseline before query optimization

The table below is the permission-maximal command fan-out derived from the repository paths used by `MyWorkService` before the query-reduction commits in this performance package. A paged repository query performs a count command plus the bounded result command.

| Provider | Baseline DB commands | Source of fan-out |
| --- | ---: | --- |
| Purchasing | up to 11 | four status pages = 8; approval page = 2; approval summary = 1 |
| Sales and Shipping | 14 | five sales-order status pages = 10; two shipment status pages = 4 |
| Inventory Counts | 8–32 | four status pages = 8; draft/posted header lookups add up to 24 |
| Accounts Receivable | 2 | one paged open-item query |
| Accounts Payable | 8–56 | four status pages = 8; pending/approved document detail + line loads add up to 48 |
| Banking | 4 | payment-run page = 2; unreconciled-line page = 2 |

The runtime counter is authoritative for a concrete user/permission set. Providers that are not eligible are recorded with zero queries.

## Performance contract

- Every My Work provider remains permission-gated.
- Provider failures stay isolated from other providers.
- Cancellation remains propagated.
- Diagnostics contain no business payload.
- Query reductions must prefer bounded read projections and slices over schema changes.


## Query fan-out after consolidated My Work reads

The measured high-fan-out providers now use dedicated bounded projections instead of repeated status pages:

| Provider | Before | After |
| --- | ---: | ---: |
| Purchasing | up to 11 | 2 |
| Sales and Shipping | 14 | 2 |
| Inventory Counts | 8–32 | 1 |
| Accounts Payable | 8–56 | 1 |

Per-status Top-N semantics are preserved by stable `ROW_NUMBER() OVER (PARTITION BY Status ...)` bounds. These reads do not execute `COUNT(*)` page totals. Inventory Counts carries creator and completion metadata in the projection, removing its header N+1. Payables carries the match-exception flag through an `EXISTS` projection, so no document/line detail load is required.


## Progressive Home evidence

Home starts the dashboard and My Work operations in parallel. Each result is now applied independently as soon as its task completes, guarded by the same `LatestRequestLease.IsCurrent` generation check. A stale generation therefore cannot overwrite any already-current block.

The first successfully applied block writes a structural `home firstContent=<block> elapsedMs=<value>` entry to `performance.log`. This enables local and remote p95 validation without logging business data. Manual shell refresh continues to invoke the complete `DashboardViewModel.LoadAsync` path; the My Work panel's own refresh remains intentionally scoped to My Work only.
