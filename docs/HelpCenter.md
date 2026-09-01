# Depot Help Center

Updated: 2026-09-01

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.21** contains Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Finance, Administration and Troubleshooting topics plus the current User Sessions and Security Center guidance.

The session/security help contract includes:

- `administration.user-sessions` — visible with `Users.View`; documents Active/History views, Online Users/Active Sessions metrics, heartbeat presence, central idle/max-age policy, `Expired` behavior, session termination, revocation and privacy boundaries. Policy editing requires `Settings.Manage`; destructive session control requires `UserSessions.Terminate`.
- `administration.security-center` — visible with `SecurityEvents.View`; documents deterministic suspicious-login rules, lockouts, 24-hour security metrics, High/Critical notifications, filtering, review workflow and privacy boundaries. Marking events reviewed additionally requires `SecurityEvents.Manage`.
- `administration.users` — documents that deactivating a user revokes still-open sessions.
- `administration.audit-log` — documents the distinction between business Audit evidence and operational Security Events.
- `getting-started.dashboard` — documents distinct Online Users and Active Sessions metrics and navigation to User Sessions.

Help visibility never grants business/security mutations; service authorization remains authoritative.

## Content rules

Help must not imply default credentials, legal/statutory certification, unconfigured jurisdiction-specific behavior, or security telemetry that Depot does not actually collect.

Session Help must distinguish presence, explicit end state and policy expiration. It must not present heartbeats or raw activity signals as Audit records and must distinguish `Users.View`, `Settings.Manage` and `UserSessions.Terminate`.

Security Center Help must describe suspicious-login detection as deterministic triage rules rather than proof of compromise. It must distinguish Security Events from the business Audit Log and must not claim IP, geolocation, device fingerprinting, typed-input capture or external-window monitoring in this version.

Finance Localization Help must continue to distinguish software capability, required configuration, external procedure and reference-only information from legal compliance certification.

## Updating Help

1. Verify current UI, ViewModels, services, permissions and routes.
2. Create/update the Markdown topic.
3. Keep stable IDs and deterministic ordering.
4. Use only valid central permission codes and `topic:` links.
5. Increment the manifest version for material topic/permission/mapping changes.
6. Run Help regression validation for duplicate IDs, missing files, unknown permissions and broken links.

Help manifest **1.21** is the current documentation contract.
