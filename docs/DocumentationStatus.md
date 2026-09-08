# Documentation status

Updated: 2026-09-08

This document identifies the documentation baseline for the current development state. Implemented technical controls must remain distinct from production/legal acceptance gates.

## Current baseline

- Application: `0.15.169-preview`
- Help manifest: `1.21`
- Core database schema: `30`
- Sales feature schema: `11`
- Finance feature schema: `9`
- User Sessions feature schema: `3`
- Security Events feature schema: `2`

## Database provider baseline

The authoritative production database support statement is [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

Current certified technical baselines are:

- Depot-bundled SQLite runtime;
- SQL Server 2022 / engine 16.x, with CI certification on SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

Documentation must not infer provider support from shared abstractions or from a different product in the same connector family. MariaDB and MySQL are independently tested. Newer/older versions remain untested/best-effort until their own full acceptance evidence exists.

The full provider matrix validates provisioning/migration, provider SQL/types/constraints, rollback, concurrency/deadlock/retry, representative Sales/Procurement/session/Finance flows, Banking/reconciliation, Financial Reporting/snapshots, remote restart, provider-native backup/restore and 100k indexed access.

SQLite documentation must preserve its dynamic `NUMERIC` precision boundary; it must not claim full fixed `DECIMAL(28,9)` range equivalence with the server providers.

## Session and authentication invariants

Documentation must state that online presence is derived from an open session plus heartbeat freshness; no persisted `IsOnline` is authoritative. The runtime heartbeat is 30 seconds and presence timeout is 90 seconds. Activity stores only the latest in-Depot keyboard/mouse/touch timestamp, never typed input or coordinates.

The shared User Session policy covers idle timeout, maximum lifetime, concurrent-session mode/limit/action and ended-session history retention. Finite limits are serialized through the policy row and may reject a login or supersede the oldest open session. Password changes invalidate other sessions with `CredentialsChanged`; user deactivation revokes open sessions with `Revoked`.

Production authentication throttling is persisted in the shared database and governed by `AuthenticationSecurityPolicy`; documentation must not call it process-local. The current local credential implementation is behind `IAuthenticationProvider` / `LocalAuthenticationProvider` to preserve the external-identity extension boundary.

Security Center investigation correlates only identifiers already present in Depot authentication/session data. Response actions delegate to the established session/user services. `SecurityEvents.View`, `SecurityEvents.Manage`, `UserSessions.Terminate`, `Users.Manage` and `Settings.Manage` remain separate permissions.

Session history and Security Event retention are actively enforced by bounded background maintenance. Security Event retention never deletes business Audit evidence. High/Critical notification behavior is behind `SecurityAlertPolicy`; the current default threshold is High.

## Privacy invariants

The current security implementation does not collect source IP, geolocation, MAC address, hardware fingerprint, typed input, key values, mouse coordinates or external-window activity. `ClientInstanceId` is a generated Depot process/session correlation identifier, not a device fingerprint.

## Documentation rules

Do not describe password-change invalidation, concurrent-session policy, shared database throttling, provider certification, investigation/response or retention as future-only work.

Do not describe database-provider technical support as jurisdiction-specific accounting, tax, legal, accessibility, bank-network or regulatory certification. Remote backup scheduling, retention, off-host copies and restore procedures remain operator responsibilities even though the CI matrix validates a provider-native restore boundary.

Help manifest **1.21** contains `administration.user-sessions` and `administration.security-center`; topic IDs and routing are unchanged by this documentation update.
