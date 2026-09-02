# User Sessions and Online Presence

Updated: 2026-09-01

Depot persists one `UserSession` for every successful authenticated login and derives online presence from heartbeat freshness. The feature follows the application layering `Views → ViewModels → Services → Repositories → DatabaseAccess` and remains provider-neutral for SQLite, SQL Server and MySQL/MariaDB.

## Presence and lifecycle

An active session is an open row (`EndedUtc IS NULL`) whose `LastSeenUtc` is within the 90-second presence timeout. There is no persisted `IsOnline` source of truth. The heartbeat runs every 30 seconds and updates only still-open sessions. User activity is represented only by the latest keyboard/mouse/touch timestamp received inside Depot; typed input, key values, coordinates and external-window activity are not retained.

Normal logout records `LoggedOut`, clean shutdown records `ApplicationClosed`, policy expiration records `Expired`, administrative termination records `AdministrativeLogout`, account deactivation records `Revoked`, credential changes may record `CredentialsChanged`, and a concurrent-session replacement records `Superseded`.

## Central session policy

`UserSessionPolicy` is shared by every Depot client and is optimistic-versioned. Defaults are:

- idle timeout: **30 minutes** (5–480);
- maximum session age: **12 hours** (1–168);
- concurrent mode: **Unlimited**;
- configured maximum concurrent sessions: **3** (1–20 when `MaximumSessions` is selected);
- limit action: **RejectNewSession**;
- ended-session history retention: **180 days** (30–3650).

Concurrent modes are `Unlimited`, `MaximumSessions` and `SingleSession`. When a finite limit is reached, Depot either rejects the new login or atomically ends the oldest open session as `Superseded` before creating the new session. The session-policy row is used as the serialization point so competing clients cannot independently exceed the configured limit.

Idle and maximum-age expiration remain independent. Maximum age is absolute even while activity continues. The heartbeat persists the newest activity timestamp before applying policy predicates, preventing a recent input event from being lost at the idle boundary.

`Users.View` permits session visibility, `Settings.Manage` permits policy changes, and `UserSessions.Terminate` permits destructive session control.

## Credential changes and account state

Changing a password invalidates other open sessions for the target account in the same transaction as the credential update and Audit evidence. A self-service change retains the current session and ends the other sessions as `CredentialsChanged`; an administrative reset invalidates all open sessions for the target user.

Deactivating an account ends all open sessions as `Revoked` in the same transaction as the account-state change and Audit record. The currently authenticated administrator cannot deactivate their own account through this workflow.

## Administration and retention

**Administration → User Sessions** exposes Online Users, Active Sessions, the complete central policy, Active/History tabs, search, single-session termination and terminate-all-for-user actions. Policy changes are optimistic-versioned and Audit-relevant.

A bounded background maintenance service enforces `SessionHistoryRetentionDays`. Each run deletes only ended sessions older than the current cutoff, in fixed batches of 250 and no more than four batches per run. Deletes repeat the cutoff predicate inside the write transaction and use the session-policy lock, so concurrent Depot clients can run maintenance safely and idempotently.

History displays the 200 most recent retained ended sessions; retention controls persistence, not only UI visibility.

## Security-event integration

Session administration, policy changes, credential invalidation, revocation and supersession can produce correlated Security Events in addition to required Audit evidence. Security Events carry the existing session identifier, generated process-level `ClientInstanceId` and machine display name where available. This is correlation metadata, not hardware fingerprinting.

Authentication failures, shared throttling, lockouts and investigation are documented in [Security Center and Authentication Risk Monitoring](SecurityCenter.md).

## Schema

- Core database schema: **30**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**

User Sessions schema 1 introduced session persistence and presence indexes, schema 2 introduced lifetime policy, and schema 3 adds concurrent-session mode/action/limit and session-history retention. No schema change is required for the bounded maintenance implementation.

## Privacy boundary

Depot does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates, external-window activity or operating-system activity as part of this feature.

## Extension boundary

Remaining identity/security extensions are MFA, OIDC/SSO/external identity providers, optional deployment-specific alert routing, and any future IP/geolocation/device-trust design only after an explicit privacy and threat-model decision.
