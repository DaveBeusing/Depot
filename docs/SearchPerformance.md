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
