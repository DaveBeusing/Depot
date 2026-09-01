# Current project status

Updated: 2026-09-01

Depot is on the `0.15.x-preview` development line. The repository contains the integrated Finance platform: foundation/master data, immutable General Ledger, Receivables, Payables, FIFO Inventory Accounting, Banking and Payments, Financial Reporting, and effective-dated Localization.

Depot also has persistent authenticated user sessions, heartbeat-derived online presence, administrative revocation and recent session history. Every successful login creates a unique session; failed logins create none. Normal logout and clean application shutdown end the current session explicitly, while crash, power loss, network loss, standby and process termination age out automatically through the 90-second presence timeout. Multi-session per user is supported.

Administrators with `Users.View` can open **Administration → User Sessions** to review active sessions, Online Users/Active Sessions metrics and the 200 most recently ended sessions. Users with the additional `UserSessions.Terminate` permission can terminate one active session or all open sessions for the selected user. Administrative termination uses `AdministrativeLogout`, is confirmed and audited, and affected clients return to sign-in after the next heartbeat detects the ended server-side session.

Deactivating a user now revokes every open session for that user with `Revoked` in the same database transaction as the account deactivation and its Audit evidence. Heartbeats remain technical liveness writes and are not emitted as Audit events.

Sales pricing supports Global, Regional and optional Customer scopes. The central resolver falls back Customer → Region → Global for each item and retains the selected price source on quote and order lines.

Item Cost Build-up derives a traceable commercial item cost from the active preferred supplier purchase price plus ordered Absolute/Percentage Cost Components. Percentage components explicitly use BaseCost or RunningTotal. Bulk Pricing consumes the same central calculation service, applies Percentage Markup, requires a Preview, supports All Active/Category/Manufacturer/Selected filters and applies through Replace/Only Increase/Only Missing modes to the existing scoped PriceList model.

New installations also receive provider-neutral standard reference data for Units of Measure and Packaging. Depot seeds 12 UoMs (`EA`, `SET`, `PAIR`, `M`, `M2`, `M3`, `KG`, `G`, `L`, `ML`, `H`, `DAY`) and 12 Packaging Types (`UNIT`, `BAG`, `BOX`, `CARTON`, `CASE`, `PACK`, `BUNDLE`, `TRAY`, `REEL`, `ROLL`, `CRATE`, `PALLET`). `EA` is the canonical built-in piece unit; `PCS` is not seeded. The initializer is idempotent and preserves existing matching or custom values without changing descriptions, activation state or versions.

## Session and presence safeguards

- `IsOnline` is not persisted; active presence is derived from `EndedUtc IS NULL` plus heartbeat freshness.
- Heartbeat interval is 30 seconds and presence timeout is 90 seconds from central options.
- Heartbeat updates only non-ended sessions, preventing logout/revocation races from reviving a session.
- Temporary heartbeat database failures are contained and are not automatically treated as revocation.
- Clean shutdown uses a bounded database-write window.
- Multiple active sessions per user are intentionally supported; there is no active-session uniqueness constraint on `UserId`.
- Session viewing requires `Users.View`; destructive session control additionally requires `UserSessions.Terminate` in the service layer.
- Single-session and bulk user-session termination use `AdministrativeLogout` and Audit evidence.
- User deactivation atomically ends every open session for that user with `Revoked`.
- The client responds to server-side revocation by clearing local authorization and returning to sign-in after heartbeat detection.
- Administration exposes Active and History tabs; History contains the 200 most recently ended sessions with duration and end reason.
- Session presence stores no MAC address, hardware fingerprint, key logging, OS activity, external-window tracking, IP/geolocation data or similar telemetry.

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

## Reference-data safeguards

- Standard UoM and Packaging values reuse the existing `UnitsOfMeasure` and `Packagings` tables; no parallel schema or hardcoded UI list exists.
- `EA` is the canonical piece unit. `PCS`, `PC` and `Piece` are not built-in equivalents.
- UoM expresses how item quantity is measured; Packaging describes physical/logistical packaging.
- Packaging Types contain no quantity, multiplier or conversion factor.
- Case-insensitive natural-key checks make initialization idempotent across SQLite, SQL Server and MySQL/MariaDB.
- Existing matching values and custom values are preserved exactly, including inactive records.
- Technical seed creation is not emitted as repeated user Audit activity; later user maintenance still uses existing RBAC/Audit services.

## Versions

- Application: **0.15.x-preview** (`Directory.Build.props` is authoritative for the exact patch; this documentation update advances it to **0.15.89-preview**)
- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- User Sessions feature schema: **1**
- Help manifest: **1.20**

Every commit increments `DepotVersionPatch`.

## Validation boundary

Release Build, win-x64 publish, repository regression tests, Release Integrity, Security Supply Chain and Software Quality gates are required on the final integration head. Provider-neutral DDL exists for SQLite, SQL Server and MySQL/MariaDB. Optional live-provider tests exercise scoped pricing, Item Cost schema migration, standard UoM/Packaging initialization and User Sessions persistence when server connection strings are configured.

## Next steps

Session-security follow-up is now narrowed to policy and monitoring features rather than basic revocation: configurable idle timeout, maximum session age, password-change session policy, concurrent-session limits, retention/archival of historical sessions, suspicious-login/security-event monitoring, MFA/external identity integration and a broader Security Center.

Further pricing extensions are demand-driven: controlled FX conversion for cross-currency cost-to-price generation, additional explicit Base Cost source strategies, Target Gross Margin as a separate pricing rule, and commercial rounding strategies such as 0.05/0.10/0.50 or .99 endings. Item-specific packaging quantities and unit conversions remain a separate future capability; they are not encoded in global Packaging Types.
