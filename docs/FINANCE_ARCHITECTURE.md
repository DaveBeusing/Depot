# Finance Architecture

Updated: 2026-08-28

## Purpose

Depot Finance is a jurisdiction-neutral accounting platform layer. Business modules create controlled financial consequences through Finance services; they do not maintain an independent accounting truth.

The current implementation baseline covers **F0 through F4**:

- F0 — International Finance Foundation
- F1 — General Ledger & Posting Engine
- F2 — Accounts Receivable
- F3 — Accounts Payable
- F4 — Inventory Accounting

The next package is **F5 — Banking and Payments**.

## Core architectural rule

```text
Views
  ↓
ViewModels
  ↓
Services
  ↓
Repositories
  ↓
DatabaseAccess
```

Views contain presentation only. ViewModels own UI state, commands and cancellation. Finance services own permissions, accounting invariants, transactions, idempotency and state transitions. Repositories own provider-neutral persistence contracts and row mapping. Provider-specific behavior remains behind database/provider abstractions.

## Finance flow

```text
Business Processes
├── Sales
├── Purchasing
├── Inventory / Warehouse
├── Returns
└── future Banking
      ↓
Finance Subledgers / Accounting Services
├── Accounts Receivable
├── Accounts Payable
├── Inventory Accounting
└── future Banking
      ↓
FinanceGeneralLedgerService
├── Posting Profiles
├── Currency / FX snapshots
├── Period validation
├── Accounting dimensions
├── Number sequences
└── Reversal / idempotency controls
      ↓
Immutable General Ledger
      ↓
Localization / Compliance Packs
```

`FinanceGeneralLedgerService` remains the authoritative accounting posting boundary. AR, AP and Inventory Accounting use this boundary instead of introducing parallel ledgers.

## F0 — International Finance Foundation

F0 establishes configuration and identity without choosing a country-specific accounting policy: legal entities, currencies, FX rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations, number sequences and extension contracts.

Finance core contains no implicit Germany, EUR, VAT rate, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, bank account, revenue/expense account, AP/AR account or statutory workflow default.

## F1 — General Ledger & Posting Engine

F1 provides immutable balanced journal entries, reporting-currency snapshots, posting profiles, open-period/account/dimension validation, transactional number allocation, operation/source idempotency, linked reversals and atomic Audit evidence.

Posted entries are not edited or deleted by business workflows. Corrections create new linked evidence.

## F2 — Accounts Receivable

`FinanceAccountsReceivableService` is the customer-subledger boundary: Sales Invoice/Credit Note integration, open items, payments/allocations, overpayments, write-offs, aging/statements and dunning. When AR is active, Sales source mutation, AR and GL commit/rollback together.

## F3 — Accounts Payable

`FinanceAccountsPayableService` is the supplier-subledger boundary: supplier document lifecycle, AP open items, payments/allocations, aging/statements, PO/goods-receipt/invoice matching and explicit match-exception authority. AP uses configured F1 posting profiles and explicit reversals.

## F4 — Inventory Accounting

F4 adds two cooperating service boundaries without duplicating F1 ledger logic:

- `FinanceInventoryAccountingService` owns physical stock-movement valuation and its GL consequence.
- `FinanceInventoryCostingService` owns close/control policy, inventory adjustments, purchase variances, landed cost and reconciliation.
- `FinanceInventoryMovementAccountingService` provides controlled idempotent processing of inventory-count correction/reversal movements by retained count reference.

### Base configuration

`FinanceInventoryAccountingConfiguration` binds:

- legal entity;
- fiscal calendar;
- purchase-order valuation currency;
- valuation method;
- goods-receipt posting profile;
- sales-issue/COGS posting profile;
- active state.

Only **FIFO** is currently implemented. Other costing methods must be added explicitly rather than inferred.

### F4 policy

`FinanceInventoryAccountingPolicy` binds the accounting book indirectly through validated posting profiles and configures:

- inventory-control account used for reconciliation;
- inventory-adjustment posting profile;
- purchase-variance posting profile;
- landed-cost posting profile;
- active state.

All profiles must belong to the same configured accounting book/legal entity. Required amount keys are validated before policy activation.

### Goods Receipt / GRNI

For an active F4 configuration, posted `Purchase` stock movements create a FIFO valuation layer and a configured F1 posting using the PO unit cost. The operational goods receipt, stock movement, valuation layer, journal and Audit evidence participate in the same database transaction.

