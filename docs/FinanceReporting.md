# Finance Financial Reporting

Updated: 2026-09-08

## Scope

Financial Reporting is a configurable reporting layer on top of existing Finance accounting evidence. It does not introduce another posting engine or ledger. The immutable General Ledger remains authoritative for GL-derived reporting.

Implemented reports:

- Trial Balance
- General Ledger detail
- Balance Sheet
- Profit & Loss
- Cash Flow
- Accounts Receivable Aging
- Accounts Payable Aging
- Tax Summary
- Inventory Valuation
- Cost of Goods Sold

## Data sources

GL-derived reports use persisted `ReportingDebit` / `ReportingCredit` and preserve the exchange-rate snapshot that existed when the journal was posted. AR/AP Aging reads open-item subledgers and shows each open item's transaction currency; it does not silently convert balances using a current or guessed historical exchange rate.

Inventory Valuation reconstructs historical valuation evidence at the requested cutoff. Cash Flow uses journal lines plus explicit reporting mappings. Banking evidence remains owned by the Banking service.

## Account mappings

Each accounting-book account may have one `FinanceReportingAccountMapping` defining financial-statement section, cash-flow category, tax-report category, cash-account flag, COGS flag, display sort order and active state. Mapping compatibility is validated against account type. Accounting meaning is never inferred from account names or numbers.

## Dimension filters

GL-line-based reports can filter by one configured accounting dimension and dimension value. Both IDs must be supplied together. Queries use persisted journal-line dimensions and do not create derived subledger dimension state.

## Report snapshots and export

`FinanceReportSnapshot` stores report parameters, accounting-book/date/dimension scope, canonical CSV, creator/time and SHA-256 hashes of parameters/content. Snapshot creation is operation-idempotent; reusing an operation ID for different content is rejected. Snapshots are immutable `AuditEvidence`.

CSV export is deterministic and culture-invariant. Export permission is separate from view/manage permission. A CSV or snapshot is accounting evidence, not a jurisdiction-specific statutory filing format.

## RBAC

- `FinanceFinancialReporting.View`
- `FinanceFinancialReporting.Manage`
- `FinanceFinancialReporting.Export`
- `FinanceReportSnapshots.Create`

UI visibility is not an authorization boundary.

## Provider production acceptance

Financial Reporting remains part of Finance schema **9**. Real-provider acceptance now runs on the certified SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 baselines.

The service-backed live scenario generates a Trial Balance from a real General Ledger entry, validates balanced debit/credit results, exports deterministic CSV, creates an immutable SHA-256-bound report snapshot, verifies operation-idempotent snapshot replay and reloads the persisted snapshot. The provider matrix additionally covers Finance GL/AR/AP/FIFO flows, restart/re-entry, native remote backup/restore and a representative 100,000-row indexed performance guard.

This technical provider certification does not make report mappings, layouts or outputs HGB/IFRS/US-GAAP certified and does not prove tax-return, statutory-filing or organization-specific reporting correctness. Large deployment-specific report/export volumes and accounting mappings still require sizing and qualified acceptance.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for exact provider baselines.
