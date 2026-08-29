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

The shell is permission-aware and workspace-oriented. Finance exposes **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting** and **Localization**. UI visibility improves usability only; service authorization is authoritative.

## Finance authority split

- `FinanceGeneralLedgerService` — immutable double-entry accounting truth and posting boundary.
- `FinanceAccountsReceivableService` — customer subledger/open-item/settlement truth.
- `FinanceAccountsPayableService` — supplier subledger/document/matching/settlement truth.
- `FinanceInventoryAccountingService` and costing services — FIFO valuation and inventory accounting evidence.
- `FinanceBankingService` — bank statements, payment-run orchestration, reconciliation and cash-position evidence.
- `FinanceFinancialReportingService` — reporting, mappings, exports and immutable report-snapshot boundary.
- `FinanceLocalizationService` — effective-dated localization assignment, pack hierarchy and capability/configuration/procedure references.
- Sales, Purchasing and Warehouse — operational source truth.

Subledgers/accounting modules call the General Ledger boundary for postings rather than duplicating ledger invariants. Reporting reads existing evidence and does not create a second ledger. Localization does not post accounting entries.

## Schema versions

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Application: **0.15.42-preview**
- Help manifest: **1.17**

Finance schema evolution is sequential: foundation (v1), General Ledger (v2), Receivables (v3), Payables (v4), inventory valuation (v5-v6), Banking (v7), Reporting (v8), Localization (v9).

## Transaction, concurrency and evidence model

Finance mutations use the existing transaction runner/database write transaction. Optimistic versions protect mutable configuration/workflow state. Operation IDs, request/content hashes and unique constraints protect retry-sensitive records. Required GL/subledger/valuation/banking/Audit effects commit or roll back together where they form one business transaction.

Finalized accounting and operational evidence is not silently rewritten. Corrections use reversals or new compensating/assessment evidence. Report snapshots and localization assignment/registry evidence are retained under the business-record classification model.

## Reporting and localization

GL-derived reports query persisted reporting-currency journal lines. AR/AP aging reads subledgers in transaction currency. Historical Inventory Valuation reconstructs valuation evidence. Cash Flow, Tax Summary and COGS use explicit account mappings rather than account-name/number heuristics.

Localization never activates from country alone. An effective root-pack assignment is explicit, country packs are validated against the Legal Entity and active assignment ranges cannot overlap. Built-in `GENERIC → EU → DE` references are immutable; custom packs can extend the model without another schema migration when metadata/configuration is sufficient.

## RBAC and segregation of duties

Service-layer permissions are authoritative. The Finance role receives normal Finance management rights; sensitive supplier/payment approvals remain independently controlled. Deployments can define stricter custom-role separation for configuration, posting, approval, reconciliation, reporting preparation and review.

## Provider acceptance

Finance schema 9 DDL/code exists for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not equivalent to production certification. Live migration, locking, deadlock/retry, recovery, backup/restore, date/decimal behavior and representative performance/concurrency acceptance remain required for every advertised server/version matrix.
