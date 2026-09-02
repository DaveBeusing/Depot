# Depot Architecture

Updated: 2026-09-01

## Overview

Depot is a .NET 10 WPF application using MVVM, service-layer business rules, repositories and a provider-neutral ADO.NET persistence layer.

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL-MariaDB
```

Composition classes create database infrastructure, repositories, services and root ViewModels. Views/ViewModels do not contain SQL. Services are the business/security boundary. Repositories own persistence/query SQL and row mapping. Provider-specific behavior remains behind established data-access abstractions.

## Authentication sessions, presence and policy enforcement

`AuthenticationService` remains the successful-login boundary and `AuthorizationService` remains the current identity/RBAC source. `SessionService` owns one persistent `UserSession` per successful login, a non-overlapping heartbeat, activity timestamping and client response to server-side termination. Failed logins never create sessions.

Presence is derived from `EndedUtc IS NULL` and heartbeat freshness. Runtime defaults are a 30-second heartbeat and 90-second presence timeout. Heartbeat writes only still-open sessions, so logout, expiration and revocation cannot be undone by a late liveness update.

The central `UserSessionPolicy` defaults to a 30-minute idle timeout and 12-hour absolute maximum session age. Input activity inside Depot updates only a throttled timestamp, persisted with the normal heartbeat before policy evaluation. Expiration records `Expired`; stricter policy changes evaluate already-open sessions immediately.

Session administration separates `Users.View`, `Settings.Manage` and `UserSessions.Terminate`. User deactivation and revocation of that user's open sessions remain one database transaction.

## Security-event and Security Center architecture

Security observations are deliberately separate from session state and from the business Audit Log:

```text
AuthenticationService ─┐
                       ├→ SecurityEventService → SecurityEventRepository → SecurityEvents
Session Administration ┘                                      ↓
                                                SQLite / SQL Server / MySQL-MariaDB
```

Authentication uses deterministic signals from the existing 15-minute throttling window. Failures 1–2 are informational; failure 3 becomes Warning, failure 4 High, and failure 5/active lockout Critical. Successful authentication after recent failures is retained as a separate event. These rules are triage signals, not proof of compromise.

`SecurityEventService` is the security-event policy boundary. Authentication telemetry is best-effort: failures to persist telemetry or deliver a notification are diagnosed but do not make a valid authentication fail solely because monitoring infrastructure was unavailable.

High/Critical events are published through the existing Notification Center to active users holding `SecurityEvents.View`. **Administration → Security Center** reads the same persisted event source and exposes 24-hour metrics plus the most recent matching events. `SecurityEvents.Manage` permits marking an event reviewed. Review modifies only review metadata and optimistic `Version`; original event content remains unchanged through normal application workflows.

Security Events complement rather than replace Audit. Administrative session termination and session-policy changes can produce both the existing Audit-relevant evidence and an operational Security Event.

The current monitoring boundary intentionally excludes source IP, geolocation and device fingerprinting. Such signals require a separate privacy/security contract before they can become risk inputs.

See [User Sessions and Online Presence](UserSessions.md) and [Security Center and Authentication Risk Monitoring](SecurityCenter.md).

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

## Costing and sales-pricing authority

```text
Preferred supplier purchase price
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

## Schema versions

- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- User Sessions feature schema: **2**
- Security Events feature schema: **1**
- Application: **0.15.94-preview**
- Help manifest: **1.21**

Feature schemas evolve independently. User Sessions schema 2 contains the central lifetime policy. Security Events schema 1 introduces the provider-neutral event/review store and its timestamp/severity, user and review indexes. Neither requires a Core schema increment.

## Transaction, concurrency and evidence model

Mutable configuration uses optimistic versions. Session lifecycle uses repository predicates and lifecycle coordination to prevent late heartbeat resurrection. User deactivation plus session revocation is atomic. Security Event review uses expected `Version`; original security-event fields are append-only through normal application paths.

Session-policy update plus its Audit record is not yet one shared database transaction and must not be represented as atomic evidence. Likewise, Security Events are operational evidence complementary to, not a substitute for, required business Audit transactions.

Bulk pricing Apply remains all-or-nothing through the provider transaction abstraction, with preview evidence revalidated before mutation.

## RBAC and segregation of duties

Service-layer authorization is authoritative. Relevant security permissions are:

- `Users.View` — session visibility/policy read.
- `Settings.Manage` — session lifetime-policy maintenance.
- `UserSessions.Terminate` — destructive session termination.
- `SecurityEvents.View` — Security Center/event visibility and security notifications.
- `SecurityEvents.Manage` — security-event review workflow.

UI visibility mirrors these rights but never replaces service authorization.

## Provider acceptance

Core persistence, Sales schema 10, User Sessions schema 2 and Security Events schema 1 DDL/code exist for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not production certification; live migration, locking, recovery, backup/restore, date behavior and representative load/concurrency acceptance remain required for advertised server/version matrices.
