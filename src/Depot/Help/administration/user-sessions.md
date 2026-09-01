# User Sessions

Use **Administration → User Sessions** to review authenticated clients, configure lifetime/concurrency/retention policy and terminate sessions.

## Permissions

- `Users.View` — view active/history sessions and policy.
- `UserSessions.Terminate` — terminate one or all open sessions for a selected user.
- `Settings.Manage` — change session policy.

## Session policy

The central policy controls:

- idle timeout: default 30 minutes, range 5–480;
- maximum lifetime: default 12 hours, range 1–168;
- concurrent mode: Unlimited, MaximumSessions or SingleSession;
- maximum concurrent sessions: 1–20 when MaximumSessions is selected;
- limit action: RejectNewSession or SupersedeOldestSession;
- ended-session history retention: default 180 days, range 30–3650.

A finite session limit is enforced atomically across clients. Replacing the oldest login records `Superseded`. A rejected login creates no new session.

## Credential and account changes

A password change invalidates other sessions with `CredentialsChanged`; an administrative reset invalidates all target-user sessions. Deactivating a user records `Revoked` for open sessions in the same transaction as the account-state change.

## Active, History and retention

**Active** shows heartbeat-present login instances. **Online Users** counts distinct users and **Active Sessions** counts login rows. **History** shows the 200 most recent retained ended sessions. A bounded background process physically removes ended sessions older than the configured retention period.

Administrative termination records `AdministrativeLogout` and remains Audit-relevant. Affected running clients return to sign-in after heartbeat detection.

## Presence and privacy

Heartbeat runs every 30 seconds; active presence expires after 90 seconds without a fresh heartbeat. Depot stores only the latest keyboard/mouse/touch activity timestamp received inside Depot. It does not record typed text, key values, coordinates, source IP, geolocation, MAC address or hardware fingerprint.

## Related topics

- [Security Center](topic:administration.security-center)
- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
- [Dashboard](topic:getting-started.dashboard)
