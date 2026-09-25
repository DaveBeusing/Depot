# Depot Architecture

Updated: 2026-09-19

## Overview

Depot is a .NET 10 WPF application using MVVM, service-layer business rules, repositories and a provider-neutral ADO.NET persistence layer.

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
               SQLite / SQL Server / MariaDB / MySQL
```

Composition classes create database infrastructure, repositories, services and root ViewModels. Views/ViewModels do not contain SQL. Services are the business/security boundary. Repositories own persistence/query SQL and row mapping. Provider-specific behavior remains behind established data-access abstractions.

## Database-provider architecture

Provider-neutral code is not itself a support claim. Production database support is controlled by the real-provider acceptance workflow and [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

Current certified baselines are the Depot-bundled SQLite runtime, SQL Server 2022 engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS. MariaDB and MySQL share `MySqlConnector` infrastructure but are accepted independently.

Remote provisioning serializes the complete global/feature migration sequence with provider-native locks: SQL Server uses `sp_getapplock`; MariaDB/MySQL use `GET_LOCK`. Write transactions are serializable on remote providers. Business serialization uses provider-specific row locking behind the data layer.

Known transient deadlock/write-conflict errors use bounded exponential retry with jitter and complete transaction recreation. Non-transient business/constraint failures are not retried. MySQL/MariaDB Finance UTC timestamps are normalized to real `DATETIME(6)` parameters at the provider boundary rather than leaking provider rules into Services/Repositories.

Sales schema 11 introduced the active reservation uniqueness invariant on every supported provider. SQLite and SQL Server use partial/filtered unique indexes; MariaDB/MySQL use an active generated inventory key plus a unique compound index. Subsequent Sales schemas build on that provider-parity baseline; the current Sales feature schema is 16.

## Authentication sessions, presence and policy enforcement

`AuthenticationService` remains the successful-login boundary and `AuthorizationService` remains the current identity/RBAC source. `SessionService` owns one persistent `UserSession` per successful login, a non-overlapping heartbeat, activity timestamping and client response to server-side termination. Failed logins never create sessions.

Presence is derived from `EndedUtc IS NULL` and heartbeat freshness. Runtime defaults are a 30-second heartbeat and 90-second presence timeout. Heartbeat writes only still-open sessions, so logout, expiration and revocation cannot be undone by a late liveness update.

The central `UserSessionPolicy` defaults to a 30-minute idle timeout and 12-hour absolute maximum session age. Finite concurrent-session limits serialize admission through the shared database and can reject a new session or supersede the oldest one.

Session administration separates `Users.View`, `Settings.Manage` and `UserSessions.Terminate`. User deactivation and revocation of that user's open sessions remain one database transaction.

## Security-event and Security Center architecture

Security observations are deliberately separate from session state and from the business Audit Log:

```text
AuthenticationService ─┐
                       ├→ SecurityEventService → SecurityEventRepository → SecurityEvents
Session Administration ┘                                      ↓
                                      provider-neutral DatabaseAccess
```

`SecurityEventService` is the operational security-event policy boundary. Security Events complement rather than replace Audit. Administrative session termination and session-policy changes can produce both Audit-relevant evidence and operational Security Events.

Security Event export projects immutable source evidence through a provider-neutral export contract. Durable delivery state is persisted separately from source `SecurityEvents`: configured targets retain filter identity, checkpoints, fixed in-flight snapshot bounds, retry/suspension state and a short worker lease. The checkpoint advances only after sink success, giving the delivery boundary explicit at-least-once semantics without mutating source events.

The current monitoring boundary intentionally excludes source IP, geolocation and device fingerprinting. Such signals require a separate privacy/security contract before they can become risk inputs.

See [User Sessions and Online Presence](UserSessions.md), [Security Center and Authentication Risk Monitoring](SecurityCenter.md) and [Security Event Export](SecurityEventExport.md).

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

## Sales CRM authority

The CRM layer sits immediately upstream of the existing Customer and Quote authorities:

```text
Lead → CustomerService + Opportunity
Opportunity → SalesQuoteService
             ↓
        existing Sales flow
```

`SalesCrmService` owns Lead, Opportunity, Stage and Activity business state, ownership rules, conversion orchestration and CRM authorization. `CustomerService` remains authoritative for Customer creation rules and `SalesQuoteService` remains authoritative for Quote creation. Pipeline amounts are operational CRM data and do not post accounting revenue. Bounded repository queries back pipeline summaries, My Work and search integrations.

## Costing and sales-pricing authority

```text
Explicit Base Cost source
(Preferred Supplier / Last Purchase / Manual Standard / Inventory Cost Reference)
        ↓
ItemCostCalculationService
        ↓ ordered Absolute / Percentage components
