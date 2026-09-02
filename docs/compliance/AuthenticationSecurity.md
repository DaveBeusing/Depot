# Depot Authentication Security

Updated: 2026-09-01

## Passwords and provider boundary

New Depot password hashes use PBKDF2-HMAC-SHA256 with per-password random salt, a 256-bit derived key and the encoded work factor. New/changed passwords require the existing 12–128 character complexity policy and are never logged.

Authentication now depends on `IAuthenticationProvider`. `LocalAuthenticationProvider` is the built-in credential provider; this keeps MFA/OIDC/SSO integration behind an explicit identity boundary rather than coupling external identity logic to session/RBAC code.

## Shared login throttling

Production failed-login state is persisted in the database and shared by Depot clients. `AuthenticationSecurityPolicy` defaults to a 15-minute failure window, 5-failure lockout threshold, 15-minute lockout duration and 365-day Security Event retention. `Settings.Manage` is required to modify the policy, and optimistic versioning prevents stale updates.

Throttle mutations serialize through the singleton policy row. Separate clients therefore share the same failure count and lockout state. Suspicious-event escalation remains deterministic and is triage evidence rather than proof of compromise.

## Sessions and concurrent policy

Successful authentication creates a persistent session before authorization is published. Heartbeat is 30 seconds and presence timeout 90 seconds. The central session policy defaults to 30 minutes idle, 12 hours maximum age, Unlimited concurrent sessions, configured maximum 3, RejectNewSession, and 180-day ended-session retention.

Finite limits support `MaximumSessions` or `SingleSession` and either reject the new login or atomically supersede the oldest open session. Policy-row serialization prevents competing clients from independently exceeding the finite limit.

Password changes invalidate other target-user sessions as `CredentialsChanged`; administrative resets invalidate all target sessions. User deactivation atomically ends open sessions as `Revoked`. Administrative session termination remains separately permissioned through `UserSessions.Terminate`.

## Security Center and response

`SecurityEvents.View` permits Security Center visibility and investigation; `SecurityEvents.Manage` permits review. Investigation correlates existing `UserId`, account, `SessionId` and generated `ClientInstanceId`. Response actions delegate to authorized `UserSessionAdministrationService` and `UserService` paths for session termination and user deactivation.

Session-policy changes, administrative termination and authentication-policy changes write Audit plus Security Event evidence through transaction-aware paths. High/Critical notification routing is separated by `SecurityAlertPolicy`.

## Retention

A bounded maintenance service actively enforces ended-session history retention and Security Event retention and removes stale authentication-throttle rows. It uses fixed 250-row batches, a maximum of four batches per data class per run, policy locks and cutoff predicates repeated in the delete transaction. Concurrent maintenance attempts are therefore restart-safe and idempotent.

Security Event retention does not affect the separate business Audit Log.

## Persistence

- Core schema: **30**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**

Provider DDL exists for SQLite, SQL Server and MySQL/MariaDB. Provider-neutral implementation is not production certification; live migration, lock/deadlock, recovery and representative load acceptance remain required.

## Privacy boundary

Depot does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates, OS activity or external-window activity for these controls. `ClientInstanceId` is a generated Depot process correlation identifier, not a hardware/device fingerprint.

## Remaining roadmap

Remaining identity/security work is MFA, OIDC/SSO/external identity providers, deployment-specific alert delivery/routing where required, and an explicit privacy/threat-model design before any IP/geolocation/device-trust signals are considered.
