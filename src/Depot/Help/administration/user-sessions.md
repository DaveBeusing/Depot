# User Sessions

Use **Administration → User Sessions** to review authenticated Depot clients, the active session policy, recent session history and administrative session termination.

## Permissions

- `Users.View` is required to read active/history data and the current policy.
- `UserSessions.Terminate` is additionally required to terminate one session or all open sessions for a selected user.
- `Settings.Manage` is required to change idle timeout or maximum session age.
- UI visibility is not the security boundary; service-layer permissions are authoritative.

## Definitions

- **Active Session** — no `EndedUtc` value and the last heartbeat is within the 90-second presence timeout.
- **Online User** — a user with at least one active session.
- **Heartbeat** — a 30-second technical liveness update; it is not an Audit event.
- **Idle timeout** — maximum time without keyboard, mouse or touch activity inside Depot.
- **Maximum session age** — absolute session lifetime even while the user remains active.
- **History** — the 200 most recently ended sessions.

## Session policy

The **Session policy** card shows the centrally stored limits used by every Depot client. Defaults are 30 minutes idle timeout and 12 hours maximum session age. Supported ranges are 5–480 minutes and 1–168 hours respectively.

With `Settings.Manage`, enter the required limits and choose **Save policy**. Depot uses optimistic concurrency and evaluates all still-open sessions against the new limits. A stricter policy can immediately mark existing sessions `Expired`; affected clients return to sign-in after heartbeat detection.

Depot remembers only the latest in-application activity timestamp and sends it with the normal heartbeat. Typed text, key values and mouse coordinates are not stored.

## Active and History tabs

The **Active** tab shows one row per currently present login instance with user, email, client, Online Since, Online For, Last Seen and Depot version. **Online Users** counts distinct users while **Active Sessions** counts login instances.

The **History** tab shows the 200 most recently ended sessions with user, email, client, Signed In, Duration, Ended, End Reason and Depot version. Common reasons are `LoggedOut`, `ApplicationClosed`, `Expired`, `AdministrativeLogout` and `Revoked`.

## Administrative termination

With `UserSessions.Terminate`, select a session and choose **Terminate session**, or use **Terminate all for user**. Depot ends affected sessions with `AdministrativeLogout`; the clients return to sign-in after heartbeat detection. These actions remain Audit-relevant administration changes and also create operational Security Events.

Deactivating a user from **Administration → Users** is separate: it revokes still-open sessions with `Revoked` in the same transaction as the account-state change.

## Security Center relationship

Authentication failures, suspicious repeated failures, lockouts and successful login after recent failures are monitored by the separate Security Events feature. Session-policy changes and administrative session termination also create Security Events.

Use **Administration → Security Center** with `SecurityEvents.View` to investigate those events. High/Critical authentication events are additionally surfaced in the Notification Center. `SecurityEvents.Manage` permits marking an event reviewed.

Security Events do not replace the business Audit Log and suspicious-login rules do not prove an account compromise.

## Presence and expiration behavior

Crash, power loss, connectivity loss, standby or hard termination may prevent an explicit end callback. The last heartbeat becomes stale and the session disappears from Active after the presence timeout. A stale open row is not automatically rewritten as a specific logout reason.

For a running client, heartbeat persists the latest activity before checking policy. Maximum session age applies regardless of activity. Temporary heartbeat database failures are contained and are not automatically treated as proof of revocation or expiration.

## Privacy

Session/security monitoring does not collect typed text, key values, mouse coordinates, MAC addresses, hardware fingerprints, OS/window activity, source IP addresses or geolocation. Any future IP/geolocation/device-trust signals require a separate privacy/security design.

## Related topics

- [Security Center](topic:administration.security-center)
- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
- [Dashboard](topic:getting-started.dashboard)