Calculated Item Cost
        ↓
PriceListGenerationService
        ↓ Percentage Markup + mandatory Preview
existing scoped SalesPriceList
        ↓
SalesPricingService
        ↓
Customer → Region → Global resolution
```

`ItemCostCalculationService` is the single item-cost formula. `PriceListGenerationService` consumes that calculation rather than reproducing it. `SalesPricingService` remains the single runtime price-resolution boundary and historical document lines retain source snapshots.

## Subscription and recurring-billing authority

Recurring sales are implemented inside the existing Sales authority rather than as a parallel invoicing stack:

```text
SubscriptionContract
    -> deterministic billing schedule / persisted due instance
        -> explicit authorized generation
            -> existing SalesInvoiceService draft
                -> existing posting / e-invoice / AR / GL path
```

`SubscriptionBillingService` owns contract lifecycle, deterministic schedule progression, pricing-policy evidence, due-instance idempotency and controlled draft generation. Fixed-price lines retain their contract snapshot; reprice-at-billing lines resolve through the existing `SalesPricingService`. A unique contract/period persistence boundary prevents duplicate billing instances, and invoice generation remains idempotent and draft-only. Business Attachments, My Work and contextual Help reuse their existing shared platform boundaries.

## Electronic-invoice artifact boundary

XRechnung CII remains the structured invoice authority. During invoice or credit-note finalization, Depot generates the XRechnung XML once, hashes it and persists the immutable finalization evidence. Sales schema 14 adds the hybrid-document boundary: `ZugferdFacturXService` uses that exact finalized XML and the same immutable electronic-invoice model to create a new PDF/A-3B document, embeds `xrechnung.xml`, records ZUGFeRD/Factur-X XMP metadata and persists the exact PDF bytes plus SHA-256 evidence in the same posting transaction. Later export verifies stored hashes and never regenerates the hybrid document from mutable master data.

## Document-template runtime and persistence

Document layout is a bounded presentation subsystem. Built-in version-1 templates remain deterministic code assets; database-backed versions are stored through `DocumentTemplateRepository` and Document Templates feature schema 1. `DocumentTemplateRuntimeCatalog` validates every loaded version, refreshes shared state from persistence, and exposes exactly one active version per document type.

The visual designer and productive Sales/warehouse PDF generation use the same runtime catalog. Bindings remain allowlisted and validated. Electronic-invoice finalization consumes the active validated invoice/credit-note template while creating the immutable hybrid artifact; later activation cannot alter retained PDF/A/XML evidence.

## Visual-designer interaction boundary

Productive visual designers share presentation infrastructure only where the interaction semantics are already identical. Posting Flow and Import Mapping reuse the common `DesignerValidationIssueTemplate` for severity/message presentation and accessibility metadata. Their selection models, validators, commands, services and persistence remain feature-owned.

Document-layout zoom, snap, resize, undo/redo and dirty-state behavior remain specific to the Document Designer because no second productive designer currently exposes the same editing semantics. Canvas coordinates and visual projections are not promoted into shared persistence merely to support UI reuse.

## Schema versions

- Core database schema: **30**
- Sales feature schema: **16**
- Finance feature schema: **12**
- User Sessions feature schema: **3**
- Security Events feature schema: **3**
- User Preferences feature schema: **2**
- Document Templates feature schema: **1**
- Enterprise Identity feature schema: **2**
- Procurement Sourcing feature schema: **1**
- Application: **0.15.x-preview**
- Help manifest: **1.34**

`Directory.Build.props` is authoritative for the exact application patch/version. Feature schema constants remain authoritative in their migration classes; this architecture document records the compatibility baselines rather than duplicating a moving preview patch.

Feature schemas evolve independently. Sales schema 11 remains the provider-parity/data-integrity correction; schema 12 introduced Advanced Pricing persistence, schema 13 adds bounded XRechnung finalization/evidence and schema 14 adds immutable ZUGFeRD/Factur-X hybrid artifact persistence and schema 15 adds the ERP-native CRM Lead/Opportunity/Activity persistence. None of these changes increments Core schema 30.

## Transaction, concurrency and evidence model

Mutable configuration uses optimistic versions. Session lifecycle uses repository predicates and lifecycle coordination to prevent late heartbeat resurrection. User deactivation plus session revocation is atomic. Security Event review uses expected `Version`; original security-event fields are append-only through normal application paths.

Bulk pricing Apply remains all-or-nothing through the provider transaction abstraction, with preview evidence revalidated before mutation. Provider retries are limited to known transient database conflicts so application/business failures cannot be repeated silently.

## RBAC and segregation of duties

Service-layer authorization is authoritative. UI visibility mirrors permissions but never replaces service authorization. Finance posting, payment, exception-approval and configuration responsibilities remain distinct according to their established permissions and deployment role design.

## Provider production acceptance

The full provider workflow exercises real database/runtime paths through provisioning, migrations, repositories and services. It covers SQL/type/constraint/date/decimal behavior, rollback, concurrency/deadlock/retry, Sales, Procurement, sessions, GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, remote restart, provider-native backup/restore and a representative 100k indexed lookup guard.

This closes the technical database-provider acceptance gate for the exact baselines in the support matrix. It does not claim jurisdiction-specific accounting/legal certification or replace deployment-specific sizing, backup operations, accessibility, signing or organization-control acceptance.

## Business-attachment boundary

Business Attachments are a cross-domain user-document subsystem behind `BusinessAttachmentService`, `BusinessAttachmentRepository` and the replaceable `IBusinessAttachmentContentStore`. V1 content is database-backed so supported database backup/restore procedures retain content with metadata. Allowlisted entity kinds and service-layer RBAC prevent arbitrary table access. Revisions are immutable; replacement creates a new retained revision and SHA-256 is verified when content is opened. Generated document archives and electronic-invoice evidence remain separate authorities.

See [Business Attachments](BusinessAttachments.md).

### Finance budgeting planning boundary

Finance Budgeting follows the standard `View → ViewModel → Service → Repository → DatabaseAccess` dependency direction. Budget data is planning evidence and does not become an alternate General Ledger. Actual-vs-Budget analysis delegates actual calculations to the existing Financial Reporting authority.


### SEPA payment-export boundary

SEPA payment-file generation follows the Finance layering: Banking UI/ViewModel -> `FinanceSepaPaymentExportService` -> Banking/SEPA repositories -> `DatabaseAccess`. The service owns eligibility, validation, deterministic identity, authorization, artifact hashing and lifecycle rules. Persistence retains exact XML and status history. External bank submission is outside the database transaction and is represented only as explicit evidence.


## Procurement sourcing authority

Procurement sourcing is a bounded purchasing feature built on the existing authority chain rather than a second purchasing stack:

```text
Purchase Requisition
  -> Approval Policy / requisition decision
    -> Request for Quotation
      -> Supplier Quote Responses
        -> deterministic comparison
          -> explicit quote selection
            -> PurchaseOrderService -> Purchase Order Draft
