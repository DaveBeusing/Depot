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
