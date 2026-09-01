# User Sessions and Online Presence

Updated: 2026-09-01

Depot persists one `UserSession` for every successful authenticated login and derives online presence from heartbeat freshness. The feature follows the existing application layering:

```text
Views
  ↓
ViewModels
  ↓
Services
  ↓
Repositories
  ↓
DatabaseAccess
  ↓
SQLite / SQL Server / MySQL-MariaDB
```

No parallel authentication stack is introduced. `AuthenticationService` remains the login boundary, `AuthorizationService` remains the current identity/RBAC boundary, and `SessionService` owns the authenticated-session lifecycle, heartbeat, activity timestamp and client-side termination response.

## Terminology

- **User Session** — one authenticated Depot login instance.
- **Active Session** — a session with `EndedUtc IS NULL` and `LastSeenUtc` within the configured presence timeout.
- **Online User** — a user with at least one active session.
- **Ended Session** — a session with `EndedUtc` and an `EndReason`; the 200 most recently ended sessions are exposed in Administration history.
- **Heartbeat** — a technical presence signal that advances `LastSeenUtc`; it is not a business audit event.
- **User activity** — keyboard, mouse or touch input received by the Depot main window. Depot stores only the most recent activity timestamp, not the input content.
- **Idle timeout** — maximum time without recorded Depot input before the session expires.
- **Maximum session age** — absolute lifetime of a session regardless of continuing activity.
- **Multi-Session** — a user may have multiple concurrent Depot sessions on different clients.
- **Revocation** — server-side session termination that the running client detects on a subsequent heartbeat and converts into a local sign-out/re-authentication flow.

There is deliberately no persisted `IsOnline` source of truth.

```text
active = EndedUtc IS NULL
         AND LastSeenUtc >= UtcNow - PresenceTimeout
```

Central runtime defaults are 30 seconds for the heartbeat interval, 90 seconds for the presence timeout, 20 seconds for the administration refresh interval and a bounded three-second shutdown write window.

## Authentication and lifecycle

A successful login performs the following sequence:

```text
Authenticate user
→ load roles/effective permissions
→ create UserSession with LastActivityUtc = UtcNow
→ retain current SessionId
→ start heartbeat
→ publish the authenticated AuthorizationService identity
```

A failed login does not create a session. A normal logout first marks the current session `EndedUtc = UtcNow` with `EndReason = LoggedOut`, stops the heartbeat and signs the authorization context out. A clean application exit uses `EndReason = ApplicationClosed` with a bounded database write so shutdown cannot wait indefinitely.

Crash, hard process termination, power loss, network loss and standby require no special cleanup path. The last persisted heartbeat becomes stale and the session falls out of the active-presence query after the timeout. Because no explicit end was recorded, such a stale-but-open row is not presented as an ended historical session until a later lifecycle action closes it.

The heartbeat update includes `EndedUtc IS NULL`. Therefore a heartbeat that finishes after logout, policy expiration or administrative termination cannot reactivate or refresh an ended session.

## Configurable session policy

Session security policy is stored centrally in the Depot database so every client uses the same limits. The singleton `UserSessionPolicy` row contains:

- `IdleTimeoutMinutes`
- `MaximumSessionAgeHours`
- `UpdatedUtc`
- optimistic `Version`

Default policy:

```text
Idle timeout:        30 minutes
Maximum session age: 12 hours
```

Supported configuration ranges are 5–480 minutes for idle timeout and 1–168 hours for maximum session age. `Users.View` may read the active policy through **Administration → User Sessions**; changing it additionally requires `Settings.Manage`.

Depot captures activity only while the main window receives keyboard, mouse or touch input. Activity updates are throttled in memory and persisted with the normal heartbeat rather than creating a database write for every input event. The heartbeat first persists the latest activity timestamp, then evaluates the current centrally stored policy. This ordering prevents legitimate activity immediately before the idle boundary from being discarded.

A session expires when either rule is true:

```text
UtcNow - LastActivityUtc >= IdleTimeout
OR
UtcNow - StartedUtc >= MaximumSessionAge
```

For legacy rows where `LastActivityUtc` is null, policy cleanup falls back to `LastSeenUtc` and then `StartedUtc`. An expired session is ended with `EndReason = Expired`, the local authorization context is cleared, and the application returns to the standard sign-in flow. Maximum session age is absolute and therefore expires a session even if the user remains active.

Saving a stricter policy immediately evaluates all still-open sessions. Sessions already beyond the new limits are marked `Expired`; affected running clients detect the ended row on their next heartbeat. Policy updates use optimistic concurrency and are recorded in Audit when the administration service is composed with `AuditService`.

## Administrative revocation

`UserSessionAdministrationService` separates visibility from destructive control:

- `Users.View` is required to load active sessions, history, presence metrics and the current policy.
- `UserSessions.Terminate` is additionally required to terminate one session or all open sessions for a selected user.
- `Settings.Manage` is required to change idle timeout or maximum session age.

