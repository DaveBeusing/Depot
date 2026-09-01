# Current project status

Updated: 2026-09-01

Depot is on the `0.15.x-preview` development line. The repository contains the integrated Finance platform: foundation/master data, immutable General Ledger, Receivables, Payables, FIFO Inventory Accounting, Banking and Payments, Financial Reporting, and effective-dated Localization.

Depot also has persistent authenticated user sessions, heartbeat-derived online presence, configurable session lifetime policy, administrative revocation, recent session history, suspicious-authentication monitoring and a reviewable Security Center. Every successful login creates a unique session; failed logins create none. Normal logout and clean application shutdown end the current session explicitly, while crash, power loss, network loss, standby and process termination age out automatically through the 90-second presence timeout. Multi-session per user is supported.

The central session policy defaults to a 30-minute idle timeout and 12-hour maximum session age. Idle activity is derived only from keyboard, mouse or touch input inside the Depot main window; the application stores the latest activity timestamp rather than input content. Running sessions that exceed either policy are ended with `Expired` and return to sign-in. Maximum session age is absolute even if activity continues.

Authentication risk monitoring reuses the existing 15-minute throttling window. Repeated failed attempts escalate deterministically from informational events to Warning/High suspicious events and finally a Critical lockout event. Successful authentication after recent failures is retained as a separate Security Event rather than erasing the risk trail. High/Critical events are surfaced through the Notification Center.

Administrators with `Users.View` can open **Administration → User Sessions** to review active sessions, Online Users/Active Sessions metrics, the central session policy and the 200 most recently ended sessions. Users with `UserSessions.Terminate` can terminate one active session or all open sessions for a selected user. Users with `Settings.Manage` can change idle timeout and maximum session age.

Users with `SecurityEvents.View` can open **Administration → Security Center** to review the latest security events and 24-hour metrics for total events, suspicious authentication, open High/Critical events and lockout activity. `SecurityEvents.Manage` additionally permits marking events reviewed. Review metadata is mutable; original event fields remain unchanged through normal application workflows.

Deactivating a user revokes every open session for that user with `Revoked` in the same database transaction as the account deactivation and its Audit evidence. Heartbeats remain technical liveness writes and are not emitted as Audit events. Session-policy changes and administrative termination remain Audit-relevant and also generate Security Events.

Sales pricing supports Global, Regional and optional Customer scopes. The central resolver falls back Customer → Region → Global for each item and retains the selected price source on quote and order lines.

Item Cost Build-up derives a traceable commercial item cost from the active preferred supplier purchase price plus ordered Absolute/Percentage Cost Components. Percentage components explicitly use BaseCost or RunningTotal. Bulk Pricing consumes the same central calculation service, applies Percentage Markup, requires a Preview, supports All Active/Category/Manufacturer/Selected filters and applies through Replace/Only Increase/Only Missing modes to the existing scoped PriceList model.

New installations also receive provider-neutral standard reference data for Units of Measure and Packaging. Depot seeds 12 UoMs (`EA`, `SET`, `PAIR`, `M`, `M2`, `M3`, `KG`, `G`, `L`, `ML`, `H`, `DAY`) and 12 Packaging Types (`UNIT`, `BAG`, `BOX`, `CARTON`, `CASE`, `PACK`, `BUNDLE`, `TRAY`, `REEL`, `ROLL`, `CRATE`, `PALLET`). `EA` is the canonical built-in piece unit; `PCS` is not seeded.

## Session and security safeguards

- `IsOnline` is not persisted; active presence is derived from `EndedUtc IS NULL` plus heartbeat freshness.
- Heartbeat interval is 30 seconds and presence timeout is 90 seconds.
- Central database policy defaults to 30 minutes idle timeout and 12 hours maximum session age.
- User activity is persisted only as `LastActivityUtc`; typed text, key values and mouse coordinates are not stored.
- Policy expiration writes `Expired`; maximum session age is absolute.
- Session-policy updates use optimistic Version checks; viewing requires `Users.View`, editing requires `Settings.Manage`.
- Administrative session termination requires `UserSessions.Terminate` and writes `AdministrativeLogout`.
- User deactivation atomically ends open sessions with `Revoked`.
- Authentication failures are tracked in Security Events using deterministic escalation in the existing 15-minute throttle window.
- High/Critical security events notify `SecurityEvents.View` holders through the existing Notification Center.
- Security Center review requires `SecurityEvents.Manage` and changes review metadata only.
- Security Events are operational security telemetry and do not replace the immutable business Audit Log.
- The security feature does not collect source IP, geolocation, MAC address, hardware fingerprint, key content, mouse coordinates or external-window activity.

## Finance capabilities

- **Finance Foundation:** legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger & Posting:** immutable balanced journals, reporting-currency snapshots, posting profiles, validation, idempotency, Audit evidence and linked reversals.
- **Accounts Receivable:** customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales integration.
- **Accounts Payable:** supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and Purchasing integration.
- **Inventory Accounting:** FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments:** bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **Financial Reporting:** Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension filtering, mappings, deterministic CSV and immutable report snapshots.
- **Finance Localization:** explicit effective-dated legal-entity assignments, built-in `GENERIC → EU → DE` references, custom pack extensibility, capability/configuration/procedure registry, RBAC and Audit evidence.

## Pricing and costing safeguards

- Item cost Base Cost is the active preferred supplier purchase price with an explicit Item Cost Profile currency.
- Legacy supplier prices are not silently treated as EUR; mismatched target currency fails closed until controlled FX conversion is available.
- Cost Components use deterministic Sequence + persisted Id ordering and optimistic Version checks.
- Bulk Preview and Apply both use `ItemCostCalculationService`; there is no second cost formula.
- Percentage Markup is explicitly distinct from Gross Margin.
- Bulk Apply is atomic, revalidates PriceList/entry/cost evidence and records batch Audit evidence.
- Historical submitted/finalized Sales documents remain snapshot-based and are not rewritten by later Bulk Pricing.

## Versions

- Application: **0.15.94-preview**
- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- User Sessions feature schema: **2**
- Security Events feature schema: **1**
- Help manifest: **1.21**

Every commit increments `DepotVersionPatch`.

## Validation boundary

Release Build, win-x64 publish, repository regression tests, Release Integrity, Security Supply Chain and Software Quality gates are required on the final integration head. Provider-neutral Security Events DDL exists for SQLite, SQL Server and MySQL/MariaDB; live-provider acceptance remains a separate production gate.

## Next steps

Security follow-up is now narrowed to password-change session invalidation, concurrent-session limits, retention/archival of historical sessions and Security Events, shared-store throttling for multi-node deployments, richer alert routing, MFA/external identity integration and explicit privacy design before any IP/geolocation/device-trust signals are added.

Further pricing extensions remain demand-driven: controlled FX conversion for cross-currency cost-to-price generation, additional explicit Base Cost source strategies, Target Gross Margin as a separate pricing rule, and commercial rounding strategies.
