# Search Performance Evidence

## Scope

This document records structural and benchmark evidence for Global Search, master-data search and large collection refreshes. Search keeps substring fallback behavior for meaningful queries while prioritizing identifier-friendly exact and prefix paths.

## Baseline

Before the search optimization pass:

- Item Master uses broad `LIKE '%search%'` predicates across item, reference and supplier fields.
- Customers use broad contains matching across customer number, name, e-mail and tax identifiers.
- Suppliers use broad contains matching across customer number, name, contact and related text fields.
- Purchase Orders, Sales Orders and Sales Invoices use paged contains queries even when Global Search does not consume the page count.
- Finance Global Search loads a recent journal page and performs the text match in memory.
- Global Search already bounds final results and executes providers in parallel.
- DataGrid virtualization, row/column virtualization, recycling and content scrolling are already enabled and are treated as a regression contract.

The deterministic benchmark in `SearchPerformanceContractTests` exercises 10k and 100k representative rows and records exact, prefix and contains timings without enforcing machine-specific timing thresholds.

## Policy

- query-shape optimization precedes index additions;
- no index is added without benchmark/query-plan evidence;
- two-character searches avoid broad contains scans on large tables;
- contains fallback remains available from three characters;
- result sets remain bounded;
- cancellation and stale-result protection remain mandatory;
- benchmark timings are evidence, not correctness assertions.


## Implemented query strategy

The high-volume identifier paths now use the shared `SearchQueryPlan` contract. Exact and prefix predicates are always available; broad contains predicates are added only when the normalized query contains at least three characters. Existing contains behavior therefore remains available for meaningful terms while two-character input avoids a leading-wildcard scan.

The covered identifier-heavy repository paths include Item Master, Customers, Suppliers, Purchase Orders, Sales Orders, Sales Invoices, Shipments and Sales Quotes.

## Global Search fan-out

Global Search now filters providers by `CanSearch` before starting provider tasks. Built-in record providers use `GlobalSearchReadRepository`, which returns bounded `QuerySliceAsync` results and does not execute page-count queries. Finance Journal matching is performed by a bounded server-side query instead of loading a larger recent window and filtering it in memory.

The existing palette debounce, cancellation token replacement and stale-result guard remain unchanged.

## Collection refresh

`CollectionSynchronizer.Replace` remains linear. Shared positions now short-circuit on `ReferenceEquals` and `EqualityComparer<T>.Default.Equals`, preventing unnecessary ObservableCollection replace notifications when the effective value did not change. No keyed/big-bang synchronization layer was introduced because the first-stage optimization addresses the measured structural issue without changing selection semantics.

## Schema and index decision

No index was added. This package changes query shape first and therefore does not advance any persisted schema version. Index changes remain contingent on provider-specific benchmark or query-plan evidence.

## Validation evidence

- deterministic 10k and 100k exact/prefix/contains benchmark probe;
- structural coverage for Item Master, Customers, Suppliers, Purchase Orders, Sales Orders, Sales Invoices, Shipments and Sales Quotes;
- Global Search permission and bounded-read contracts;
- Finance Journal server-side search contract;
- rapid-input debounce/cancellation/stale-result contract;
- CollectionSynchronizer notification regression tests;
- AppDataGrid virtualization/recycling contract.

Machine-specific p95 targets are not encoded as correctness thresholds. Runtime p95 should be reported from stable local and remote test environments rather than inferred from CI runner timings.
