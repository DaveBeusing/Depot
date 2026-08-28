# Depot Architecture

Updated: 2026-08-27

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

Depot uses a dark workspace-oriented shell with permission-aware primary modules and secondary pages. Main modules currently include:

- Dashboard
- Inventory
- Warehouse
- Purchasing
- Sales
- Approvals
- Reports
- Administration

The shell supports closeable document/workspace tabs, stable routes, navigation history, Quick Open, Command Palette, F1 context Help, notifications, and unsaved-change guards.

Module/page visibility is permission-aware for usability, but authorization is enforced again by services. UI visibility is never treated as a security boundary.

Finance F0 currently supplies domain/schema/RBAC foundations and Help content; it deliberately does not add a General Ledger posting workspace before the posting engine exists.

## Presentation layer

Views contain layout, bindings, and presentation resources. ViewModels own presentation state, commands, selection, loading/error feedback, and cancellable user workflows.

Key rules:

- Views do not contain business logic or SQL.
- ViewModels call application/domain services.
- Services do not reference WPF Views or controls.
- File-save/open and confirmation interactions use `IFileDialogService`.
- Shared design-system resources live in `src/Depot/Resources`.
- Reusable WPF controls live in `src/Depot/Controls`.
- Long-running loads use cancellation and stale-request protection where applicable.

The Sales workspace is split into section ViewModels for Overview, Quotes, Pricing, Customers, Sales Orders, Approvals, Shipping, and Invoices while sharing coordinated sales state where needed.

## Business/service layer

Services are the business and security boundary. They own validation, permissions, state transitions, transaction orchestration, and cross-repository invariants.

Major service groups include:

- authentication, session, users, roles, and RBAC;
- item/master-data and supplier management;
- inventory, stock movements, transfers, counts, issues, returns, and reversals;
- purchasing, approvals, goods receipts, and supplier returns;
- sales customers, pricing, quotes, orders, reservations, shipments, invoices, credit notes, and customer returns;
- Company legal/document identity;
- document generation, historical issuer snapshots, and Sales Invoice XRechnung finalization;
- Finance localization/tax/exchange-rate provider contracts and jurisdiction-neutral foundation models;
- audit, privacy-data discovery/export, notifications, reporting, backup/recovery, settings, and help.

Business services use optimistic concurrency and provider-neutral transaction runners for workflows that must commit atomically.

## Persistence layer

Repositories own SQL and row mapping. They use `DatabaseAccess` / transaction-session abstractions rather than constructing arbitrary provider connections inside business workflows.

`DatabaseAccess` and the connection-factory layer provide:

- SQLite, SQL Server, and MySQL/MariaDB provider implementations;
- parameter normalization and provider-specific generated-ID behavior;
- async query/command execution and cancellation;
- bounded paging/slicing/streaming paths;
- provider-controlled write transactions;
- provider-specific locking SQL where required;
- normalized connection/error handling without leaking credentials.

The application avoids application-wide mutable caches for business records. Small reference data may be bounded/cached where appropriate; transactional truth remains in the database.

## Database schemas and migrations

Depot has version concepts that must not be confused:

- **Core database schema:** currently version **29**.
- **Sales feature schema:** currently version **8** in `DepotFeatureVersions`.
- **Finance feature schema:** currently version **1** in `DepotFeatureVersions`.
- Application SemVer is independent from all database schema systems.

All advertised providers have schema creation/migration implementations for repository-supported structures. Live SQL Server/MySQL/MariaDB version matrices, migration/recovery drills, representative concurrency, and latency/load acceptance remain production-release gates.

`DatabaseComposition` initializes the core provider schema, then Sales feature migrations, then Finance feature migrations. Finance v1 creates provider-specific equivalents for currencies, legal entities, tax registrations, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, dimensions/values, and number sequences. It deliberately seeds no country, currency, tax rate, chart of accounts, accounting book, accounting standard, or legal entity.

### Item master-data extension

The enriched item master remains on the normal `View -> ViewModel -> Service -> Repository -> DatabaseAccess` path. `ItemService` owns normalization, permissions and cross-field validation; `ItemRepository` owns SQL and mapping.

`DatabaseProviderFactory` decorates each provider's normal initializer with the additive, idempotent `ItemMasterDataSchema` extension. The extension has explicit SQLite, SQL Server and MySQL/MariaDB definitions and adds the product-identification, lifecycle, trade/compliance and logistics fields without bypassing the shared data-access layer.

