# Depot Authentication Security

Updated: 2026-09-01

## Password hashing

New Depot password hashes use PBKDF2-HMAC-SHA256 with a per-password 128-bit random salt, a 256-bit derived key and a current work factor of 600,000 iterations. The encoded format contains the algorithm and iteration count, allowing older hashes to remain verifiable while being identifiable for later upgrade.

The work factor must be benchmarked on supported production hardware before 1.0 and reviewed at least annually. It may be raised without invalidating existing hashes because verification uses the encoded iteration count.

## Password policy

New/changed passwords require 12-128 characters plus uppercase, lowercase, numeric and symbol characters, and may not contain a meaningful account-name component. Passwords are never logged or stored outside the password hash.

## Login throttling and suspicious-authentication monitoring

Depot tracks failed attempts by normalized account key in process memory. Five failures inside a 15-minute window cause a 15-minute lockout. A successful authentication clears the active throttling state.

The same window now feeds a deterministic Security Event model:

- failures 1–2: informational authentication failures;
- failure 3: suspicious authentication pattern, Warning;
- failure 4: suspicious authentication pattern, High;
- failure 5: Critical lockout event;
- attempts while lockout remains active: Critical;
- successful authentication after recent failures: separately recorded and elevated according to the preceding failure count.

The rules are triage signals rather than proof that an account is compromised. Authentication-event persistence is best-effort: a temporary telemetry failure is logged diagnostically but does not make a valid login fail solely because the event could not be stored.

The current limiter remains per application process. A future multi-node/server authentication architecture must move throttling to a shared trusted store or identity provider.

## Authenticated sessions, presence and lifetime policy

A successful local authentication creates a unique persistent `UserSession` before the authenticated identity is published to the application. Failed authentication creates no session. Multiple simultaneous sessions for the same user are allowed.

Online presence is derived only when the session has no `EndedUtc` value and `LastSeenUtc` is within the central 90-second presence timeout. Depot sends a heartbeat every 30 seconds while the session is authenticated. Heartbeat updates only still-unended sessions, preventing a late heartbeat from reviving a session that logout, expiration or revocation already ended.

Depot persists one central `UserSessionPolicy` shared by all clients. Defaults are a 30-minute idle timeout and 12-hour maximum session age. Supported ranges are 5–480 idle minutes and 1–168 maximum-age hours. `Users.View` permits reading this policy; `Settings.Manage` is required to modify it.

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

## Security Center

`SecurityEvents.View` grants access to **Administration → Security Center** and its recent-event metrics. `SecurityEvents.Manage` additionally permits marking events reviewed.

Security Events are an operational security stream separate from the immutable business Audit Log. Events include authentication outcomes, suspicious failure escalation, lockout activity, successful authentication after failures, administrative session termination and session-policy changes. High and Critical events are additionally surfaced through the existing Notification Center to active `SecurityEvents.View` holders.

Review changes only review metadata and optimistic Version. Original event type, timestamp, severity, account/session context, summary and details are not edited through normal application workflows.

## Persistence and migration

User Sessions feature schema version 2 contains the provider-neutral `UserSessionPolicy` singleton. Security Events use an independent provider-neutral feature schema `SecurityEvents` version 1. Core schema remains 30. Both feature schemas support SQLite, SQL Server and MySQL/MariaDB.

## Audit behavior

Heartbeats and raw activity events are deliberately excluded from Audit because they are high-volume technical liveness signals. Administrative session termination and session-policy changes remain Audit-relevant actions and also produce Security Events. Security Events complement rather than replace Audit evidence.

The current administration-service policy update and its Audit write are not yet one shared database transaction; this remains a transaction-composition hardening item and must not be described as atomic evidence.

## Privacy boundary

Session/security data remains deliberately scoped. Security Events may store a normalized account identifier and existing session machine name where available. The feature does not collect typed text, key values, mouse coordinates, MAC addresses, hardware fingerprints, operating-system activity, external-window activity, source IP addresses or geolocation.

IP/geolocation or device-trust risk signals require an explicit privacy/security design before implementation.

## Current security gaps / roadmap

Implemented session/security controls now include server-side revocation, configurable idle timeout and maximum session age, suspicious-login monitoring, Security Events, High/Critical notifications and a reviewable Security Center.

Remaining security work includes concurrent-session limits, password-change session invalidation, persistence/shared-store throttling for multi-node deployments, retention/archival for historical sessions and Security Events, richer alert routing, MFA and external identity providers (Microsoft Entra ID/OIDC and, where customer demand justifies it, SAML).