Single-session termination writes `AdministrativeLogout`. Bulk termination ends every still-open session for the selected user with the same reason. Both actions require confirmation in the UI and are recorded as Audit actions when the administration service is composed with `AuditService`.

The next heartbeat from an administratively terminated client receives no successful heartbeat update because the repository predicate only updates unended rows. `SessionService` then clears the local authenticated context and requests re-authentication; the application returns to the normal sign-in flow. Temporary database/transport failures are handled separately and do not automatically become revocation signals.

Deactivating a user is stronger than merely preventing a future login. `UserService.SetActiveAsync(..., false, ...)` ends every open session for that user with `EndReason = Revoked` in the same database transaction as the account deactivation and its Audit evidence. The currently signed-in user cannot deactivate their own account through this workflow.

## Persistence and schema

`UserSessions` contains:

- `Id`
- unique `SessionId`
- `UserId`
- `StartedUtc`
- `LastSeenUtc`
- nullable `LastActivityUtc`
- nullable `EndedUtc`
- nullable `EndReason`
- generated `ClientInstanceId`
- display-only `MachineName`
- `AppVersion`
- optimistic `Version`

There is no uniqueness constraint on `UserId`; multiple simultaneous sessions for one user are intentional.

Indexes cover the unique session identifier, `UserId`, and the presence predicate `(EndedUtc, LastSeenUtc)`. Provider-specific DDL remains inside the existing database abstraction and is available for SQLite, SQL Server and MySQL/MariaDB.

The shared core database schema remains version 30. Session persistence is a feature-local schema with `UserSessionSchemaMigration.CurrentVersion = 2`, tracked through `DepotFeatureVersions`. Version 2 adds the central `UserSessionPolicy` singleton. The 1→2 policy migration, default seed and feature-version update execute in one write transaction.

## Administration UI

**Administration → User Sessions** contains:

- **Online Users** — distinct users within the presence timeout.
- **Active Sessions** — individual active login instances.
- **Session policy** — current idle timeout and maximum session age; editable with `Settings.Manage`.
- **Active** tab — user, email, client, online-since time, online duration, relative last-seen time and Depot version.
- **History** tab — the 200 most recently ended sessions with user, client, sign-in time, duration, end time, Depot version and end reason, including `Expired`.
- local search over display name, email and machine name for both active and historical rows.
- `Terminate session` and `Terminate all for user` when `UserSessions.Terminate` is granted.

While the view is loaded it polls approximately every 20 seconds. A semaphore prevents overlapping refreshes, navigation/unload cancels polling, and cancellation/disposal checks prevent late refreshes from updating a closed ViewModel. Polling does not overwrite unsaved policy edits.

## Dashboard metrics

The **User Presence** dashboard card uses the same session source of truth and exposes five metrics:

```text
Online Users           = COUNT(DISTINCT UserId) over heartbeat-active sessions
Active Sessions        = COUNT(*) over heartbeat-active sessions
Sessions Today         = sessions whose StartedUtc falls within the current local calendar day
Admin Logouts Today    = sessions ended today with AdministrativeLogout
Revoked Sessions Today = sessions ended today with Revoked
```

The current client's local midnight is translated to UTC before querying the UTC session timestamps. A user logged in on an office PC and a notebook therefore contributes one online user and two active sessions, while both logins contribute to `Sessions Today` when they started during the same local day.

The card remains visible only to administrators or users with `Users.View` and navigates to User Sessions. The separate Reports dashboard card is permissioned independently through `Reports.View`; Reports visibility is not coupled to user-session administration.

## Audit behavior

Heartbeats and raw user-input events are intentionally not audited because they are high-frequency liveness signals. Only the resulting latest activity timestamp is retained in `UserSessions`. Administrative session termination and session-policy changes are audited. User deactivation produces user Audit evidence and atomically revokes the user's open sessions.

Session history is operational lifecycle data stored in `UserSessions`; it is not a substitute for the immutable business Audit log.

## Privacy and security boundary

The feature stores only data necessary for authenticated-session lifecycle, presence, policy enforcement and administration: user/session identifiers, timestamps, generated client-instance identifier, display-only machine name, Depot version, end state, policy values and optimistic versions.

It does not collect key values, mouse coordinates, typed text, MAC addresses, hardware fingerprints, operating-system activity, external-window activity, IP addresses, geolocation or other behavioral tracking. Activity detection is limited to whether input occurred inside Depot and stores only the resulting timestamp.

## Current extension boundary

Implemented end reasons include normal logout, clean application close, policy expiration, administrative logout and revocation on user deactivation; the domain reserves `Superseded` for later concurrent-session policy work.

Not implemented in this branch: concurrent-session limits, password-change session policy, suspicious-login detection, IP/geolocation analysis, security alerts, session-history retention/archival policy, MFA or external identity-provider integration.
