# Depot Help Center

Updated: 2026-09-18

Depot ships an embedded offline Markdown Help Center rendered natively in WPF. Help is permission-filtered, locally searchable, uses stable topic IDs, and opens in the normal workspace shell.

## Current manifest

Help manifest **1.26** contains Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, Finance, Administration and Troubleshooting topics plus the current User Sessions and Security Center guidance.

The session/security help contract includes:

- `administration.user-sessions` — visible with `Users.View`; documents Active/History views, Online Users/Active Sessions metrics, heartbeat presence, central idle/max-age policy, `Expired` behavior, session termination, revocation and privacy boundaries. Policy editing requires `Settings.Manage`; destructive session control requires `UserSessions.Terminate`.
- `administration.security-center` — visible with `SecurityEvents.View`; documents deterministic suspicious-login rules, lockouts, 24-hour security metrics, High/Critical notifications, filtering, review workflow and privacy boundaries. Marking events reviewed additionally requires `SecurityEvents.Manage`.
- `administration.document-designer` — visible with `DocumentTemplates.View`; documents the bounded Depot document-template designer, draft/preview/activation lifecycle, unsaved-change handling and the separation between visual layout and accounting/e-invoice semantics.
- `administration.approval-policies` — visible with `ApprovalPolicies.View`; documents bounded deterministic approval routing, policy validation/preview, immutable in-flight snapshots, explicit delegation and the separation between policy administration and domain approval authority.
- `administration.users` — documents that deactivating a user revokes still-open sessions.
- `administration.audit-log` — documents the distinction between business Audit evidence and operational Security Events.
- `getting-started.dashboard` — documents distinct Online Users and Active Sessions metrics and navigation to User Sessions.

Help visibility never grants business/security mutations; service authorization remains authoritative.

## Current content alignment

The embedded articles are aligned with the current product boundary:

- Item and Sales Pricing Help documents the four explicit Base Cost strategies (Preferred Supplier, Last Purchase, Manual Standard and Inventory Cost Reference), controlled FX evidence, pricing methods and commercial rounding.
- Sales Invoice Help documents the bounded Standard (`S`), Zero (`Z`), Exempt (`E`) and Reverse-charge (`AE`) invoice matrix, Standard-rated Sales Credit Note `381`, and retained ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile PDF/A-3B evidence.
- The bounded electronic-invoice matrix is repository-validated with pinned KoSIT/XRechnung and veraPDF tooling; Help does not present that as jurisdiction-wide legal/tax certification.

These are article-content corrections only. Topic IDs, `requiredPermission` mappings, related-topic routing and manifest structure are unchanged, so Help manifest **1.26** remains the correct contract.

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

Help manifest **1.26** is the current documentation contract.


Approval Policy administration requires `ApprovalPolicies.Manage`; actual Purchase Order, Sales Order, Accounts Payable exception and payment-proposal decisions still require their existing domain approval permissions in addition to current-stage eligibility.
