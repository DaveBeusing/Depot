# Documentation status

Updated: 2026-09-01

This document identifies the documentation baseline for the current development state. Implemented technical controls must remain distinct from production/legal acceptance gates.

## Current baseline

- Application: `0.15.98-preview`
- Help manifest: `1.21`
- Core database schema: `30`
- Sales feature schema: `10`
- Finance feature schema: `9`
- User Sessions feature schema: `3`
- Security Events feature schema: `2`

## Session and authentication invariants

Documentation must state that online presence is derived from an open session plus heartbeat freshness; no persisted `IsOnline` is authoritative. The runtime heartbeat is 30 seconds and presence timeout is 90 seconds. Activity stores only the latest in-Depot keyboard/mouse/touch timestamp, never typed input or coordinates.

The shared User Session policy covers idle timeout, maximum lifetime, concurrent-session mode/limit/action and ended-session history retention. Finite limits are serialized through the policy row and may reject a login or supersede the oldest open session. Password changes invalidate other sessions with `CredentialsChanged`; user deactivation revokes open sessions with `Revoked`.

Production authentication throttling is persisted in the shared database and governed by `AuthenticationSecurityPolicy`; documentation must not call it process-local. The current local credential implementation is behind `IAuthenticationProvider` / `LocalAuthenticationProvider` to preserve the external-identity extension boundary.

Security Center investigation correlates only identifiers already present in Depot authentication/session data. Response actions delegate to the established session/user services. `SecurityEvents.View`, `SecurityEvents.Manage`, `UserSessions.Terminate`, `Users.Manage` and `Settings.Manage` remain separate permissions.

Session history and Security Event retention are actively enforced by bounded background maintenance. Security Event retention never deletes business Audit evidence. High/Critical notification behavior is behind `SecurityAlertPolicy`; the current default threshold is High.

Security Events schema 2 and User Sessions schema 3 are provider-neutral implementation baselines, not live-provider production certification.

## Privacy invariants

The current security implementation does not collect source IP, geolocation, MAC address, hardware fingerprint, typed input, key values, mouse coordinates or external-window activity. `ClientInstanceId` is a generated Depot process/session correlation identifier, not a device fingerprint.

## Documentation rules

Do not describe password-change invalidation, concurrent-session policy, shared database throttling, investigation/response or retention as future-only work. Remaining identity extensions are MFA/OIDC/SSO and any privacy-approved IP/geolocation/device-trust work.

Help manifest **1.21** contains `administration.user-sessions` and `administration.security-center`; topic IDs and routing are unchanged by this documentation update.