Receipt reversal creates a linked GL reversal and marks the valuation layer reversed only when the layer is still completely unconsumed. If downstream valued issues have consumed it, F4 fails closed and requires those downstream effects to be reversed first.

### Sales issue / COGS

A posted sales shipment consumes available FIFO layers oldest-first. Layer updates, consumption evidence, COGS/inventory posting and operational shipment posting share the existing transaction boundary. F4 refuses negative valued inventory when sufficient valued quantity does not exist.

Shipment reversal restores the exact recorded layer consumptions and creates a linked F1 GL reversal.

### Inventory adjustments

Inventory-count correction movements can be valued through F4:

- negative corrections consume FIFO layers and post the cost through adjustment debit/credit amount keys;
- positive corrections create a new layer using the current valued FIFO average and fail closed when no defensible valued basis exists;
- reversals restore or remove the exact valuation effect and reverse the GL posting.

Processing is idempotent by immutable stock-movement identity. The dedicated Finance workspace can process a retained inventory-count reference to catch up previously unvalued correction/reversal movements without rewriting Warehouse history.

### Purchase-price variance

For a posted supplier document linked to PO lines, F4 compares the expected PO net amount represented by invoiced quantities with the posted supplier-document net amount. A non-zero signed variance is posted through configured `VarianceDebit` / `VarianceCredit` amount keys and retained as `FinanceInventoryPurchaseVariance`.

The generic F4 core does not invent tolerances. AP matching remains the F3 approval boundary; F4 variance accounting records the financial consequence after the source document is posted. Reversal uses a linked GL reversal and retained original variance evidence.

### Landed cost

Landed cost may be allocated to selected, fully unconsumed valuation layers by:

- quantity; or
- existing layer value.

Allocation changes unit cost and posts the configured landed-cost GL consequence atomically. Cross-currency layer allocation is rejected. Landed cost can be reversed only while every affected layer remains unconsumed and unreversed.

### Historical as-of valuation

F4 reconciliation does not use current remaining quantities as a proxy for prior dates. It reconstructs each layer at the requested cutoff from:

- acquisition date;
- original quantity;
- consumptions created on/before cutoff;
- consumption reversals occurring after/before cutoff;
- layer reversal timing;
- landed-cost posting/reversal timing;
- persisted receipt and landed-cost FX snapshots.

This prevents later inventory activity from silently rewriting a historical period-end valuation.

### Period-end reconciliation

`FinanceInventoryCostingService.ReconcileAsync` calculates inventory valuation in accounting-book reporting currency and compares it to the configured inventory-control account balance in F1 GL as of the same date. It persists:

- operation ID;
- accounting book/control account;
- as-of date and reporting currency;
- valuation amount;
- GL amount;
- difference;
- immutable per-item quantity/value lines;
- user/time Audit evidence.

Reconciliation runs are snapshots. A later assessment creates a new run; prior close evidence is not updated.

## Transaction and concurrency model

Finance mutations use `IDatabaseTransactionRunner` / `DatabaseAccess`. A non-generic transaction-runner overload supports command-style transactions while still delegating to the same provider-controlled generic boundary.

SQLite uses its immediate-write semantics; SQL Server and MySQL/MariaDB use provider-specific locking/transaction behavior. Optimistic versions protect mutable configuration/policy state, while uniqueness and operation IDs protect retry-sensitive accounting records.

No business workflow is allowed to partially commit a required GL posting while its valuation/subledger/audit mutation fails.

## Provider model

Finance feature schema **6** is implemented for SQLite, SQL Server and MySQL/MariaDB. Provider neutrality is a code/design property, not a production-certification claim. Live migration, locking, concurrency, backup/recovery and performance acceptance remain required per supported deployment matrix.

## Schema versions

Current architecture baseline:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **6**

Finance migrations are ordered: F0 schema 1 → F1 schema 2 → F2 schema 3 → F3 schema 4 → F4 valuation core schema 5 → F4 close/control schema 6.

## Security boundary

UI visibility is usability only; service authorization is authoritative. F4 uses `FinanceInventoryAccounting.View` and `FinanceInventoryAccounting.Manage`. The default Finance system role receives both. Administrator retains the complete permission catalog.

Audit evidence, operation IDs, immutable GL history, explicit reversals and retained reconciliation snapshots remain part of the accounting control model.

## F5 boundary

F4 deliberately does not implement bank account masters, bank statement imports, payment proposal/execution, bank reconciliation or cash position. Those belong to **F5 — Banking and Payments** and must integrate with the existing AR/AP/F1 boundaries rather than create a parallel settlement ledger.
