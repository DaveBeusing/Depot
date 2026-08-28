# Current project status

Updated: 2026-08-28

Depot is on the `0.15.x-preview` development line. The current `finance` branch contains the integrated Finance platform: foundation/master data, immutable General Ledger, Receivables, Payables, FIFO Inventory Accounting, Banking and Payments, Financial Reporting, and effective-dated Localization.

## Finance capabilities

- **Finance Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger & Posting:** immutable balanced journals, reporting-currency snapshots, posting profiles, validation, idempotency, Audit evidence and linked reversals.
- **Accounts Receivable:** customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales integration.
- **Accounts Payable:** supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and Purchasing integration.
- **Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments:** bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **Financial Reporting:** Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension filtering, mappings, deterministic CSV and immutable report snapshots.
- **Finance Localization:** explicit effective-dated legal-entity assignments, built-in `GENERIC → EU → DE` references, custom pack extensibility, capability/configuration/procedure registry, RBAC and Audit evidence.

## Localization boundary

`LegalEntity.CountryCode` is a validation attribute, not an activation switch. A legal entity remains jurisdiction-neutral until an authorized Finance user assigns an effective root localization pack. Active assignments for the same entity cannot overlap.

Built-in `GENERIC`, `EU` and `DE` definitions and built-in registry rows are immutable. Custom regional/country packs can extend the hierarchy without another database schema change. Registry support levels describe technical capability and responsibility boundaries; they are not legal/compliance status flags.

## Versions

- Application: **0.15.42-preview**
- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Help manifest: **1.17**

`Directory.Build.props` is authoritative for the exact application patch. Every commit increments `DepotVersionPatch`.

## Validation boundary

Release Build, win-x64 publish, repository regression tests, Release Integrity, Security Supply Chain and Software Quality gates are required on the final integration head. Provider-neutral Finance DDL exists for SQLite, SQL Server and MySQL/MariaDB; live server migration, provider locking/concurrency/recovery, performance and organization-specific accounting/localization acceptance remain production gates.

## Next steps

The generic Finance platform has no additional mandatory feature milestone in the current roadmap. Remaining Finance work is production acceptance, deployment policy approval and demand-driven country/statutory extensions using the existing localization framework.
