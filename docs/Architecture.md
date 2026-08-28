# Depot Architecture

Updated: 2026-08-28

## Overview

Depot is a .NET 10 Windows desktop application built with WPF, MVVM, service-layer business rules, provider-neutral repositories, and ADO.NET database abstractions.

```text
Presentation
  Views
    ↓ bindings/commands
  ViewModels
    ↓ application operations
Business
  Services
    ↓ persistence contracts / transactions
Persistence
  Repositories + DatabaseAccess
    ↓
  SQLite / SQL Server / MySQL or MariaDB
```

The composition classes under `src/Depot/Composition` create database infrastructure, repositories, services, and root ViewModels. Dependencies are passed explicitly; Views and ViewModels do not open database connections or contain SQL.

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware primary modules and secondary pages. Current primary modules are Dashboard, Inventory, Warehouse, Purchasing, Sales, Finance, Approvals, Reports, and Administration.

The shell supports closeable workspaces/tabs, stable routes, navigation history, Quick Open, Command Palette, F1 context Help, notifications, and unsaved-change guards. Module/page visibility is permission-aware for usability, but service authorization remains the security boundary.

Finance F2 introduces the first dedicated Finance workspace page: **Finance > Receivables**. General Ledger remains an accounting service boundary; Depot does not expose a separate free-form GL UI merely because F1 exists.

## Presentation layer

Views contain layout, bindings, and presentation resources. ViewModels own presentation state, commands, selection, loading/error feedback, and cancellable user workflows.

Key rules:

- Views do not contain business logic or SQL.
- ViewModels call application/domain services.
- Services do not reference WPF Views or controls.
- Shared design-system resources live in `src/Depot/Resources`.
- Reusable WPF controls live in `src/Depot/Controls`.
- Long-running loads use cancellation and stale-request protection where applicable.

`FinanceReceivablesViewModel` is a normal application ViewModel and calls `FinanceAccountsReceivableService`; it does not implement accounting rules or query the database directly.

## Business/service layer

Services are the business and security boundary. They own validation, permissions, state transitions, transaction orchestration, and cross-repository invariants.

Major service groups include authentication/RBAC, item and supplier master data, inventory/warehouse workflows, purchasing, sales, Company/document identity, electronic-invoice finalization, Finance foundation, General Ledger posting, Accounts Receivable, audit/privacy/notifications/reporting, database management, settings, and Help.

`FinanceGeneralLedgerService` is the authoritative accounting posting boundary. AR/AP/inventory-accounting/banking services must call this boundary rather than maintain a parallel mutable ledger or independently reproduce accounting invariants.

`FinanceAccountsReceivableService` is the F2 customer-subledger boundary. It owns receivable open-item, settlement, payment, allocation, write-off, aging, statement, and dunning rules and invokes the F1 GL posting/reversal boundary within the same transaction for accounting mutations.

## Persistence layer

Repositories own SQL and row mapping. They use `DatabaseAccess` / transaction-session abstractions rather than constructing arbitrary provider connections inside business workflows.

The data-access layer provides:

- SQLite, SQL Server, and MySQL/MariaDB provider implementations;
- parameter normalization and provider-specific generated-ID behavior;
- asynchronous query/command execution and cancellation;
- bounded paging/slicing/streaming paths;
- provider-controlled write transactions;
- provider-specific locking SQL where required;
- normalized connection/error handling without leaking credentials.

Transactional truth remains in the database. Application-wide mutable caches are not used as a second source of business/accounting truth.

## Database schemas and migrations

Depot has independent version concepts:

- **Core database schema:** **29**
- **Sales feature schema:** **8** in `DepotFeatureVersions`
- **Finance feature schema:** **3** in `DepotFeatureVersions`
- **Application version:** independent SemVer (`0.15.6-preview` for the completed F2 documentation baseline)

`DatabaseComposition` initializes the core provider schema, then Sales feature migrations, then the Finance migration chain.

Finance schema v1 provides currencies, legal entities, tax registrations, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, dimensions/values, and number sequences.

Finance schema v2 adds:

- `FinanceJournalEntries`
- `FinanceJournalEntryLines`
- `FinanceJournalLineDimensions`
- `FinancePostingProfiles`
- `FinancePostingProfileLines`
- `FinanceJournalReversals`

Finance schema v3 adds:

