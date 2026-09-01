# Current project status

Updated: 2026-09-01

Depot is on the `0.15.x-preview` development line. Finance, inventory, purchasing, sales, reporting, localization, notifications, Audit, persistent user sessions and operational security monitoring are integrated in the repository.

## Session and authentication security

Depot persists one session per successful login and derives online presence from heartbeat freshness. The shared session policy now covers idle timeout, absolute maximum session age, concurrent-session mode/limit/action and ended-session history retention. Concurrent limits can reject a new login or supersede the oldest session under a database serialization lock.

Credential changes invalidate other open sessions with `CredentialsChanged`; account deactivation atomically revokes open sessions with `Revoked`. Administrative single/bulk termination remains permissioned through `UserSessions.Terminate` and is coupled to Audit/Security Event evidence in the production transaction path.

Production login throttling is database-shared across Depot clients. `AuthenticationSecurityPolicy` controls failure window, lockout threshold, lockout duration and Security Event retention. Local credentials are behind `IAuthenticationProvider` / `LocalAuthenticationProvider`, preserving a future OIDC/SSO boundary.

**Administration → User Sessions** exposes lifetime, concurrency and history-retention policy plus active/history views and termination controls. **Administration → Security Center** exposes six review KPIs, filters, authentication-policy maintenance, event/session/client correlation and controlled response actions for terminating sessions or deactivating a resolved user.

A bounded maintenance service enforces ended-session history retention, Security Event retention and stale authentication-throttle cleanup in fixed-size batches. High/Critical event notifications are routed through a separate `SecurityAlertPolicy` boundary.

The security feature does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity.

## Versions

- Application: **0.15.98-preview**
- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

Every commit increments `DepotVersionPatch`.

## Validation boundary

Release build, win-x64 publish, repository regression tests, Release Integrity, Security Supply Chain and Software Quality gates are required on the final integration head. Provider-neutral code and migrations do not replace live SQL Server/MySQL-MariaDB acceptance for production certification.

## Next steps

The remaining authentication roadmap is deliberately narrower: MFA, OIDC/SSO/external identity providers, optional deployment-specific alert delivery/routing implementations, and explicit privacy/threat-model work before any IP/geolocation/device-trust signals are considered.

Commercial/Finance follow-up remains independent from this security work and continues according to the respective feature roadmaps.
