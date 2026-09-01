# Security Center and Authentication Risk Monitoring

Updated: 2026-09-01

Depot now maintains an operational security-event stream for authentication and session-security administration. The stream is intentionally separate from the business Audit Log: Audit remains the retained business/change evidence source, while `SecurityEvents` captures security observations, risk signals and review workflow.

## Architecture

```text
Authentication / Session Administration
            ↓
     SecurityEventService
            ↓
   SecurityEventRepository
            ↓
      SecurityEvents
            ↓
SQLite / SQL Server / MySQL-MariaDB
```

`SecurityEventService` is the policy boundary. Views and ViewModels never write security events directly. Authentication telemetry is best-effort so a valid login is not rejected solely because the security-event store or notification path is temporarily unavailable.

## Deterministic suspicious-login rules

Depot does not use opaque scoring. It reuses the existing in-process 15-minute login-throttling window:

- failures 1–2: `AuthenticationFailed`, Information;
- failure 3: `SuspiciousAuthenticationFailures`, Warning;
- failure 4: `SuspiciousAuthenticationFailures`, High;
- failure 5: `AuthenticationBlocked`, Critical and the existing 15-minute lockout begins;
- attempts during an active lockout: `AuthenticationBlocked`, Critical;
- successful login after recent failures: `AuthenticationSucceededAfterFailures`, Warning or High depending on the preceding count;
- ordinary successful login: `AuthenticationSucceeded`, Information.

A suspicious event is a triage signal, not proof of compromise.

## Session-security events

Administration also emits Security Events when an administrator terminates a session or changes the central idle/max-age policy. These events complement, rather than replace, the existing Audit evidence for administrative changes.

## Security Center UI

**Administration → Security Center** requires `SecurityEvents.View` and shows:

- Events in the last 24 hours;
- suspicious authentication events in the last 24 hours;
- unreviewed High/Critical events;
- lockout events in the last 24 hours;
- the 250 most recent matching events with time, severity, type, account/client context, summary and review state.

Filtering supports free-text search, minimum severity and an unreviewed-only mode.

`SecurityEvents.Manage` additionally permits **Mark reviewed**. Review updates only `ReviewedUtc`, `ReviewedByUserId` and optimistic `Version`; the original event fields are immutable through normal application workflows.

## Notifications

High and Critical events are also published through the existing Notification Center to active users holding `SecurityEvents.View`. The Security Center remains the detailed source for investigation and review.

## Persistence and schema

Security Events use a dedicated feature schema:

- feature name: `SecurityEvents`
- current feature schema: **1**
- core schema remains **30**
- User Sessions feature schema remains **2**

The schema is provider-neutral for SQLite, SQL Server and MySQL/MariaDB. Indexes cover timestamp/severity, user and review-state queries.

## Privacy boundary

The initial Security Center deliberately avoids IP/geolocation-based scoring because Depot does not yet have a controlled network-identity collection contract. This implementation does not add source IP, geolocation, MAC address, hardware fingerprint, typed text, key values, mouse coordinates or external-window activity.

Normalized account identifiers are stored to correlate authentication attempts. Machine name is included only for events originating from session administration where that information already exists in the session record.

## Current extension boundary

Implemented now: authentication failures, suspicious failure escalation, lockout events, success-after-failure correlation, administrative session-termination events, session-policy-change events, High/Critical notifications, Security Center metrics/filtering and review workflow.

Future security work includes password-change session invalidation, concurrent-session policy, persistence/shared-store throttling for multi-node deployments, session/security-event retention and archival, richer alert routing, MFA and external identity providers. IP/geolocation or device-trust signals require an explicit privacy/security design before implementation.