- `FinanceReceivablesConfigurations`
- `FinanceReceivableOpenItems`
- `FinanceReceivableAllocations`
- `FinanceReceivableAllocationOperations`
- `FinanceReceivablePayments`
- `FinanceReceivableWriteOffs`
- `FinanceDunningPolicies`
- `FinanceDunningPolicyLevels`
- `FinanceDunningRuns`
- `FinanceDunningRunLines`

F2 has an explicit dependency on the Sales feature schema because the customer subledger references the existing Customer master and consumes Sales Invoice/Credit Note source identities. `FinanceAccountsReceivableSchemaMigration` therefore ensures the current Sales schema before applying Finance v3. F0/F1 remain conceptually independent of Sales; the dependency begins with the AR integration package.

Migration remains additive and sequential. SQLite, SQL Server, and MySQL/MariaDB implementations exist. Live server migration, locking, deadlock/retry, recovery, and representative load acceptance remain production gates.

## Finance F0 — International Finance Foundation

F0 follows the normal Depot architecture rather than introducing a parallel accounting stack. It defines currencies, legal entities, tax registrations, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, number sequences, and exchange-rate/tax/localization extension contracts.

The generic Finance foundation does not infer Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or another jurisdiction/accounting-standard default.

## Finance F1 — General Ledger & Posting Engine

F1 adds immutable journal entries/lines and posting-profile orchestration.

Before persistence, the General Ledger service verifies balance, account/chart/direct-posting eligibility, required dimensions, book/journal/period relationships, period status/date, configured currency precision, exchange-rate identity/effective date, and reporting-currency balance after conversion.

Each posting has operation/source idempotency boundaries. Finance numbers are allocated inside the accounting transaction. Posted journals are not edited or deleted; correction creates a linked reversal entry. Journal persistence, number allocation, reversal links, and central Audit Log evidence commit atomically.

## Finance F2 — Accounts Receivable

F2 adds the customer subledger while preserving the F1 accounting authority.

### Configuration

An active `FinanceReceivablesConfiguration` maps one legal entity/fiscal calendar to four controlled posting profiles: Sales Invoice, Sales Credit Note, customer payment, and receivable write-off. The service verifies that all profiles are active, use the expected source type/event/amount keys, belong to the configured legal entity, and resolve to one accounting book.

Depot seeds no AR account, revenue account, tax account, bank account, write-off account, currency, legal entity, dunning policy, or jurisdiction rule.

### Sales integration and atomicity

When AR is actively configured, Sales Invoice and Credit Note posting invoke AR inside the **same** `DatabaseTransactionContext` as the Sales status/finalization mutation.

Invoice posting creates a controlled F1 journal and debit receivable open item. Credit-note posting creates its controlled journal and credit open item, then can allocate the credit against the original invoice open item. Any AR/GL validation, period/rate/account/dimension failure, number-allocation failure, persistence failure, or audit failure rolls the entire source transaction back.

When no AR configuration is active, existing Sales posting continues without AR/GL entries. This is deliberate fail-safe behavior: Depot never invents accounting configuration.

### Open-item and settlement model

Open items retain source type/id/reference, customer, legal entity, accounting book, document/due dates, currency, original amount, remaining amount, linked journal, operation id, version, and creation evidence.

Debit and credit settlement is represented by explicit `FinanceReceivableAllocation` records. Allocation requires the same customer, currency, book, and legal entity; amounts must be positive and cannot exceed available balances. Optimistic versions plus transaction locks/unique operation records protect concurrent settlement and retries.

### Payments and overpayments

A customer payment creates a controlled GL posting and payment credit open item. Requested allocations may settle invoices partially or fully. Excess/unallocated value remains as customer credit and can be allocated later.

Payment reversal uses F1's linked journal reversal and reverses **all active allocations from that payment credit**, including allocations created after the original payment operation. The payment open item is voided while original payment/allocation evidence remains retained.

### Write-offs

Write-offs reduce only active debit receivables, cannot exceed the remaining amount, and require a dedicated posting profile. Reversal creates a linked F1 reversal and restores the receivable balance. Write-off post/reverse permissions are intentionally not granted to the default Finance system role.

### Aging, statements, and dunning

Aging is derived from current outstanding invoice open items by due-date bucket and reports unapplied credits separately. Customer statement rows are derived from retained AR open-item evidence for customer/currency/date range.

Dunning policies use configurable levels/overdue thresholds. Dunning runs persist the evaluated open item, customer, currency, outstanding amount, days overdue, and selected level. Runs are idempotent by operation id/request content. F2 does not implement statutory fee/interest logic, legal-collection workflows, or jurisdiction-specific delivery evidence.

