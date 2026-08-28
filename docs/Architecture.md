# Depot Architecture

Updated: 2026-08-28

## Overview

Depot is a .NET 10 WPF application using MVVM, service-layer business rules, repositories and a provider-neutral ADO.NET persistence layer.

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL-MariaDB
```

Composition classes create database infrastructure, repositories, services and root ViewModels. Views/ViewModels do not contain SQL. Services are the business/security boundary. Repositories own persistence SQL and row mapping. Provider-specific behavior remains behind established data-access abstractions.

## Application shell

The shell is permission-aware and workspace-oriented. Finance currently exposes two secondary pages:

- **Finance > Receivables** — `FinanceReceivables.View`
- **Finance > Payables** — `FinancePayables.View`

General Ledger remains an authoritative accounting service boundary rather than a free-form posting UI. UI visibility improves usability only; service authorization is authoritative.

Long-running loads use cancellation/stale-request protection where applicable. Finance Payables uses the normal shell, `AsyncRelayCommand` cancellation model, central controls and central design resources.

## Finance authority split

- `FinanceGeneralLedgerService` — immutable double-entry accounting truth.
- `FinanceAccountsReceivableService` — customer subledger/open-item/settlement truth.
- `FinanceAccountsPayableService` — supplier subledger/document/matching/settlement truth.
- Sales and Purchasing/Warehouse — source operational truth.

AR/AP call the GL boundary in the same database transaction for accounting mutations; they do not duplicate ledger invariants or persist a parallel ledger.

## Schema versions

Independent version levels are:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **4**
- Application version: **0.15.14-preview** at the F3 documentation baseline
- Help manifest: **1.12**

Finance migrations are sequential:

- v1 — F0 foundation
- v2 — F1 General Ledger/posting profiles/reversals
- v3 — F2 Accounts Receivable
- v4 — F3 Accounts Payable

F2/F3 consume existing Sales/Purchasing master/source data where their subledgers require it. Migrations and service composition make those dependencies explicit rather than relying on undocumented startup ordering.

## Finance F3 — Accounts Payable

F3 adds the supplier subledger while preserving the F1 accounting authority.

### Supplier documents

`FinanceSupplierDocument` supports supplier invoices and credit notes with draft, pending-approval, approved/rejected, posted and reversed states. Draft lines may reference Purchase Order and Goods Receipt lines. Posted documents retain approval, matching, source, posting-operation and journal evidence.

### Three-way matching

For PO-linked invoice lines, F3 evaluates supplier identity, PO unit price, non-reversed received quantity and previously invoiced quantity. Generic Finance has no implicit price/quantity tolerance. Mismatches become explicit `Match Exception` state.

Exception approval requires `FinanceSupplierMatchExceptions.Approve` and a retained reason. Non-PO documents are supported and remain matching-not-required rather than receiving invented purchasing evidence.

### AP posting and settlement

Configured supplier-invoice, supplier-credit-note and supplier-payment posting profiles determine GL accounts. A supplier invoice creates a credit AP open item; supplier credit notes and supplier payments create debit-direction AP items. Allocations require the same supplier, currency, accounting book and legal entity and cannot exceed available balances.

Supplier-payment reversal creates a linked F1 reversal, restores all active allocations from the payment and voids the payment open item while retaining original evidence. A posted supplier document can be reversed only while its AP open item remains completely unsettled.

### Transaction and concurrency model

Finance mutations use the existing transaction runner/database write transaction. Optimistic versions protect mutable workflow state. Operation IDs/request hashes and unique constraints protect retry-sensitive operations. Required GL, AP and Audit effects commit or roll back together.

## RBAC and segregation of duties

F3 permissions include AP view/manage, supplier-document create/submit/approve/post/reverse, match-exception approve and supplier-payment post/reverse. The default Finance role receives operational AP rights but does not receive supplier-document approval or match-exception approval automatically.

Deployments remain responsible for assigning custom roles that satisfy their segregation-of-duties policy.

## Business-record integrity

Finalized accounting/operational evidence is not silently rewritten. Supplier documents, AP open items and supplier payments are classified as retained accounting-relevant records. Corrections use explicit reversal/allocation transactions and linked General Ledger reversals.

## Provider acceptance

Finance v4 DDL exists for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not equivalent to production certification. Live migration, locking, deadlock/retry, recovery, backup/restore and representative load/concurrency acceptance remain required for every advertised server/version matrix.

## F4 boundary

Inventory valuation, COGS, GRNI, landed cost, valuation layers and inventory-to-GL accounting remain outside F3. **F4 — Inventory Accounting** must consume the existing F1 General Ledger boundary and preserve the same transaction/audit/idempotency rules.
