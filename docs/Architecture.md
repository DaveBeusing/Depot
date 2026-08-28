# Depot Architecture

Updated: 2026-08-28

## Overview

Depot is a .NET 10 WPF application using MVVM, service-layer business rules, repositories and a provider-neutral ADO.NET persistence layer.

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL-MariaDB
```

Composition classes create database infrastructure, repositories, services and root ViewModels. Views/ViewModels do not contain SQL. Services are the business/security boundary. Repositories own persistence/query SQL and row mapping. Provider-specific behavior remains behind established data-access abstractions.

## Application shell

The shell is permission-aware and workspace-oriented. Finance currently exposes:

- **Finance > Receivables**
- **Finance > Payables**
- **Finance > Inventory Accounting**
- **Finance > Banking**
- **Finance > Financial Reporting**

UI visibility improves usability only; service authorization is authoritative. Long-running workspace loads use the established cancellation/stale-request model where applicable, and Finance views use central WPF controls/design resources.

## Finance authority split

- `FinanceGeneralLedgerService` — immutable double-entry accounting truth and posting boundary.
- `FinanceAccountsReceivableService` — customer subledger/open-item/settlement truth.
- `FinanceAccountsPayableService` — supplier subledger/document/matching/settlement truth.
- `FinanceInventoryAccountingService` / costing services — FIFO valuation and inventory accounting evidence.
- `FinanceBankingService` — bank statements, payment-run orchestration, reconciliation and cash-position evidence.
- `FinanceFinancialReportingService` — read/reporting, mapping and immutable report-snapshot boundary.
- Sales, Purchasing and Warehouse — source operational truth.

Subledgers/accounting modules call the GL boundary for financial postings rather than duplicating ledger invariants. F6 Financial Reporting reads those existing records and does not create a second ledger.

## Schema versions

Independent current version levels are:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **8**
- Application: **0.15.36-preview**
- Help manifest: **1.15**

Finance migrations are sequential:

- v1 — F0 International Finance Foundation
- v2 — F1 General Ledger & Posting Engine
- v3 — F2 Accounts Receivable
- v4 — F3 Accounts Payable
- v5 — F4 Inventory valuation core
- v6 — F4 Inventory close/control extensions
- v7 — F5 Banking and Payments
- v8 — F6 Financial Reporting

## Transaction, concurrency and evidence model

Finance mutations use the existing transaction runner/database write transaction. Optimistic versions protect mutable configuration/workflow state. Operation IDs, request/content hashes and unique constraints protect retry-sensitive records. Required GL/subledger/valuation/banking/Audit effects commit or roll back together where they form one business transaction.

Finalized accounting/operational evidence is not silently rewritten. Corrections use reversals or new compensating/assessment evidence. F6 report snapshots are immutable AuditEvidence and bind parameters/content with SHA-256 hashes.

## Reporting architecture

GL-derived F6 reports query persisted F1 reporting-currency journal lines. AR/AP aging reads the F2/F3 subledgers in transaction currency. Historical Inventory Valuation reconstructs F4 evidence. Cash Flow, Tax Summary and COGS use explicit account mappings rather than name/number heuristics. Optional dimension filters query persisted F1 journal-line dimensions.

## RBAC and segregation of duties

Service-layer permissions are authoritative. The default Finance role receives operational Finance rights including F6 view/manage/export/snapshot creation; sensitive AP/payment approvals remain separately controlled. Deployments can define stricter custom-role separation for configuration, posting, approval, reconciliation, reporting preparation and review.

## Provider acceptance

Finance v8 DDL/code exists for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not equivalent to production certification. Live migration, locking, deadlock/retry, recovery, backup/restore, date/decimal behavior and representative performance/concurrency acceptance remain required for every advertised server/version matrix.

## Next Finance boundary

F0-F6 are implemented. **F7 — Localization Framework** is next and owns country/statutory extension infrastructure. Generic Finance does not claim jurisdiction-specific financial-statement, tax-return or filing certification.
