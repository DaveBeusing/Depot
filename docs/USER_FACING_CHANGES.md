# User-facing changes

Updated: 2026-08-28

Depot's current `0.15.x-preview` line includes the completed **Finance F0-F6** baseline.

## Finance workspaces

Finance now provides permission-aware workspaces for **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, and **Financial Reporting**. All financial posting consequences still flow through the existing immutable General Ledger boundary.

## F6 — Financial Reporting

Users with the corresponding permissions can now:

- generate Trial Balance and General Ledger detail;
- produce Balance Sheet and Profit & Loss views;
- create Cash Flow reports from explicitly classified cash/counterpart accounts;
- view Accounts Receivable and Accounts Payable aging;
- generate Tax Summary, historical Inventory Valuation and COGS reports;
- filter GL-derived reports by a persisted accounting dimension/value pair;
- configure per-account reporting classifications for statement sections, cash flow, tax, cash-account identity and COGS;
- export deterministic CSV;
- retain immutable report snapshots bound to their parameters/content with SHA-256 hashes.

GL-derived reports use persisted reporting-currency values from F1. AR/AP aging remains in each open item's transaction currency rather than silently applying a current or guessed historical exchange rate.

## Permissions

F6 adds separate permissions for financial-report viewing, mapping management, CSV export and report-snapshot creation. Service-layer authorization remains authoritative regardless of UI visibility.

## Evidence and scope limits

Report snapshots preserve canonical CSV, parameters, user/time evidence and hashes. They are retained AuditEvidence and cannot be edited into a different historical result.

F6 does not certify report layouts for HGB, IFRS, US-GAAP, GoBD, tax returns or other jurisdiction-specific filings. Country-specific statutory presentation and filing behavior remains future F7 localization/compliance scope.

## Current technical baseline

- Application: **0.15.34-preview**
- Finance schema: **8**
- Help manifest: **1.15**
- Provider-neutral schema/code: SQLite, SQL Server and MySQL/MariaDB

Live remote-provider migration/concurrency/recovery/performance acceptance remains required before production-provider support claims.
