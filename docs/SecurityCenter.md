# Security Center and Authentication Risk Monitoring

Updated: 2026-09-01

Depot maintains a provider-neutral operational `SecurityEvents` stream for authentication and session-security observations. Security Events complement the business Audit Log; they do not replace required Audit evidence for administrative changes.

## Authentication architecture

Local credentials are accessed through `IAuthenticationProvider`; the built-in `LocalAuthenticationProvider` is the current implementation. This preserves a clean boundary for future OIDC/SSO providers without changing session or authorization semantics.

Failed-login throttling is persisted in the shared database rather than process-local memory in the production composition. `AuthenticationSecurityPolicy` controls:

- failure window: default **15 minutes**, range 1–1440;
- lockout threshold: default **5**, range 3–20;
- lockout duration: default **15 minutes**, range 1–1440;
- Security Event retention: default **365 days**, range 30–3650.

The policy is optimistic-versioned and requires `Settings.Manage` to change. Authentication operations serialize through the singleton policy row before reading/updating the account throttle state, so separate Depot clients share the same failure count and lockout decision.

Risk escalation remains deterministic: ordinary failures are informational, repeated failures escalate to Warning/High, and a lockout is Critical. Successful login after recent failures is retained as a separate event. These signals are triage evidence, not proof of compromise.

## Security Center

**Administration → Security Center** requires `SecurityEvents.View`. It exposes six operational KPIs:

- events in the last 24 hours;
- suspicious authentication events in the last 24 hours;
- open unreviewed High/Critical events;
- blocked authentication events in the last 24 hours;
- events reviewed in the last 24 hours;
- all currently open unreviewed events.

Filtering supports search, minimum severity and unreviewed-only mode. `SecurityEvents.Manage` permits marking an event reviewed. Review changes only review metadata and optimistic `Version`; original event content is not rewritten.

## Investigation context

Selecting an event loads related events by existing `UserId`, normalized account identifier, `SessionId` or generated `ClientInstanceId`. When the viewer also has the relevant user/session permission, Depot resolves the account and its currently open sessions. Correlation therefore uses identifiers already present in authentication/session records; it does not introduce IP, geolocation or hardware fingerprinting.

## Response actions

The Security Center does not implement parallel mutation logic. It delegates to established authorized services:

- **Terminate session** → `UserSessionAdministrationService` and `UserSessions.Terminate`;
- **Terminate all sessions** → `UserSessionAdministrationService` and `UserSessions.Terminate`;
- **Deactivate user** → `UserService` and `Users.Manage`.

These paths retain their existing transaction, Audit, optimistic-concurrency and session-revocation semantics. Destructive UI actions require confirmation.

## Notifications and alert boundary

`SecurityAlertPolicy` defines the notification threshold separately from event persistence. The current default routes High and Critical events to active holders of `SecurityEvents.View` through the existing Notification Center; Critical maps to an Error notification and High to Warning. Changing future delivery channels does not require changing the Security Event persistence model.

## Retention and maintenance

A bounded background maintenance service enforces `SecurityEventRetentionDays` and cleans stale authentication-throttle rows. Each data class is processed in batches of 250, with at most four batches per run. Deletion is repeated under the relevant policy lock and with the cutoff predicate inside the transaction, making concurrent maintenance attempts idempotent and safe across multiple Depot clients.

Security Event retention affects the operational event store only. It does not delete or shorten the separate business Audit Log.

## Persistence and schema

- Core database schema: **30**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**

Security Events schema 1 introduced event/review persistence. Schema 2 adds `ClientInstanceId`, the central authentication-security policy and the shared authentication-throttle store. Provider DDL exists for SQLite, SQL Server and MySQL/MariaDB.

## Privacy boundary

The feature does not collect source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity. Machine name and process-generated `ClientInstanceId` are used only where they already belong to a Depot session.

## Extension boundary

Remaining work is external identity/MFA integration and, where a deployment requires it, configurable delivery-channel/routing implementations behind the alert boundary. IP/geolocation/device-trust signals remain out of scope until an explicit privacy and threat-model design exists.