```

`ProcurementSourcingService` owns requisition/RFQ/quote lifecycle validation, authorization, deterministic comparison, award selection, idempotent conversion and sourcing Audit evidence. `ProcurementSourcingRepository` owns sourcing persistence and optimistic state mutation. `PurchaseOrderService` remains authoritative for Purchase Order creation and downstream approval/order behavior.

Procurement Sourcing feature schema **1** is tracked independently in `DepotFeatureVersions`; it does not change Core schema 30. The feature is provisioned through the normal database provisioning path and has provider acceptance coverage for SQLite, SQL Server, MariaDB and MySQL.

The resulting Purchase Order retains immutable sourcing evidence that identifies the originating Purchase Requisition, RFQ and selected Supplier Quote Response. Business Attachments can be associated with sourcing records, and awarded quote evidence is protected from destructive replacement.

## Inventory replenishment authority

`ReplenishmentService` owns policy validation, deterministic requirement calculation, suggestion lifecycle, authorization, optimistic concurrency and transactional conversion into Purchase Requisitions. `ReplenishmentRepository` owns set-based stock/demand/supply projections and feature persistence. Existing Inventory, Sales, Purchasing and SupplierItem stores remain source authorities.

The calculation is `ProjectedAvailable = OnHand - Reserved - Backordered + EligibleInbound`; at or below the reorder point, required quantity is `max(0, TargetStock - ProjectedAvailable)`, followed by explicit SupplierItem MOQ rounding. Ambiguous warehouse allocation or supplier evidence fails visibly as Blocked.

Feature schema **1** is tracked independently in `DepotFeatureVersions`. Suggestions are planning evidence and can only create Purchase Requisition demand through `ProcurementSourcingService`; no automatic supplier award or Purchase Order creation exists.



## Project and cost accounting boundary

Project Accounting follows the standard `View → ViewModel → Service → Repository → DatabaseAccess` dependency direction and owns an independent `ProjectAccounting` feature schema for project lifecycle, shallow phases, explicit source attribution and links to existing Finance budget lines.

It is not an alternate ledger. Posted General Ledger and subledger evidence remains immutable and authoritative; project actuals read that evidence, open purchasing commitments remain operational projections, and budget comparisons reuse Finance Budgeting amounts and periods. Project attribution adds analysis context without rewriting source accounting records.