GTIN is validated in the service and protected by a provider-specific unique database index for concurrent/race-safe uniqueness. Physical values use an explicit unit contract: weights are persisted as kilograms and dimensions as millimetres. Activation/deactivation loads the complete master-data projection before audit persistence so audit evidence retains the full before/after record.

Item master-data classifications are interpreted as transaction rules only where a dedicated workflow explicitly implements the behavior. Current stock, purchasing and sales workflows implement the documented item type, tracking and lifecycle controls; legacy tracked opening-balance import remains fail-closed because it lacks allocation fields. See `docs/ITEM_MASTER_DATA.md` and `docs/ITEM_TRACEABILITY.md`.

## Finance foundation

Finance F0 follows the same Depot architecture rather than introducing a parallel accounting stack. Domain contracts are in `Models`, generic provider/localization contracts in `Services`, and the additive provider-neutral feature migration in `Data`.

F0 introduces `CurrencyCode`, `FinanceCurrency`, `LegalEntity`, `TaxRegistration`, `ExchangeRate`, `FiscalCalendar`, `AccountingPeriod`, `ChartOfAccounts`, `FinanceAccount`, `AccountingBook`, `JournalDefinition`, `AccountingDimension`, `AccountingDimensionValue`, and `FinanceNumberSequence`.

Structural validation covers required identifiers/codes, ISO-style country/currency syntax, positive exchange rates, same-currency rate consistency, valid date ranges, and number-sequence constraints. ISO-style validation is syntactic; authoritative code/reference-data validity remains deployment/reference-data responsibility.

`IExchangeRateSource`, `ITaxDeterminationService`, and `IFinanceLocalizationProvider` define extension boundaries. The generic foundation does not infer Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or another local rule/default. Accounting-standard identity is configuration data rather than a fixed enum.

F0 defines journal master data only. General Ledger entry/line persistence, double-entry balancing, posting profiles, source-document idempotency, period-lock enforcement, reversals, transactional audit and posting concurrency belong to F1. See `docs/FINANCE_ARCHITECTURE.md`.

## Authorization and identity

Core schema version 28 introduced database-backed RBAC through Roles, Permissions, RolePermissions, and UserRoles. Effective permissions are the union of active assigned roles and are enforced at service boundaries.

The protected Administrator role receives catalogued permissions through persisted role data rather than hidden authorization bypasses. Explicit business rules such as creator/approver separation remain separate from generic permissions.

Finance F0 adds dedicated `Finance.*`-derived permission codes for generic Finance access plus exchange rates, periods, accounting books, tax configuration and number sequences. The existing Finance system role receives them; Administrator inherits them through `PermissionCatalog.All`.

First-run databases use an administrator-bootstrap workflow rather than shared default credentials. Password policy, throttling, versioned PBKDF2-HMAC-SHA256 hashing, protected database settings, and encrypted remote transport are part of the security baseline.

## Audit, business records, and corrections

Depot treats posted/finalized records as historical evidence when the workflow requires it. Corrections are represented by explicit operations rather than destructive rewriting, for example:

- stock movement counter-movements;
- goods-receipt reversal;
- transfer/count reversal;
- material return/supplier return workflows;
- shipment/customer return workflows;
- Sales Credit Notes for posted Sales Invoices.

Reviewed retained workflows persist business state and audit evidence atomically. Audit entries capture actor, UTC timestamp, action/state transition, entity identity, and sanitized before/after data.

Finance F1 must apply the same evidence model to posted journal entries: no destructive rewrite, explicit reversals/corrections, transactional audit, source-document traceability and idempotency.

## Inventory and warehouse integrity

Stock is movement-derived rather than maintained as an independently mutable balance. Provider-specific locks and stable inventory ordering protect posting workflows from oversubscription and lock-order inversion.

Posted movements are immutable. Reversal creates a new counter-movement linked to the original where applicable. Transfers create paired out/in movements. Inventory counts preserve their starting snapshot and post only the required correction against movement-derived stock at posting time.

Serial/lot identity is also movement-derived through `ItemTrackingUnits` and `StockMovementTracking`; exact-location availability, block/expiry state and reversal identity are enforced in posting workflows.

## Purchasing

Purchase Orders use audited lifecycle transitions, explicit approvals, optimistic concurrency, and creator/approver separation. Goods Receipts are warehouse receipt facts, not supplier invoices.

