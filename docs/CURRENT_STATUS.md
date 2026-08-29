# Current project status

Updated: 2026-08-29

Depot is on the `0.15.x-preview` development line. The repository contains the integrated Finance platform: foundation/master data, immutable General Ledger, Receivables, Payables, FIFO Inventory Accounting, Banking and Payments, Financial Reporting, and effective-dated Localization.

Sales pricing supports Global, Regional and optional Customer scopes. The central resolver falls back Customer → Region → Global for each item and retains the selected price source on quote and order lines.

Item Cost Build-up now derives a traceable commercial item cost from the active preferred supplier purchase price plus ordered Absolute/Percentage Cost Components. Percentage components explicitly use BaseCost or RunningTotal. Bulk Pricing consumes the same central calculation service, applies Percentage Markup, requires a Preview, supports All Active/Category/Manufacturer/Selected filters and applies through Replace/Only Increase/Only Missing modes to the existing scoped PriceList model.

## Finance capabilities

- **Finance Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger & Posting:** immutable balanced journals, reporting-currency snapshots, posting profiles, validation, idempotency, Audit evidence and linked reversals.
- **Accounts Receivable:** customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales integration.
- **Accounts Payable:** supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and Purchasing integration.
- **Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments:** bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **Financial Reporting:** Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension filtering, mappings, deterministic CSV and immutable report snapshots.
- **Finance Localization:** explicit effective-dated legal-entity assignments, built-in `GENERIC → EU → DE` references, custom pack extensibility, capability/configuration/procedure registry, RBAC and Audit evidence.

## Pricing and costing safeguards

- Item cost Base Cost is the active preferred supplier purchase price with an explicit Item Cost Profile currency.
- Legacy supplier prices are not silently treated as EUR; mismatched target currency fails closed until controlled FX conversion is available.
- Cost Components use deterministic Sequence + persisted Id ordering and optimistic Version checks.
- Bulk Preview and Apply both use `ItemCostCalculationService`; there is no second cost formula.
- Percentage Markup is explicitly distinct from Gross Margin.
- Bulk Apply is atomic, revalidates PriceList/entry/cost evidence and records batch Audit evidence.
- Historical submitted/finalized Sales documents remain snapshot-based and are not rewritten by later Bulk Pricing.

## Versions

- Application: **0.15.x-preview** (`Directory.Build.props` is authoritative for the exact patch)
- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- Help manifest: **1.18**

Every commit increments `DepotVersionPatch`.

## Validation boundary

Release Build, win-x64 publish, repository regression tests, Release Integrity, Security Supply Chain and Software Quality gates are required on the final integration head. Provider-neutral DDL exists for SQLite, SQL Server and MySQL/MariaDB. Optional live-provider tests exercise scoped pricing and Item Cost schema migration when server connection strings are configured.

## Next steps

Further pricing extensions are demand-driven: controlled FX conversion for cross-currency cost-to-price generation, additional explicit Base Cost source strategies, Target Gross Margin as a separate pricing rule, and commercial rounding strategies such as 0.05/0.10/0.50 or .99 endings. None of these are simulated by the current Markup implementation.
