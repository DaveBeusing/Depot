# Finance Financial Reporting

Updated: 2026-08-28

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

## Provider and compliance boundary

Finance reporting schema is part of Finance schema **9** and has DDL for SQLite, SQL Server and MySQL/MariaDB. Automated regression coverage verifies migration/schema, reporting-currency/cutoff behavior, explicit mapping, RBAC, snapshot retention/idempotency/content binding and deterministic CSV.

Live provider migration/concurrency/recovery and representative production report-load testing remain acceptance activities. Financial reports and snapshots do not by themselves claim HGB/IFRS/US-GAAP certification, tax-return correctness, GoBD certification or jurisdiction-specific filing acceptance.