Goods-receipt posting, purchase-order quantity/status effects, stock movements, and audit share one transaction. Supplier Returns preserve historical receipt facts and represent outbound corrections separately.

Critical workflows use the `WorkflowOperations` idempotency ledger where implemented so replay of a completed operation ID does not duplicate business effects.

Accounts Payable supplier-invoice accounting remains a later Finance package and must not reinterpret goods-receipt facts as invoices.

## Sales architecture

Sales contains Customers/Contacts/Addresses, Quotes, Pricing, Sales Orders, Reservations, Shipments, Customer Returns, Sales Invoices, and Credit Notes.

Commercial transaction values are snapshotted progressively:

- Sales Orders retain commercial customer/address/reference and line pricing/tax values used by later fulfillment.
- Shipments derive from posted/released order state and reservation/inventory data.
- Sales Invoices are created from posted shipments and retain their invoice billing-address snapshot and invoiced commercial lines.
- Posting a Sales Invoice is the final financial-document identity boundary described below.

Posted Sales Invoices are corrected with Credit Notes rather than editing the original invoice.

Finance F0 does not yet create an Accounts Receivable subledger or GL posting from these documents; those integrations belong to later Finance packages.

## Company master and document issuer identity

`Administration > Company` is the authoritative mutable legal seller/document profile for the current database. It contains structured legal, registration, tax, contact, banking, electronic-invoice, customs, and selected regulatory data.

`CompanyDocumentIdentityService` validates and projects that master data into a publication-safe `DocumentIssuerProfile`. Restricted/scenario-specific values such as IOSS or internal customs-account references are deliberately excluded from ordinary document identity.

Draft/current operational documents can use current Company master data. Posted financial documents do not.

`SalesDocumentIssuerSnapshots` stores one immutable issuer projection for each posted Sales Invoice or Sales Credit Note. A posted document whose historical issuer snapshot is missing fails closed rather than falling back to today's Company master.

Finance `LegalEntity` is a generic accounting boundary and does not silently replace or reconstruct existing Company document identity. Explicit integration/mapping belongs to later Finance workflows.

## Sales Invoice finalization

Sales Invoice posting is an atomic business-document finalization transaction. The service transaction includes, as applicable:

1. invoice quantity effects on Sales Order lines;
2. Draft → Posted invoice transition and posting user/time;
3. immutable seller `DocumentIssuerProfile` capture;
4. Buyer identity validation and `DocumentBuyerProfile` capture;
5. deterministic XRechnung-oriented UN/CEFACT CII generation;
6. persistence of the exact generated XML and SHA-256 fingerprint;
7. Sales Order completion transition where all conditions are met;
8. audit persistence.

If seller/Buyer identity, payment configuration, tax semantics, or XML generation is invalid, the transaction rolls back and the invoice remains unposted.

### Buyer identity

Customer master data includes structured electronic-invoice fields separate from display-oriented/free-form addresses:

- Buyer Reference (BT-10);
- electronic endpoint/address (BT-49) and scheme;
- Tax ID and VAT ID;
- structured billing street/address line/postal code/city/country;
- normal contact data.

The finalized Buyer record also keeps the invoice's existing free-form billing-address snapshot. Country code validation enforces two ASCII letters as ISO alpha-2 syntax; at least one Buyer tax identifier is required by the current finalization path.

### Exact issued XML and integrity

`SalesInvoiceFinalizations` has one row per Sales Invoice and stores:

- serialized immutable Buyer payload;
- exact XRechnung XML generated at posting;
- SHA-256 digest of the UTF-8 XML;
- finalization timestamp.

Loading or exporting a finalization recalculates and verifies the SHA-256 digest. The Invoice workspace exposes **Export XRechnung** only for posted invoices and exports the persisted verified XML through `SalesDocumentService`; it does not regenerate the document from current Company/Customer master data.

The digest is an application integrity/tamper-detection control, not a digital signature or independent authenticity/non-repudiation mechanism.

### Electronic-invoice boundary

The semantic/generator layer supports Invoice and Credit Note type codes, but the operational Buyer/XML finalization described above currently applies to Sales Invoices only. Posted Sales Credit Notes capture immutable issuer identity; equivalent Buyer/XML finalization remains follow-up work before electronic credit-note issuance is advertised.

