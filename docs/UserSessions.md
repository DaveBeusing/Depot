# User Sessions and Online Presence

Updated: 2026-08-31

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

No parallel authentication stack is introduced. `AuthenticationService` remains the login boundary, `AuthorizationService` remains the current identity/RBAC boundary, and `SessionService` owns the authenticated-session lifecycle and heartbeat.

## Terminology

- **User Session** — one authenticated Depot login instance.
- **Active Session** — a session with `EndedUtc IS NULL` and `LastSeenUtc` within the configured presence timeout.
- **Online User** — a user with at least one active session.
- **Heartbeat** — a technical presence signal that advances `LastSeenUtc`; it is not a business audit event.
- **Multi-Session** — a user may have multiple concurrent Depot sessions on different clients.

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

Crash, hard process termination, power loss, network loss and standby require no special cleanup path. The last persisted heartbeat simply becomes stale and the session falls out of the active-presence query after the timeout. The historical session row remains available for future lifecycle/security extensions.

The heartbeat update itself includes `EndedUtc IS NULL`. Therefore a heartbeat that finishes after logout cannot reactivate or refresh an already ended session.

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

Indexes cover the unique session identifier, `UserId`, and the presence predicate `(EndedUtc, LastSeenUtc)`. The provider-specific DDL remains inside the existing database abstraction and is available for SQLite, SQL Server and MySQL/MariaDB.

The shared core database schema remains version 30. Session persistence is a feature-local schema and introduces `UserSessionSchemaMigration.CurrentVersion = 1`, advancing the `UserSessions` feature version from 0 to 1 through `DepotFeatureVersions`. This follows the existing independently versioned feature-schema pattern.

## Administration and RBAC

The administration workspace exposes **Administration → User Sessions**. The existing `Users.View` permission is reused because it already represents administrative visibility of user identity data. Authorization is checked in `UserSessionAdministrationService`; hiding the UI is not treated as the security boundary.

The view shows each active session separately with user, email, client, sign-in time, relative last-seen time and Depot version. The exact local last-seen time is available as detail/tooltip. Search is performed over the small active-session result set for display name, email and machine name.

While the view is open it polls approximately every 20 seconds. A semaphore prevents overlapping refreshes, navigation/unload cancels polling, and cancellation/disposal checks prevent late refreshes from updating a closed ViewModel.

## Dashboard metrics

Dashboard presence metrics use the same heartbeat cutoff rule:

```text
Online Users    = COUNT(DISTINCT UserId) over active sessions
Active Sessions = COUNT(*) over active sessions
```

A user logged in on an office PC and a notebook therefore contributes one online user and two active sessions. The dashboard presence card navigates to the administration session view when the current user has the corresponding existing permission.

## Audit behavior

Heartbeats are intentionally not audited. They are technical liveness writes and would otherwise flood business evidence. Existing authentication/user audit behavior remains authoritative; this feature does not create duplicate login/logout audit streams.

## Privacy and security boundary

The feature stores only data necessary for authenticated-session presence and display: user/session identifiers, timestamps, generated client-instance identifier, machine name, Depot version, end state and optimistic version.

It does not collect MAC addresses, hardware fingerprints, key logging, operating-system activity, external-window activity, IP/geolocation data or other behavioral tracking.

## Extension boundary

The persistence model leaves room for future end reasons such as `Revoked`, `Expired`, `Superseded` and `AdministrativeLogout`, but this branch does not implement remote logout, revocation UI, single-session enforcement, concurrent-session limits, suspicious-login detection, IP tracking, geolocation, full session-history UI or security alerts.
