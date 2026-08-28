# Finance Financial Reporting (F6)

Updated: 2026-08-28

## Scope

F6 adds a configurable financial-reporting layer on top of the existing Finance packages. It does not introduce another posting engine or ledger. The immutable F1 General Ledger remains authoritative for GL-derived reporting.

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

GL-derived reports use persisted F1 `ReportingDebit` / `ReportingCredit` and therefore preserve the exchange-rate snapshot that existed when the journal was posted.

AR/AP aging reads the F2/F3 open-item subledgers and shows each open item's transaction currency. F6 does not silently convert those aging balances using a current or guessed historical exchange rate.

Inventory Valuation reconstructs F4 valuation evidence at the requested cutoff. Cash Flow uses F1 journal lines plus explicit reporting mappings. Banking evidence remains owned by F5.

## Account mappings

Each accounting-book account may have one `FinanceReportingAccountMapping`. The mapping can define:

- Balance Sheet / P&L section;
- Operating / Investing / Financing cash-flow category;
- tax-report category;
- whether the account itself is a cash account;
- whether the account is a COGS account;
- display sort order and active state.

F6 validates mapping compatibility with the account type. Cash-flow and tax classification are never inferred from account names or numbers.

## Dimension filters

Where a report is GL-line based, users may filter by one configured accounting dimension and dimension value. Both IDs must be provided together. F6 queries the persisted `FinanceJournalLineDimensions`; it does not add derived dimension state to subledgers.

## Report snapshots

A report can be retained as `FinanceReportSnapshot`. The snapshot stores report parameters, accounting book/date/dimension scope, canonical CSV, creator/time and SHA-256 hashes of parameters and content.

Snapshot creation is operation-idempotent. Retrying the same operation ID with identical content returns the existing snapshot. Reusing the operation ID for different content is rejected. Historical snapshots are never edited and are classified as `AuditEvidence`.

## Export

CSV export is deterministic and culture-invariant. Export permission is separate from view/manage permission. A CSV or snapshot is accounting evidence, not a jurisdiction-specific statutory filing format.

## RBAC

F6 service boundaries enforce:

- `FinanceFinancialReporting.View`
- `FinanceFinancialReporting.Manage`
- `FinanceFinancialReporting.Export`
- `FinanceReportSnapshots.Create`

UI visibility is not an authorization boundary.

## Provider and validation boundary

F6 schema version **8** is implemented for SQLite, SQL Server and MySQL/MariaDB. Automated regression coverage verifies SQLite migration/schema, F1 reporting-currency/cutoff behavior, explicit cash-flow mapping, RBAC, snapshot retention/idempotency/content binding and deterministic CSV.

Live SQL Server/MySQL-MariaDB migration, provider concurrency/recovery and representative production report-load testing remain acceptance activities.

## Compliance boundary

F6 provides configurable accounting reports and retained evidence. It does not claim HGB/IFRS/US-GAAP certification, tax-return correctness, GoBD certification, statutory financial-statement layout conformance or jurisdiction-specific filing acceptance. Those require organization policy, external review and/or F7 localization packs.