## Authorization and segregation of duties

Effective permissions are database-backed and enforced at service boundaries.

F0/F1 permissions remain in force for generic Finance and GL operations. F2 adds:

- `FinanceReceivables.View`
- `FinanceReceivables.Manage`
- `FinanceReceivablePayments.Post`
- `FinanceReceivablePayments.Reverse`
- `FinanceReceivableWriteOffs.Post`
- `FinanceReceivableWriteOffs.Reverse`
- `FinanceDunning.View`
- `FinanceDunning.Manage`

The Finance system role receives Receivables view/manage, customer payment post/reverse, and dunning rights. Sensitive write-off post/reverse rights remain withheld. `FinanceManualJournals.Post` also remains separate. Administrator receives all catalogued permissions through normal RBAC; deployments can enforce stricter custom-role separation.

## Audit, business records, and corrections

Depot treats finalized records as historical evidence. Corrections are separate transactions rather than destructive mutation.

Finance journals, receivable open items, payments, and write-offs are accounting-relevant retained records. Dunning runs are retained audit evidence. GL reversals, payment/allocation reversals, and write-off reversals preserve original evidence and write central Audit Log evidence transactionally.

## Inventory and warehouse integrity

Stock remains movement-derived. Posted stock movements are immutable and corrected through counter-movements. Serial/lot identity is movement-derived through tracking allocations and respects exact-location availability, block/expiry state, and reversal identity.

F2 does not change this operational truth. Inventory valuation/COGS/GRNI integration belongs to F4 and must consume the F1 posting engine.

## Purchasing

Purchase Orders, approvals, goods receipts, and supplier returns remain operational purchasing records. Goods Receipts are not supplier invoices.

Accounts Payable supplier invoices/open items and purchase-order/goods-receipt/invoice matching belong to F3 and will consume F1 rather than reinterpret operational receipt facts.

## Sales and electronic invoicing

Sales contains customers, quotes, pricing, orders, reservations, shipments, invoices, credit notes, and returns. Posted Sales Invoices freeze seller/buyer identity and persist issued XRechnung XML with integrity verification.

F2 now connects configured Sales Invoice/Credit Note posting to the customer subledger and F1 ledger atomically. Electronic invoicing remains separate from generic Finance: XRechnung/EN 16931 is not used as a generic accounting/tax engine.

## Company master and legal-entity boundary

`Administration > Company` remains the authoritative mutable legal seller/document identity for the current database. Finance `LegalEntity` is the generic accounting boundary.

F2 does not silently merge those concepts. AR configuration explicitly anchors accounting to a Finance legal entity while issued Sales documents retain their own immutable seller/buyer snapshots.

## Privacy and retention

Finance records can contain financial, reference, customer, tax, actor, source, and collection-state identifiers and therefore inherit Depot's authorization, backup, retention, audit, and privacy controls. Retention duration, statutory exports, reminder wording, and legal collection procedures remain deployment/jurisdiction-specific.

## Quality and provider acceptance

F2 regression coverage includes schema v3 migration, Sales-source idempotency and balanced GL linkage, overpayment/later allocation, payment reversal across active allocations, write-off authorization/reversal, aging, dunning idempotency, RBAC, and retained-record classification.

Acceptance distinguishes Finance-introduced failures from pre-existing repository test failures. Live SQL Server/MySQL/MariaDB Finance v3 migration/concurrency/recovery testing remains a production gate.

## Key architectural invariants

- Business/accounting rules live in services, not Views or repositories.
- Permissions are enforced by services even when UI elements are hidden.
- Finalized business/accounting records are not silently rewritten.
- Corrections are explicit transactions.
- Stock remains movement-derived.
- Critical source/subledger/ledger effects commit in one transaction.
- Provider-specific behavior stays behind provider/data-access abstractions.
- Generic Finance does not infer jurisdiction, currency, tax rate, chart, bank/write-off account, or accounting standard.
- General Ledger has one authoritative immutable double-entry truth.
- Accounts Receivable has one controlled open-item/settlement truth and consumes F1 for accounting entries.
- Future AP/inventory/banking integrations consume F1 rather than creating a second ledger.
- Technical compliance evidence is not described as legal certification.

## Related documentation

- `README.md`
- `docs/CURRENT_STATUS.md`
- `docs/FINANCE_ARCHITECTURE.md`
- `docs/FINANCE_COMPLIANCE.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/HELP_CENTER.md`
- `docs/compliance/COMPLIANCE_MATRIX.md`
