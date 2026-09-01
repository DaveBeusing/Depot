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

## Authenticated sessions and presence

A successful local authentication creates a unique persistent `UserSession` before the authenticated identity is published to the application. Failed authentication creates no session. Multiple simultaneous sessions for the same user are allowed.

Online presence is derived only when the session has no `EndedUtc` value and `LastSeenUtc` is within the central 90-second presence timeout. Depot sends a heartbeat every 30 seconds while the session is authenticated. Heartbeat updates only still-unended sessions, preventing a late heartbeat from reviving a session that logout or revocation already ended.

Normal logout records `LoggedOut`; a clean application exit records `ApplicationClosed` with a bounded shutdown write. Crashes, power loss, process termination, network loss and standby are handled by heartbeat expiry rather than relying on a logout callback.

## Administrative session security

Session visibility and destructive session control use separate service-layer permissions:

- `Users.View` — view active sessions, presence metrics and recent ended-session history.
- `UserSessions.Terminate` — terminate one active session or all open sessions for a selected user.

Administrative termination uses `EndReason = AdministrativeLogout`. Affected running clients detect the ended server-side session at the next successful heartbeat check, clear the local authenticated identity and return to the normal sign-in flow. Temporary heartbeat database failures are contained and are not treated as proof of revocation.

Deactivating a user revokes every still-open session for that user with `EndReason = Revoked` in the same database transaction as the account deactivation and user Audit evidence. This ensures deactivation affects already-authenticated clients rather than only future login attempts.

The administration view also exposes the 200 most recently ended sessions, including lifecycle duration and end reason. This operational history is stored in `UserSessions`; it does not replace the immutable business Audit log.

## Audit behavior

Heartbeats are deliberately excluded from Audit because they are high-volume technical liveness writes. Administrative session termination is audited. Account deactivation remains audited through the existing user-management transaction and atomically revokes the user's open sessions.

## Privacy boundary

Session data is deliberately minimal: user/session identifiers, timestamps, generated client-instance identifier, display-only machine name, application version, end state and optimistic version. The feature does not collect MAC addresses, hardware fingerprints, key logging, operating-system activity, external-window activity, IP addresses, geolocation or similar telemetry.

## Current security gaps / roadmap

The current session architecture supports explicit server-side revocation, but does not yet implement configurable idle timeout, maximum session age, concurrent-session limits, password-change session policy, suspicious-login detection, IP/geolocation analysis, security alerts or a retention/archival policy for historical session rows.

MFA and external identity providers (Microsoft Entra ID/OIDC and, where customer demand justifies it, SAML) remain roadmap items. Enterprise identity should be introduced behind an authentication-provider abstraction so local accounts remain usable for offline/recovery scenarios and external-provider policy can be centrally enforced.
