# Security Center

Use **Administration → Security Center** to monitor authentication risk, lockouts, session-security administration events, and review status.

## Permissions

- `SecurityEvents.View` is required to open the Security Center and read events and metrics.
- `SecurityEvents.Manage` is additionally required to mark events as reviewed.
- Service-layer authorization is authoritative; hiding UI controls does not grant or revoke access.

## What Depot records

Security Events are an append-only operational security stream separate from the business Audit Log. Events can include:

- successful authentication;
- failed authentication;
- repeated failures that cross the suspicious threshold;
- account-key lockout and attempts made while lockout is active;
- successful authentication after recent failures;
- administrative session termination;
- session-policy changes.

The event stores the time, event type, severity, optional user/account reference, optional session/client context, summary, details, and review state.

## Suspicious authentication rules

Depot uses deterministic rules rather than opaque risk scoring:

- failures 1–2: informational authentication failures;
- failure 3 in the active 15-minute window: suspicious pattern, Warning;
- failure 4: High severity;
- failure 5: Critical lockout event;
- attempts while the account key remains blocked: Critical;
- a successful login after recent failures is recorded separately, with elevated severity when the preceding failure count was high.

These rules reuse the same in-process 15-minute authentication-throttling window. They do not claim that a suspicious event proves account compromise.

## Metrics

The top cards summarize the last 24 hours:

- **Events 24h** — all security events.
- **Suspicious 24h** — suspicious-failure, blocked-login, and success-after-failures events.
- **High Risk Open** — unreviewed events at High or Critical severity.
- **Blocked 24h** — lockout-related events.

## Review workflow

Use search, minimum severity, and **Only unreviewed** to narrow the list. Users with `SecurityEvents.Manage` can select an open event and choose **Mark reviewed**. Review changes only the review metadata and optimistic version; it does not rewrite the original event.

## Notifications

High and Critical security events also create a system notification for active users who hold `SecurityEvents.View`. Security-event persistence is best-effort relative to authentication: a temporary telemetry failure is logged diagnostically but does not make a valid user login fail solely because the event could not be stored.

## Privacy boundary

This version does not collect source IP addresses, geolocation, MAC addresses, hardware fingerprints, typed text, key values, or mouse coordinates. Account identifiers are normalized for correlation; machine name is stored only when an existing session administration action already provides it.

## Related topics

- [User Sessions](topic:administration.user-sessions)
- [Users and Roles](topic:administration.users)
- [Audit Log](topic:administration.audit-log)
