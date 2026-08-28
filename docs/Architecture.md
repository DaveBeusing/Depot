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

Depot uses a dark workspace-oriented shell with permission-aware primary modules and secondary pages. Current primary modules are Dashboard, Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, and Administration.

The shell supports closeable workspaces/tabs, stable routes, navigation history, Quick Open, Command Palette, F1 context Help, notifications, and unsaved-change guards. Module/page visibility is permission-aware for usability, but service authorization remains the security boundary.

Finance F1 is intentionally service/repository-first. A dedicated Finance workspace is deferred until source integrations can expose complete user workflows rather than partial accounting behavior.

## Presentation layer

Views contain layout, bindings, and presentation resources. ViewModels own presentation state, commands, selection, loading/error feedback, and cancellable user workflows.

Key rules:

- Views do not contain business logic or SQL.
- ViewModels call application/domain services.
- Services do not reference WPF Views or controls.
- Shared design-system resources live in `src/Depot/Resources`.
- Reusable WPF controls live in `src/Depot/Controls`.
- Long-running loads use cancellation and stale-request protection where applicable.

## Business/service layer

Services are the business and security boundary. They own validation, permissions, state transitions, transaction orchestration, and cross-repository invariants.

Major service groups include authentication/RBAC, item and supplier master data, inventory/warehouse workflows, purchasing, sales, Company/document identity, electronic-invoice finalization, Finance foundation and General Ledger posting, audit/privacy/notifications/reporting, database management, settings, and Help.

`FinanceGeneralLedgerService` is the authoritative F1 accounting posting boundary. Future AR/AP/inventory-accounting/banking services must call this service boundary rather than maintain a parallel mutable ledger or independently reproduce accounting invariants.

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
- **Finance feature schema:** **2** in `DepotFeatureVersions`
- **Application version:** independent SemVer (`0.15.2-preview` for this documentation synchronization)

`DatabaseComposition` initializes the core provider schema, then Sales feature migrations, then Finance feature migrations.

Finance schema v1 provides currencies, legal entities, tax registrations, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, dimensions/values, and number sequences.

Finance schema v2 adds provider-specific equivalents of:

- `FinanceJournalEntries`
- `FinanceJournalEntryLines`
- `FinanceJournalLineDimensions`
- `FinancePostingProfiles`
- `FinancePostingProfileLines`
- `FinanceJournalReversals`

Migration is sequential. New databases receive v1 and v2; existing Finance v1 databases receive the v2 extension. SQLite, SQL Server, and MySQL/MariaDB implementations exist. Live server migration, locking, deadlock/retry, recovery, and representative load acceptance remain production gates.

## Finance F0 — International Finance Foundation

F0 follows the normal Depot architecture rather than introducing a parallel accounting stack. It defines:

- `CurrencyCode` / `FinanceCurrency`
- `LegalEntity`
- `TaxRegistration`
- `ExchangeRate`
- `FiscalCalendar` / `AccountingPeriod`
- `ChartOfAccounts` / `FinanceAccount`
- `AccountingBook`
- `JournalDefinition`
- `AccountingDimension` / `AccountingDimensionValue`
- `FinanceNumberSequence`
- exchange-rate, tax-determination, and localization extension contracts

The generic Finance foundation does not infer Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or another jurisdiction/accounting-standard default.

## Finance F1 — General Ledger & Posting Engine

F1 adds immutable journal entries/lines and posting-profile orchestration.

### Posting invariants

Before persistence, the General Ledger service verifies that:

- an entry has at least two lines;
- each line has exactly one positive debit or credit amount;
- transaction-currency debit equals credit;
- accounts are active, directly postable, and belong to the accounting book's chart;
- all configured required dimensions are present with valid active values;
- the journal is active and belongs to the selected accounting book;
- the accounting period belongs to the same legal entity, is open, and contains the posting date;
- transaction amounts respect configured currency minor units;
- foreign-currency postings use an explicit persisted transaction→reporting exchange rate valid for the posting date;
- reporting-currency debit equals credit after configured minor-unit rounding.

Depot does not silently invent a rounding line/account. A caller must provide an explicit configured balancing treatment when required.

### Currency and historical snapshots

Every journal retains transaction and reporting currency. Cross-currency entries retain the used exchange-rate identity/rate snapshot, so later reference-data changes cannot reinterpret historical accounting evidence.

### Idempotency

Each posting has an operation ID and deterministic request fingerprint. Identical retries return the existing entry instead of consuming another number.

A second uniqueness boundary protects accounting-book/source-type/source-id/source-event. Reusing an operation or source identity with different accounting content is rejected.

### Posting profiles

Posting profiles map named business amount keys to configured debit/credit accounts, multiplier, journal, accounting book, and General Ledger number sequence. This keeps account numbers out of future Sales/AR, Purchasing/AP, Inventory Accounting, and Banking workflows.

