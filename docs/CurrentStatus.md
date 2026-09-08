# Current project status

Updated: 2026-09-08

Depot is on the `0.15.x-preview` development line. Finance, inventory, purchasing, sales, reporting, localization, notifications, Audit, persistent user sessions and operational security monitoring are integrated in the repository.

## Database provider production status

Depot now has a dedicated real-provider production acceptance matrix in `.github/workflows/database-provider-acceptance.yml`.

The following database baselines are technically **Supported** when shipped with a release whose full provider matrix is green:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x; CI certification uses SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The full matrix validates fresh/idempotent provisioning, Core 29→30 and Sales 10→11 migrations, concurrent provisioning, SQL/type/constraint/date/decimal behavior, transactional rollback, concurrency/deadlock/retry behavior, Sales, Procurement, sessions, Finance GL/AR/AP/FIFO, Banking/reconciliation, Financial Reporting/snapshots, server restart, native remote backup/restore and representative 100k indexed access.

MariaDB and MySQL are independently certified; support for one never implies support for the other. Versions outside the listed baselines remain untested/best-effort until explicitly added to the matrix.

SQLite remains the embedded baseline but does not provide the full fixed `DECIMAL(28,9)` magnitude/precision semantics of the server providers because SQLite uses dynamic `NUMERIC` affinity. See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md) for the exact boundary.

## Session and authentication security

Depot persists one session per successful login and derives online presence from heartbeat freshness. The shared session policy covers idle timeout, absolute maximum session age, concurrent-session mode/limit/action and ended-session history retention. Concurrent limits can reject a new login or supersede the oldest session under a database serialization lock.

Credential changes invalidate other open sessions with `CredentialsChanged`; account deactivation atomically revokes open sessions with `Revoked`. Administrative single/bulk termination remains permissioned through `UserSessions.Terminate` and is coupled to Audit/Security Event evidence in the production transaction path.

Production login throttling is database-shared across Depot clients. `AuthenticationSecurityPolicy` controls failure window, lockout threshold, lockout duration and Security Event retention. Local credentials are behind `IAuthenticationProvider` / `LocalAuthenticationProvider`, preserving a future OIDC/SSO boundary.

**Administration → User Sessions** exposes lifetime, concurrency and history-retention policy plus active/history views and termination controls. **Administration → Security Center** exposes review KPIs, filters, authentication-policy maintenance, event/session/client correlation and controlled response actions for terminating sessions or deactivating a resolved user.

A bounded maintenance service enforces ended-session history retention, Security Event retention and stale authentication-throttle cleanup in fixed-size batches. High/Critical event notifications are routed through a separate `SecurityAlertPolicy` boundary.

The security feature does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity.

## Versions

- Application: **0.15.169-preview**
- Core database schema: **30**
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

Every commit increments `DepotVersionPatch`.

Sales schema 11 restores/enforces the active inventory-reservation uniqueness invariant for every supported provider. This feature-schema correction does not change Core database schema 30.

## Validation boundary

Release build with `-warnaserror`, repository regression suites, Release Integrity, Security Supply Chain and Software Quality remain release gates. Database-provider support is additionally governed by the full real-provider acceptance workflow and its exact version matrix.

Provider support does not replace deployment-specific accounting/tax/legal, accessibility, signing, OS/client, performance-sizing, backup-retention or organizational acceptance.

## Next steps

Remaining authentication roadmap items include MFA, OIDC/SSO/external identity providers, optional deployment-specific alert delivery/routing implementations, and explicit privacy/threat-model work before IP/geolocation/device-trust signals are considered.

For the wider 1.0 path, the database-provider technical gate is closed for the certified baselines. Remaining release work includes signing, accessibility/manual desktop acceptance, deployment procedures, accounting/tax/localization review and other items tracked in [Release 1.0](Release1.0.md) and the [Roadmap](Roadmap.md).
