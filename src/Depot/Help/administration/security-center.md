# Security Center

Use **Administration → Security Center** to investigate authentication/session risk, review events, maintain authentication policy and apply controlled response actions.

## Permissions

- `SecurityEvents.View` — open Security Center, metrics and investigation events.
- `SecurityEvents.Manage` — mark events reviewed.
- `Settings.Manage` — change authentication failure/lockout/retention policy.
- `UserSessions.Terminate` — terminate one or all sessions for a resolved user.
- `Users.Manage` — deactivate a resolved user.

Service-layer authorization is authoritative.

## Shared authentication policy

Production failed-login throttling is shared through the Depot database. The policy controls failure window, lockout threshold, lockout duration and Security Event retention. Defaults are 15 minutes, 5 failures, 15 minutes and 365 days respectively.

## Investigation and response

Select an event to see its session/client identifiers, related events and any open sessions that your permissions allow Depot to resolve. Correlation uses existing `UserId`, normalized account, `SessionId` and generated `ClientInstanceId` values.

Available response actions are **Terminate session**, **Terminate all sessions** and **Deactivate user**. Depot delegates these actions to the existing session/user services, so Audit, RBAC, concurrency and revocation rules remain unchanged. Destructive actions require confirmation.

## Metrics and review

The cards show Events 24h, Suspicious 24h, High Risk Open, Blocked 24h, Reviewed 24h and Open Unreviewed. Use search, minimum severity and **Only unreviewed** to filter. Marking reviewed changes only review metadata and version.

## Retention and notifications

High and Critical events are routed through the Security Alert policy to `SecurityEvents.View` holders. A bounded maintenance process enforces the configured Security Event retention and stale-throttle cleanup. Business Audit records are separate and are not deleted by Security Event retention.

## Privacy boundary

Depot does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity for this feature. `ClientInstanceId` is a generated Depot process correlation ID, not a device fingerprint.

## Related topics

- [User Sessions](topic:administration.user-sessions)
- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
