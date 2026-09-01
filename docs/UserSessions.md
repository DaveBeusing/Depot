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

No parallel authentication stack is introduced. `AuthenticationService` remains the login boundary, `AuthorizationService` remains the current identity/RBAC boundary, and `SessionService` owns the authenticated-session lifecycle, heartbeat and client-side revocation response.

## Terminology

- **User Session** — one authenticated Depot login instance.
- **Active Session** — a session with `EndedUtc IS NULL` and `LastSeenUtc` within the configured presence timeout.
- **Online User** — a user with at least one active session.
- **Ended Session** — a session with `EndedUtc` and an `EndReason`; the 200 most recently ended sessions are exposed in Administration history.
- **Heartbeat** — a technical presence signal that advances `LastSeenUtc`; it is not a business audit event.
- **Multi-Session** — a user may have multiple concurrent Depot sessions on different clients.
- **Revocation** — server-side session termination that the running client detects on a subsequent heartbeat and converts into a local sign-out/re-authentication flow.

There is deliberately no persisted `IsOnline` source of truth.

```text
active = EndedUtc IS NULL
         AND LastSeenUtc >= UtcNow - PresenceTimeout
```

Central defaults are 30 seconds for the heartbeat interval, 90 seconds for the presence timeout, 20 seconds for the administration refresh interval and a bounded three-second shutdown write window.

## Authentication and lifecycle

A successful login performs the following sequence:

```text
Authenticate user
→ load roles/effective permissions
→ create UserSession
→ retain current SessionId
→ start heartbeat
→ publish the authenticated AuthorizationService identity
```

A failed login does not create a session. A normal logout first marks the current session `EndedUtc = UtcNow` with `EndReason = LoggedOut`, stops the heartbeat and signs the authorization context out. A clean application exit uses `EndReason = ApplicationClosed` with a bounded database write so shutdown cannot wait indefinitely.

Crash, hard process termination, power loss, network loss and standby require no special cleanup path. The last persisted heartbeat becomes stale and the session falls out of the active-presence query after the timeout. Because no explicit end was recorded, such a stale-but-open row is not presented as an ended historical session until a later lifecycle action closes it.

The heartbeat update includes `EndedUtc IS NULL`. Therefore a heartbeat that finishes after logout or administrative termination cannot reactivate or refresh an ended session.

## Administrative revocation

`UserSessionAdministrationService` separates visibility from destructive control:

- `Users.View` is required to load active sessions, history and presence metrics.
- `UserSessions.Terminate` is additionally required to terminate one session or all open sessions for a selected user.

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

The shared core database schema remains version 30. Session persistence is a feature-local schema with `UserSessionSchemaMigration.CurrentVersion = 1`, tracked through `DepotFeatureVersions`. Session history and revocation reuse the original columns and therefore do not require another schema migration.

## Administration UI

**Administration → User Sessions** contains:

- **Online Users** — distinct users within the presence timeout.
- **Active Sessions** — individual active login instances.
- **Active** tab — user, email, client, online-since time, online duration, relative last-seen time and Depot version.
- **History** tab — the 200 most recently ended sessions with user, client, sign-in time, duration, end time, Depot version and end reason.
- local search over display name, email and machine name for both active and historical rows.
- `Terminate session` and `Terminate all for user` when `UserSessions.Terminate` is granted.

While the view is loaded it polls approximately every 20 seconds. A semaphore prevents overlapping refreshes, navigation/unload cancels polling, and cancellation/disposal checks prevent late refreshes from updating a closed ViewModel.

## Dashboard metrics

Dashboard presence metrics use the same heartbeat cutoff rule:

```text
Online Users    = COUNT(DISTINCT UserId) over active sessions
Active Sessions = COUNT(*) over active sessions
```

A user logged in on an office PC and a notebook therefore contributes one online user and two active sessions. The dashboard administration card exposes both values and navigates to User Sessions for authorized users.

## Audit behavior

Heartbeats are intentionally not audited because they are high-frequency liveness writes. Administrative session termination is audited. User deactivation already produces user Audit evidence and now atomically revokes the user's open sessions.

Session history is operational lifecycle data stored in `UserSessions`; it is not a substitute for the immutable business Audit log.

## Privacy and security boundary

The feature stores only data necessary for authenticated-session lifecycle, presence and administration: user/session identifiers, timestamps, generated client-instance identifier, display-only machine name, Depot version, end state and optimistic version.

It does not collect MAC addresses, hardware fingerprints, key logging, operating-system activity, external-window activity, IP addresses, geolocation or other behavioral tracking.

## Current extension boundary

Implemented end reasons include normal logout, clean application close, administrative logout and revocation on user deactivation; the domain also reserves `Expired` and `Superseded` for later policy work.

Not implemented in this branch: configurable idle timeout, maximum session age, concurrent-session limits, password-change session policy, suspicious-login detection, IP/geolocation analysis, security alerts, session-history retention/archival policy, MFA or external identity-provider integration.
