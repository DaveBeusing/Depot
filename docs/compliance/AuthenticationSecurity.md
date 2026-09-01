# Depot Authentication Security

Updated: 2026-09-01

## Password hashing

New Depot password hashes use PBKDF2-HMAC-SHA256 with a per-password 128-bit random salt, a 256-bit derived key and a current work factor of 600,000 iterations. The encoded format contains the algorithm and iteration count, allowing older hashes to remain verifiable while being identifiable for later upgrade.

The work factor must be benchmarked on supported production hardware before 1.0 and reviewed at least annually. It may be raised without invalidating existing hashes because verification uses the encoded iteration count.

## Password policy

New/changed passwords require 12-128 characters plus uppercase, lowercase, numeric and symbol characters, and may not contain a meaningful account-name component. Passwords are never logged or stored outside the password hash.

## Login throttling

Depot tracks failed attempts by normalized account key in process memory. Five failures inside a 15-minute window cause a 15-minute lockout. A successful authentication clears the failure state. This limits online guessing without creating a persistent denial-of-service flag in the user record.

The current limiter is per application process. A future multi-node/server authentication architecture must move throttling to a shared trusted store or identity provider.

## Authenticated sessions, presence and lifetime policy

A successful local authentication creates a unique persistent `UserSession` before the authenticated identity is published to the application. Failed authentication creates no session. Multiple simultaneous sessions for the same user are allowed.

Online presence is derived only when the session has no `EndedUtc` value and `LastSeenUtc` is within the central 90-second presence timeout. Depot sends a heartbeat every 30 seconds while the session is authenticated. Heartbeat updates only still-unended sessions, preventing a late heartbeat from reviving a session that logout, expiration or revocation already ended.

Depot also persists one central `UserSessionPolicy` shared by all clients. Defaults are a 30-minute idle timeout and 12-hour maximum session age. Supported ranges are 5–480 idle minutes and 1–168 maximum-age hours. `Users.View` permits reading this policy; `Settings.Manage` is required to modify it.

The main window detects keyboard, mouse and touch activity only to maintain a latest-activity timestamp. Activity is throttled in memory and persisted with the normal heartbeat; Depot does not persist typed text, key values, mouse coordinates or a stream of input events. The heartbeat writes the latest activity before applying the current policy.

A running session is ended with `EndReason = Expired` when either the idle timeout or the absolute maximum session age is reached. Maximum session age applies even while user activity continues. Saving a stricter policy evaluates existing open sessions immediately, and sessions already beyond the new limits are marked `Expired`. Affected clients clear their authenticated identity and return to sign-in after detecting the ended session.

Normal logout records `LoggedOut`; a clean application exit records `ApplicationClosed` with a bounded shutdown write. Crashes, power loss, process termination, network loss and standby are handled by heartbeat presence expiry rather than relying on a logout callback.

## Administrative session security

Session visibility, lifetime-policy maintenance and destructive session control use separate service-layer permissions:

- `Users.View` — view active sessions, presence metrics, current session policy and recent ended-session history.
- `Settings.Manage` — change idle timeout and maximum session age.
- `UserSessions.Terminate` — terminate one active session or all open sessions for a selected user.

Policy changes use optimistic Version checks. Administrative termination uses `EndReason = AdministrativeLogout`. Affected running clients detect the ended server-side session at the next successful heartbeat check, clear the local authenticated identity and return to the normal sign-in flow. Temporary heartbeat database failures are contained and are not treated as proof of revocation or expiration.

Deactivating a user revokes every still-open session for that user with `EndReason = Revoked` in the same database transaction as the account deactivation and user Audit evidence. This ensures deactivation affects already-authenticated clients rather than only future login attempts.

The administration view also exposes the 200 most recently ended sessions, including lifecycle duration and end reason such as `Expired`, `AdministrativeLogout` or `Revoked`. This operational history is stored in `UserSessions`; it does not replace the immutable business Audit log.

## Persistence and migration

User-session persistence is feature-versioned independently from the core schema. User Sessions feature schema version 2 adds the provider-neutral `UserSessionPolicy` singleton for SQLite, SQL Server and MySQL/MariaDB. The v1→v2 table creation, default seed and feature-version update execute within one provider write transaction.

## Audit behavior

Heartbeats and raw activity events are deliberately excluded from Audit because they are high-volume technical liveness signals. Administrative session termination and session-policy changes are audit-relevant actions. Account deactivation remains audited through the existing user-management transaction and atomically revokes the user's open sessions.

The current administration-service policy update and its Audit write are not yet one shared database transaction; this remains a transaction-composition hardening item and must not be described as atomic evidence.

## Privacy boundary

Session data is deliberately minimal: user/session identifiers, timestamps, generated client-instance identifier, display-only machine name, application version, end state, central lifetime-policy values and optimistic versions. User activity tracking stores only the latest time input occurred inside Depot.

The feature does not collect typed text, key values, mouse coordinates, MAC addresses, hardware fingerprints, operating-system activity, external-window activity, IP addresses, geolocation or similar telemetry.

## Current security gaps / roadmap

The current session architecture supports server-side revocation, configurable idle timeout and maximum session age. Remaining security work includes concurrent-session limits, password-change session policy, suspicious-login/security-event monitoring, historical-session retention/archival and a broader Security Center.

MFA and external identity providers (Microsoft Entra ID/OIDC and, where customer demand justifies it, SAML) remain roadmap items. Enterprise identity should be introduced behind an authentication-provider abstraction so local accounts remain usable for offline/recovery scenarios and external-provider policy can be centrally enforced.
