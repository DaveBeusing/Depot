# User Sessions

Use **Administration → User Sessions** to see authenticated Depot clients that are currently considered present. Access requires the existing `Users.View` permission and is enforced in the service layer.

## Definitions

- **User Session** — one authenticated Depot login instance.
- **Active Session** — a session with no end timestamp whose `LastSeenUtc` is within the configured presence timeout.
- **Online User** — a user with at least one active session.
- **Heartbeat** — the technical presence signal that advances `LastSeenUtc`; it is not a business audit event.
- **Multi-Session** — one user may have multiple simultaneous Depot sessions, for example an office PC and a notebook.

## What the page shows

The page displays each active session as its own row with the user, email, client machine name, sign-in time, relative last-seen time, and Depot version. Hover **Last Seen** for the exact local timestamp. The search box filters by user, email, or client name.

The **Online Users** metric counts distinct users. **Active Sessions** counts session rows, so a user signed in on two clients contributes one online user and two active sessions.

## Presence behavior

Depot sends a heartbeat every 30 seconds. A session is considered active only while its last heartbeat is within the 90-second presence timeout. A normal logout records `LoggedOut`; a clean application exit records `ApplicationClosed`. If a client crashes, loses power, loses its network connection, enters standby, or is terminated, no special cleanup is required: the stale session automatically disappears from the online view after the timeout.

The historical session row remains stored. Heartbeats are not written to the audit log.

## Privacy

Session presence stores only the authenticated user identifier, generated session identifier, timestamps, generated client-instance identifier, display-only machine name, Depot version, end state, and optimistic-concurrency version. Depot does not collect MAC addresses, hardware fingerprints, key logging, operating-system activity, external-window tracking, or geolocation for this feature.
