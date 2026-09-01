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

## Application shell

The shell is permission-aware and workspace-oriented. Finance exposes **Receivables**, **Payables**, **Inventory Accounting**, **Banking**, **Financial Reporting** and **Localization**. UI visibility improves usability only; service authorization is authoritative.

## Authentication sessions, presence and policy enforcement

`AuthenticationService` remains the successful-login boundary and `AuthorizationService` remains the current identity/RBAC source. `SessionService` extends that flow with one persistent `UserSession` per successful login, a single non-overlapping heartbeat loop, in-application activity timestamping and client-side response to server-side session termination. Failed logins never create sessions.

Presence is derived rather than stored:

```text
EndedUtc IS NULL
AND LastSeenUtc >= UtcNow - PresenceTimeout
```

The central runtime defaults are a 30-second heartbeat and a 90-second presence timeout. Normal logout ends a session as `LoggedOut`; clean application shutdown uses `ApplicationClosed` with a bounded write. Crashes, power loss, network loss, standby and process termination require no explicit cleanup because stale heartbeats naturally age out. Heartbeat updates include `EndedUtc IS NULL`, so a delayed heartbeat cannot reactivate an ended session.

A centrally persisted `UserSessionPolicy` adds two independent security limits:

```text
Idle timeout default:        30 minutes
Maximum session age default: 12 hours
```

The supported ranges are 5–480 idle minutes and 1–168 maximum-age hours. Keyboard, mouse and touch input received by the Depot main window updates an in-memory activity timestamp with a small throttle. The timestamp is persisted only with the normal heartbeat; input content is never recorded. The heartbeat writes the latest activity before policy evaluation, preventing a recent user action from being lost at the idle boundary.

Policy enforcement ends a still-open session with `Expired` when either `UtcNow - LastActivityUtc >= IdleTimeout` or `UtcNow - StartedUtc >= MaximumSessionAge`. Maximum session age is absolute even while the user remains active. Saving a stricter policy also evaluates all currently open sessions immediately. Affected clients detect the ended row on their next heartbeat, clear local authorization and return to sign-in.

Session administration has three authorization levels. `Users.View` permits active-session, presence-metric, recent-history and policy reads. `UserSessions.Terminate` additionally permits destructive session control. `Settings.Manage` permits changes to idle timeout and maximum session age. Policy updates are optimistic-versioned and are Audit-relevant administration changes when `AuditService` is present.

Administrators can terminate one active session or all open sessions for a selected user. Those actions use `AdministrativeLogout`, are confirmed in the UI and are audited. The affected client detects the ended row on a later heartbeat, clears its local authorization context and returns to the sign-in flow.

User deactivation is integrated with the same lifecycle. `UserService` ends every open session for the deactivated user with `Revoked` in the same transaction as the account-state change and Audit evidence. Thus deactivation applies to already-authenticated clients, not only future authentication attempts.

**Administration → User Sessions** exposes the current policy plus Active and History views. Active rows show the concrete login instance; History shows the 200 most recently ended sessions with duration and end reason, including `Expired`. Multiple sessions per user remain intentional. Dashboard presence distinguishes `COUNT(DISTINCT UserId)` online users from active session rows. Heartbeats and raw input events remain technical liveness signals and are not Audit events.

See [User Sessions and Online Presence](UserSessions.md) for the persistence, lifecycle, privacy and security contract.

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

Commercial costing and sales-price resolution are separate but connected business boundaries:

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

`ItemCostCalculationService` is the single item-cost formula. Percentage components explicitly use `BaseCost` or `RunningTotal`, are ordered by `Sequence` plus persisted component identity, and return calculation evidence. `PriceListGenerationService` consumes that calculation; it does not reproduce it. Bulk Apply uses the established price-list repository and transaction infrastructure.

`SalesPricingService` remains the single business boundary for runtime item-price resolution. It resolves Customer → Region → Global independently for every item and returns source metadata with the price. Quotes and Sales Orders consume this result; Views and ViewModels do not reproduce fallback logic. Historical document lines retain price-source snapshots.

## Schema versions

- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- User Sessions feature schema: **2**
- Application: **0.15.x-preview**
- Help manifest: **1.20**

Core schema 30 remains the current shared compatibility baseline. Sales schema 9 introduced scoped price lists, Sales Regions and quote/order price-source snapshots. Sales schema 10 adds provider-neutral `ItemCostProfiles` and `ItemCostComponents`. Finance schema evolution remains independent through Finance schema 9. User Sessions schema 1 introduced provider-neutral persistent authenticated sessions and presence indexes; schema 2 adds the centrally persisted `UserSessionPolicy` singleton. The 1→2 policy migration, seed and feature-version update execute in one provider write transaction.

Feature schemas are versioned independently from the core schema. A feature-local persistence change increments its feature version; a shared/core schema change increments `DatabaseVersion.CurrentVersion`.

## Transaction, concurrency and evidence model

Mutable configuration uses optimistic versions. Required business mutation and Audit evidence commit or roll back together where they form one transaction.

Session heartbeat/logout/revocation/expiration concurrency is protected by lifecycle coordination and repository predicates that update heartbeats only while `EndedUtc IS NULL`. Presence therefore cannot be revived by a late write after logout, policy expiration or administrative termination. The heartbeat persists the current activity timestamp before applying idle/max-age predicates, avoiding a boundary race. User deactivation and revocation of that user's open sessions share one database transaction. Session-policy writes use an expected Version and reject stale edits.

Bulk price Apply is all-or-nothing through the existing provider write transaction. Preview captures target PriceList/entry versions plus item-cost evidence. Apply reloads the current records, recalculates through `ItemCostCalculationService`, compares evidence and fails closed on a concurrent change. Preview and Apply therefore cannot diverge through separate formulas.

Finalized accounting and operational evidence is not silently rewritten. Item Cost Build-up and Bulk Pricing modify current master/pricing configuration only; submitted/finalized Sales documents retain their stored snapshots.

## Currency and rounding boundary

Item Cost Profiles state the currency of the selected purchasing Base Cost because existing supplier purchase prices do not carry currency metadata. Costs in different currencies are never added or written to a target PriceList through an implicit 1:1 conversion. Until controlled FX conversion is explicitly integrated, mismatched currencies fail closed.

Commercial cost and generated price amounts use deterministic decimal currency precision. More advanced commercial rounding remains an extension point rather than an implicit UI rule.

## RBAC and segregation of duties

Service-layer permissions are authoritative. Item-cost visibility/maintenance reuses the existing Item permissions; Bulk Pricing reuses Sales Pricing permissions in combination with item-cost visibility. User-session reads require `Users.View`; administrative termination additionally requires `UserSessions.Terminate`; session-policy changes additionally require `Settings.Manage`. UI controls mirror these rights but do not replace service authorization.

The Finance role receives normal Finance management rights; sensitive supplier/payment approvals remain independently controlled. Deployments can define stricter custom-role separation for configuration, posting, approval, reconciliation, reporting preparation and review.

## Provider acceptance

Core persistence, Sales schema 10 and User Sessions schema 2 DDL/code exist for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not equivalent to production certification. Live migration, locking, deadlock/retry, recovery, backup/restore, date/decimal behavior and representative performance/concurrency acceptance remain required for every advertised server/version matrix.
