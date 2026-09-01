# User Sessions

Use **Administration → User Sessions** to review authenticated Depot clients, recently ended sessions, and administrative revocation. Viewing requires `Users.View`; destructive session actions additionally require `UserSessions.Terminate`. Both are enforced in the service layer.

## Definitions

- **User Session** — one authenticated Depot login instance.
- **Active Session** — a session with no end timestamp whose `LastSeenUtc` is within the configured presence timeout.
- **Online User** — a user with at least one active session.
- **Heartbeat** — the technical presence signal that advances `LastSeenUtc`; it is not a business audit event.
- **Multi-Session** — one user may have multiple simultaneous Depot sessions, for example an office PC and a notebook.

## Active sessions

The **Active** tab displays each currently present session as its own row with user, email, client machine, sign-in time, online duration, relative last-seen time, and Depot version. **Online Users** counts distinct users while **Active Sessions** counts individual login instances.

Administrators with `UserSessions.Terminate` can terminate the selected session or terminate every open session for the selected user. Destructive actions require confirmation and use `AdministrativeLogout`. Affected clients are returned to sign-in when their next heartbeat detects that the server-side session has ended.

## History

The **History** tab shows the 200 most recently ended sessions. It includes the user, client, sign-in time, duration, end time, Depot version, and end reason such as `LoggedOut`, `ApplicationClosed`, `Revoked`, or `AdministrativeLogout`. Search filters both active sessions and history locally by user, email, or client.

## Presence behavior

Depot sends a heartbeat every 30 seconds. A session is considered active only while its last heartbeat is within the 90-second presence timeout. A normal logout records `LoggedOut`; a clean application exit records `ApplicationClosed`. If a client crashes, loses power, loses its network connection, enters standby, or is terminated, the stale session automatically disappears from the online view after the timeout.

The historical session row remains stored. Heartbeats are not written to the audit log. Administrative session termination is audited.

## Privacy

Session presence stores only the authenticated user identifier, generated session identifier, timestamps, generated client-instance identifier, display-only machine name, Depot version, end state, and optimistic-concurrency version. Depot does not collect MAC addresses, hardware fingerprints, key logging, operating-system activity, external-window tracking, IP addresses, or geolocation for this feature.