The current commercial line model does not persist an explicit EN 16931 tax category plus exemption/reason semantics. Therefore Sales Invoice finalization accepts positive taxable rates and fails closed for zero-rated, exempt, or reverse-charge lines instead of guessing their category from a numeric `0%`.

Representative CII is validated in CI against pinned KoSIT/XRechnung assets. Runtime posting performs Depot application-level validation and does not execute the external KoSIT validator. Production recipient/channel configuration and validation of every advertised tax/profile/channel scenario remain release/deployment gates.

ZUGFeRD/Factur-X is not claimed; it requires a conforming PDF/A-3 container and end-to-end validation.

## Documents

`SalesDocumentService` generates human-readable PDFs and exposes persisted XRechnung XML export. Document responsibilities are intentionally separated:

- PDF generation resolves current or historical issuer identity according to document status.
- Posted invoice PDF regeneration requires the historical issuer snapshot.
- XRechnung export requires the historical `SalesInvoiceFinalizations` record and verifies its hash.
- No UI/ViewModel layer reconstructs seller/Buyer/XML data independently.

## Notifications

Core schema version 29 introduced the Notification Center through `Notifications` and `NotificationRecipients`.

Recipients are materialized from active RBAC assignments at event time. Notification navigation goes through controlled shell routes and repeats permission checks; possession of a notification never grants access to the referenced business record.

## Privacy and data protection

Administration > Privacy Data provides authorized discovery and machine-readable export of supported person-related data. Authentication hashes, connection credentials, protected configuration, and other secrets are excluded.

Electronic invoice finalization records and future Finance records may contain contact, tax, and financial information and therefore inherit Depot's authorization, backup, retention, audit, and privacy requirements.

Depot deliberately does not provide a universal destructive GDPR-delete operation because legal retention and lifecycle handling are record-specific.

## Database administration and recovery

`DatabaseManagementService` covers provider/schema status, backup/archive validation, restore with safety-backup behavior, scheduled backup retention, provider-specific integrity checks, and SQLite compaction.

SQLite recovery paths are automated where practical. Live SQL Server/MySQL/MariaDB recovery drills and provider/version acceptance remain production gates. Finance provider schemas require the same live-provider acceptance before being advertised as production-supported.

## Loading and large-data behavior

Productive list/report paths use bounded paging, aggregation, slices, or streaming rather than unbounded application reads. Search paths are server-side and cancellation-aware where applicable.

CI includes a 100,000-record SQLite performance baseline. Representative production sizing, network latency, concurrency, and external-provider performance remain release acceptance tasks.

## Quality and accessibility

Software-quality gates build with zero warnings and run regression suites on Windows Server 2022 and 2025. Static accessibility gates protect focus visibility, selected core contrast pairs, automation names, and textual/non-color status semantics.

Interactive keyboard, focus-order, Narrator/Accessibility Insights, DPI/scaling, visual-state, and real desktop/provider acceptance remain explicit 1.0 release gates.

## Key architectural invariants

- Business rules live in services, not Views or repositories.
- Permissions are enforced by services even when UI elements are hidden.
- Finalized business records are not silently rewritten.
- Historical financial identity never falls back to mutable current master data.
- Corrections are explicit business operations.
- Stock remains movement-derived.
- Critical multi-entity effects commit in one transaction.
- Provider-specific behavior stays behind provider/data-access abstractions.
- Electronic invoice XML exported for a posted invoice is the persisted issued representation, not a reconstruction.
- Generic Finance does not infer jurisdiction, currency, tax rate, chart of accounts or accounting standard.
- Future GL posting has one authoritative balanced double-entry truth and cannot maintain a parallel mutable balance.
- Technical compliance evidence is not described as legal certification.

## Related documentation

- `README.md`
- `docs/FINANCE_ARCHITECTURE.md`
- `docs/FINANCE_COMPLIANCE.md`
- `docs/ITEM_MASTER_DATA.md`
- `docs/ITEM_TRACEABILITY.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/DATA_ACCESS_AUDIT.md`
- `docs/NOTIFICATION_CENTER.md`
- `docs/HELP_CENTER.md`
- `docs/compliance/COMPANY_MASTER_DATA.md`
- `docs/compliance/ISSUER_SNAPSHOTS.md`
- `docs/compliance/ELECTRONIC_INVOICING.md`
- `docs/compliance/INVOICE_FINALIZATION.md`
