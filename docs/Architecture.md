# Depot Architecture

Updated: 2026-09-08

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

Sales schema 11 enforces the active reservation uniqueness invariant on every supported provider. SQLite and SQL Server use partial/filtered unique indexes; MariaDB/MySQL use an active generated inventory key plus a unique compound index.

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
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Application: **0.15.168-preview**
- Help manifest: **1.21**

Feature schemas evolve independently. Sales schema 11 is a provider-parity/data-integrity correction and therefore does not increment Core schema 30.

## Transaction, concurrency and evidence model

Mutable configuration uses optimistic versions. Session lifecycle uses repository predicates and lifecycle coordination to prevent late heartbeat resurrection. User deactivation plus session revocation is atomic. Security Event review uses expected `Version`; original security-event fields are append-only through normal application paths.

Bulk pricing Apply remains all-or-nothing through the provider transaction abstraction, with preview evidence revalidated before mutation. Provider retries are limited to known transient database conflicts so application/business failures cannot be repeated silently.

## RBAC and segregation of duties

Service-layer authorization is authoritative. UI visibility mirrors permissions but never replaces service authorization. Finance posting, payment, exception-approval and configuration responsibilities remain distinct according to their established permissions and deployment role design.

## Provider production acceptance

The full provider workflow exercises real database/runtime paths through provisioning, migrations, repositories and services. It covers SQL/type/constraint/date/decimal behavior, rollback, concurrency/deadlock/retry, Sales, Procurement, sessions, GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, remote restart, provider-native backup/restore and a representative 100k indexed lookup guard.

This closes the technical database-provider acceptance gate for the exact baselines in the support matrix. It does not claim jurisdiction-specific accounting/legal certification or replace deployment-specific sizing, backup operations, accessibility, signing or organization-control acceptance.
