# Finance Architecture

Updated: 2026-08-28

## Purpose

Depot Finance is a jurisdiction-neutral accounting platform layer. Business modules create controlled financial consequences through Finance services; they do not maintain an independent accounting truth.

The current implementation baseline covers **F0 through F3**:

- F0 — International Finance Foundation
- F1 — General Ledger & Posting Engine
- F2 — Accounts Receivable
- F3 — Accounts Payable

The next package is **F4 — Inventory Accounting**.

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

Views contain presentation only. ViewModels own UI state, commands, cancellation and stale-request protection. Finance services own permissions, accounting invariants, transactions, idempotency and state transitions. Repositories own provider-neutral persistence contracts and row mapping. Provider-specific behavior remains behind the database-access/provider abstractions.

## Finance flow

```text
Business Processes
├── Sales
├── Purchasing
├── Inventory
├── Returns
└── Banking
      ↓
Finance Subledgers / Posting Services
├── Accounts Receivable
├── Accounts Payable
├── future Inventory Accounting
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

`FinanceGeneralLedgerService` is the authoritative accounting posting boundary. AR, AP, future inventory-accounting and banking workflows must use this boundary instead of introducing a second ledger.

## F0 — International Finance Foundation

F0 establishes configuration and identity without choosing a country-specific accounting policy:

- legal entities with explicit functional currency;
- currencies and minor units;
- sourced/effective exchange rates;
- fiscal calendars and accounting periods;
- charts of accounts and accounts;
- accounting books;
- journal definitions;
- accounting dimensions and values;
- tax registrations;
- Finance number sequences;
- exchange-rate, tax-determination and localization extension contracts.

Finance core contains no implicit Germany, EUR, VAT rate, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, bank account, revenue/expense account, AP/AR account or statutory workflow default.

## F1 — General Ledger & Posting Engine

F1 provides:

- immutable journal entries and journal lines;
- balanced double-entry validation in transaction and reporting currency;
- persisted FX snapshots;
- posting profiles mapping named amount keys to configured debit/credit accounts;
- accounting-book identity on every posting;
- open-period/date/legal-entity validation;
- active/direct-posting account and chart validation;
- required accounting dimensions;
- transactional number-sequence allocation;
- operation/source idempotency;
- explicit linked reversals;
- atomic Audit Log persistence.

Posted entries are not edited or deleted by business workflows. Corrections create new, linked evidence.

## F2 — Accounts Receivable

`FinanceAccountsReceivableService` is the customer-subledger boundary. It provides:

- Sales Invoice / Credit Note → AR → GL integration;
- receivable open items;
- partial/full customer payments;
- unapplied overpayments and later allocations;
- payment reversals restoring allocations;
- controlled write-offs and reversal;
- aging and customer statements;
- dunning policies and retained dunning runs;
- dedicated **Finance > Receivables** workspace.

When AR is configured, source posting, AR mutation, GL posting, number allocation and required audit evidence participate in one database transaction.

## F3 — Accounts Payable

`FinanceAccountsPayableService` is the supplier-subledger boundary. It provides:

- supplier invoice and supplier credit-note lifecycle;
- draft → pending approval → approved/rejected → posted/reversed states;
- AP open items with retained source and journal linkage;
- partial/full supplier payments;
- unapplied supplier debit balances and later allocation;
- payment reversals restoring every active allocation from the payment;
- aging and supplier statements;
- PO / goods-receipt / supplier-invoice matching;
- explicit match-exception evidence and authorization;
- dedicated **Finance > Payables** workspace;
- atomic AP → F1 GL posting/reversal.

### Supplier document accounting

Supplier documents store explicit document values. F3 does not calculate country-specific tax rules. The supplier-invoice posting profile decides which configured accounts receive `Gross`, `Net` and `Tax` amounts. Supplier credit notes use a separate configured profile. Supplier payments use a separate payment posting profile.

### Three-way matching

For an invoice line linked to a purchase-order line, F3 evaluates:

1. supplier consistency;
2. purchase-order unit price;
3. actually posted and non-reversed goods-receipt quantity;
4. quantity already represented by previously approved/posted invoices;
5. current invoiced quantity and price.

The generic core has **no implicit matching tolerance**. A line is matched only when the available received quantity is sufficient and the unit price equals the purchase-order price. Otherwise it is a match exception.

A match exception requires an explicit exception approval and the dedicated `FinanceSupplierMatchExceptions.Approve` permission. The exception reason is retained. Non-PO documents are supported and are marked as matching-not-required rather than being assigned an invented PO relationship.

### Approval and segregation of duties

Supplier document preparation/submission and approval are separated by permissions. The default Finance role receives operational AP creation/submission/posting/reversal/payment rights but does not receive `FinanceSupplierInvoices.Approve` or `FinanceSupplierMatchExceptions.Approve` automatically. Deployments must assign approval authority to a separate role/user population appropriate to their control framework.

### Settlement direction

- Supplier invoice: credit-direction AP open item.
- Supplier credit note: debit-direction AP open item.
- Supplier payment: debit-direction AP open item.

Allocations require the same supplier, currency, accounting book and legal entity and cannot exceed either available side.

### Reversal rules

A supplier payment reversal:

- creates a linked GL reversal;
- restores all active allocations originating from that payment;
- voids the payment open item;
- preserves original payment and allocation evidence.

A posted supplier document can be reversed only while its open item is completely unsettled. Settlement corrections must occur first. The original supplier document and original journal remain retained.

## Transaction and concurrency model

Finance mutations use the existing `IDatabaseTransactionRunner` / `DatabaseAccess` write-transaction boundary. SQLite uses its immediate-write semantics; SQL Server and MySQL/MariaDB use their provider-specific locking/transaction behavior. Optimistic versions protect mutable workflow state, while database uniqueness plus operation IDs protect retry-sensitive accounting operations.

No business service is allowed to partially commit a GL entry while its required subledger or audit mutation fails.

## Provider model

Finance feature schema **4** is implemented for:

- SQLite;
- SQL Server;
- MySQL/MariaDB.

Provider neutrality is a code/design property, not a production-certification claim. Live migration, locking, concurrency, backup/recovery and representative performance acceptance remain required for each supported server/version matrix.

## Schema versions

Current architecture baseline:

- Core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **4**

Finance migrations are ordered: F0 schema 1 → F1 schema 2 → F2 schema 3 → F3 schema 4.

## Security boundary

UI visibility is only usability. Service authorization is authoritative. F3 adds separate permissions for AP viewing/configuration, supplier-document create/submit/approve/post/reverse, match-exception approval and supplier-payment post/reverse.

Audit evidence, operation IDs, request hashes, immutable GL history and explicit reversals remain part of the accounting control model.

## F4 boundary

F3 deliberately does not implement inventory valuation, inventory-to-GL posting, landed cost, COGS, standard/actual cost, valuation layers or cost revaluation. Those belong to **F4 — Inventory Accounting** and must integrate through the same F1 General Ledger boundary.
