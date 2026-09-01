# User Sessions

Use **Administration → User Sessions** to review authenticated Depot clients, the active session policy, recent session history and administrative session termination.

## Permissions

- `Users.View` is required to open the session overview and read active/history data and the current policy.
- `UserSessions.Terminate` is additionally required to terminate one session or all open sessions for a selected user.
- `Settings.Manage` is required to change the idle timeout or maximum session age.
- Hiding or disabling a control is not the security boundary; the service layer enforces the permissions.

## Definitions

- **Active Session** — no `EndedUtc` value and the last heartbeat is within the 90-second presence timeout.
- **Online User** — a user with at least one active session.
- **Heartbeat** — a 30-second technical liveness update; it is not an Audit event.
- **Idle timeout** — maximum time without keyboard, mouse or touch activity inside Depot.
- **Maximum session age** — absolute session lifetime even while the user remains active.
- **History** — the 200 most recently ended sessions.

## Session policy

The **Session policy** card shows the centrally stored limits used by every Depot client.

Default values are:

- **Idle timeout:** 30 minutes.
- **Maximum session age:** 12 hours.

Supported ranges are 5–480 minutes for idle timeout and 1–168 hours for maximum session age.

If you have `Settings.Manage`:

1. Enter the required idle timeout in minutes.
2. Enter the required maximum session age in hours.
3. Choose **Save policy**.
4. Confirm the change.
5. Depot saves the policy with optimistic concurrency control and evaluates all still-open sessions against the new limits.

A stricter policy can immediately mark existing sessions `Expired`. Affected running clients return to sign-in when they detect the ended session on their next heartbeat.

Depot does not write every input event to the database. It remembers only the latest in-application activity timestamp and sends it with the normal heartbeat. Typed text, key values and mouse coordinates are not stored.

## Active tab

The **Active** tab shows one row per currently present login instance with user, email, client, Online Since, Online For, Last Seen and Depot version.

The KPI cards use different counts:

- **Online Users** counts distinct users.
- **Active Sessions** counts login instances, so one user on two clients contributes one online user and two active sessions.

Use the search box to filter by user name, email or client machine.

## Terminate a session

If you have `UserSessions.Terminate`:

1. Select an active session.
2. Choose **Terminate session**.
3. Confirm the destructive action.
4. Depot ends the server-side session with `AdministrativeLogout` and records Audit evidence.
5. The affected client detects the ended session on its next heartbeat, clears its local authenticated state and returns to sign-in.

## Terminate all sessions for a user

If the selected user has more than one open session, choose **Terminate all for user** and confirm. Depot ends every still-open session for that user with `AdministrativeLogout`. Each affected client is returned to sign-in after heartbeat detection.

Deactivating a user from **Administration → Users** is a separate account action. Deactivation revokes all still-open sessions with `Revoked` in the same transaction as the account-state change.

## History tab

The **History** tab shows the 200 most recently ended sessions with user, email, client, Signed In, Duration, Ended, End Reason and Depot version.

Common end reasons are:

- `LoggedOut` — normal user logout.
- `ApplicationClosed` — clean application exit.
- `Expired` — idle timeout or maximum session age was reached.
- `AdministrativeLogout` — administrator terminated one or more sessions.
- `Revoked` — the account was deactivated while sessions were still open.

History is session lifecycle information. It does not replace the immutable business Audit Log.

## Presence and expiration behavior

If a client crashes, loses power, loses connectivity, enters standby or is killed, no explicit end callback is guaranteed. Its last heartbeat becomes stale and it disappears from the Active view after the presence timeout. Such a stale open row is not automatically rewritten as a specific logout reason.

For a running client, the heartbeat first persists the latest Depot activity timestamp and then checks the central policy. This prevents recent input from being lost at the idle-timeout boundary. Maximum session age always wins regardless of activity.

Temporary heartbeat database failures are contained and retried on the next normal interval; they are not automatically treated as proof that the session was revoked or expired.

## Privacy

Session data is limited to authenticated user/session identifiers, timestamps, a generated client-instance identifier, display-only machine name, Depot version, end state, policy values and optimistic versions. Activity tracking stores only the time when input occurred inside Depot. Depot does not collect typed text, key values, mouse coordinates, MAC addresses, hardware fingerprints, OS/window activity, IP addresses or geolocation for this feature.

## Related topics

- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
- [Dashboard](topic:getting-started.dashboard)