Profiles use optimistic concurrency and are validated/audited transactionally.

### Number sequences

General Ledger numbers are allocated from an active Finance number sequence for the same legal entity. Allocation occurs inside the posting transaction and is rolled back when a later persistence or audit step fails.

### Reversals and immutability

Posted journal entries are never edited or deleted by the F1 workflow. `ReverseAsync` creates a new linked entry with the original transaction/reporting debit and credit amounts swapped exactly. The original remains intact, and a second reversal of the same original is rejected.

### Transactions and audit

Finance reuses the established transaction runner / `DatabaseAccess` infrastructure. Accounting persistence, number allocation, reversal linking, and Audit Log writes participate in the same transaction. An audit persistence failure therefore rolls back the accounting transaction rather than leaving an unaudited posting.

Unique constraints remain the final database race-safety boundary for operation IDs, source postings, entry numbers, posting-profile identities/lines, and reversal links.

## Authorization and segregation of duties

Effective permissions are database-backed and enforced at service boundaries.

F0 adds generic Finance permissions for Finance access, exchange rates, periods, accounting books, tax configuration, and number sequences.

F1 adds:

- `FinanceGeneralLedger.View`
- `FinanceGeneralLedger.Post`
- `FinanceGeneralLedger.Reverse`
- `FinancePostingProfiles.View`
- `FinancePostingProfiles.Manage`
- `FinanceManualJournals.Post`

The Finance system role receives controlled GL view/post/reversal and posting-profile permissions. The sensitive manual-journal permission is intentionally not assigned automatically. Administrator receives catalogued permissions through the normal RBAC model; deployments can build stricter custom-role separation.

## Audit, business records, and corrections

Depot treats finalized records as historical evidence. Corrections are separate transactions rather than destructive mutation.

Finance journal entries follow that same invariant. Journal creation and reversal write central Audit Log evidence atomically with their accounting mutation. Operation/source identifiers preserve traceability to later originating business workflows.

## Inventory and warehouse integrity

Stock remains movement-derived. Posted stock movements are immutable and corrected through counter-movements. Serial/lot identity is movement-derived through tracking allocations and respects exact-location availability, block/expiry state, and reversal identity.

Finance F1 does not alter this operational truth. Inventory valuation/COGS/GRNI integration belongs to F4 and must consume the F1 posting engine.

## Purchasing

Purchase Orders, approvals, goods receipts, and supplier returns remain operational purchasing records. Goods Receipts are not supplier invoices.

Accounts Payable supplier invoices/open items and purchase-order/goods-receipt/invoice matching belong to F3 and will use F1 posting profiles/ledger rather than reinterpret operational receipt facts.

## Sales and electronic invoicing

Sales contains customers, quotes, pricing, orders, reservations, shipments, invoices, credit notes, and returns. Posted Sales Invoices freeze seller/buyer identity and persist the issued XRechnung XML with integrity verification.

Finance F1 does not automatically post Sales documents to GL. F2 Accounts Receivable will add receivable open-item truth and connect Sales Invoice/Credit Note events to F1 posting profiles.

Electronic invoicing stays separate from generic Finance. XRechnung/EN 16931 behavior is not used as a generic accounting/tax engine.

## Company master and legal-entity boundary

`Administration > Company` remains the authoritative mutable legal seller/document identity for the current database. Finance `LegalEntity` is the generic accounting boundary.

F1 does not silently merge/reconstruct those concepts. Explicit mapping/integration must be introduced by a complete workflow package so historical document identity cannot fall back to mutable current master data.

## Privacy and retention

Finance records can contain financial, reference, tax, actor, and source identifiers and therefore inherit Depot's authorization, backup, retention, audit, and privacy controls. Retention duration and statutory export requirements remain deployment/jurisdiction-specific.

## Quality and provider acceptance

F1 regression coverage includes:

- balanced posting;
- operation/source idempotency;
- closed-period rejection;
- rollback when Audit Log persistence fails;
- posting-profile posting;
- explicit reversal behavior.

Live SQL Server/MySQL/MariaDB Finance migration/concurrency/recovery testing remains a production acceptance gate.

## Key architectural invariants

- Business/accounting rules live in services, not Views or repositories.
- Permissions are enforced by services even when UI elements are hidden.
- Finalized business/accounting records are not silently rewritten.
- Corrections are explicit transactions.
- Stock remains movement-derived.
- Critical multi-entity effects commit in one transaction.
- Provider-specific behavior stays behind provider/data-access abstractions.
- Generic Finance does not infer jurisdiction, currency, tax rate, chart, or accounting standard.
- General Ledger has one authoritative immutable double-entry truth.
- Future subledgers/source integrations consume F1 rather than creating a second ledger.
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
