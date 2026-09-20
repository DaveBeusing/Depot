# Documentation status

Updated: 2026-09-20

This document identifies the documentation baseline for the current development state. Implemented technical controls must remain distinct from production/legal acceptance gates.

## Current baseline

- Application: `0.15.x-preview`
- Help manifest: `1.22`
- Core database schema: `30`
- Sales feature schema: `14`
- Finance feature schema: `9`
- User Sessions feature schema: `3`
- Security Events feature schema: `3`
- User Preferences feature schema: `2`
- Document Templates feature schema: `1`
- Enterprise Identity feature schema: `2`

`Directory.Build.props` is the authoritative source for the exact application patch/version. Canonical documentation records the moving preview line as `0.15.x-preview`; exact patch numbers belong in source/release evidence, not manually duplicated baseline text.

## Database provider baseline

The authoritative production database support statement is [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

Current certified technical baselines are:

- Depot-bundled SQLite runtime;
- SQL Server 2022 / engine 16.x, with CI certification on SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

Documentation must not infer provider support from shared abstractions or from a different product in the same connector family. MariaDB and MySQL are independently tested. Newer/older versions remain untested/best-effort until their own full acceptance evidence exists.

The full provider matrix validates provisioning/migration, provider SQL/types/constraints, rollback, concurrency/deadlock/retry, representative Sales/Procurement/session/Finance flows, Banking/reconciliation, Financial Reporting/snapshots, remote restart, provider-native backup/restore and 100k indexed access. Enterprise Identity schema 2 includes provider-level assurance-policy migration/persistence in the external-identity provider boundary.

SQLite documentation must preserve its dynamic `NUMERIC` precision boundary; it must not claim full fixed `DECIMAL(28,9)` range equivalence with the server providers.

## Electronic-invoice documentation invariant

Sales schema 14 retains ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid artifacts as exact PDF/A-3B bytes linked to the finalized XRechnung XML SHA-256. Documentation must not describe the hybrid PDF as a later reconstruction from customer or company master data. Independent pinned veraPDF acceptance is green for the bounded advertised matrix. Documentation must not broaden that repository evidence into jurisdiction-wide tax/legal certification or support for profiles, tax scenarios or conversion paths outside the accepted matrix.

## 2026-09-18 documentation reconciliation

The current repository/help audit reconciles the post-Track-C and post-H1 baseline across README, canonical status/security/pricing documents and embedded Help. It also records the current PR-versus-authoritative release-validation boundary introduced by the latest CI hardening. The Document Designer rollout adds a dedicated `administration.document-designer` topic bound to `DocumentTemplates.View`, so Help manifest version `1.22` records the new permission-bound route.

## 2026-09-20 baseline reconciliation

The current baseline is reconciled against the live repository rather than earlier acceptance snapshots. User Preferences schema 2 provides persisted Saved Views, supported filter/column/sort state, grid density, Favorites, Recents and default landing behavior; command/search productivity and current role-center navigation are implemented and must not be described as deferred. The current visual-designer set is present on `master`, while shared designer infrastructure remains intentionally bounded to interactions with proven duplicate semantics.

Live repository governance is also evidence-driven. Ruleset `23590604` is active and still enforces pull-request delivery plus deletion/non-fast-forward protection, but it currently lacks the five aggregate required-status-check bindings. H1 is therefore `BLOCKED` until those live checks are restored and the governance validator passes. Historical text that recorded H1 as closed does not override the live state.

Embedded Finance Help must describe Accounts Payable, Inventory Accounting, Banking, Financial Reporting and Localization as implemented capabilities. Help manifest `1.22` remains unchanged because this reconciliation changes Help content only; topic IDs, routing and permission metadata are unchanged.

## Deployment sizing documentation invariant

[Deployment Sizing and Operations Readiness](DeploymentSizing.md) is the canonical deployment-capacity evidence procedure. Documentation may describe the repository's calibration profiles and aggregation tooling, but must not convert those profiles or one measured environment into universal user-count, hardware, database-size or latency guarantees. Customer-specific sizing remains an environment-specific acceptance activity and must retain provider/security/transaction semantics.

## Release compliance and limitations invariant

[Release Compliance Boundary](ReleaseComplianceBoundary.md) is the canonical supported/unsupported claim matrix. [Known Limitations](KnownLimitations.md) is the release-facing limitation list that must be revalidated for the exact Stable release candidate.

Documentation must not broaden:

- the bounded XRechnung/ZUGFeRD/Factur-X issuance matrix;
- the exact database-provider baselines;
- Finance Localization support levels into legal compliance states;
- repository backup/recovery evidence into deployment RPO/RTO or legal-retention evidence;
- repository CRA/privacy/security controls into a completed external legal/conformity assessment;
- Preview engineering evidence into a commercial production-support commitment.

Help manifest `1.22` is unchanged because this reconciliation changes release/compliance documentation only; no Help topic route, permission or in-app workflow contract changes.

## Session and authentication invariants

Documentation must state that online presence is derived from an open session plus heartbeat freshness; no persisted `IsOnline` is authoritative. The runtime heartbeat is 30 seconds and presence timeout is 90 seconds. Activity stores only the latest in-Depot keyboard/mouse/touch timestamp, never typed input or coordinates.

The shared User Session policy covers idle timeout, maximum lifetime, concurrent-session mode/limit/action and ended-session history retention. Finite limits are serialized through the policy row and may reject a login or supersede the oldest open session. Password changes invalidate other sessions with `CredentialsChanged`; user deactivation revokes open sessions with `Revoked`.

Production authentication throttling is persisted in the shared database and governed by `AuthenticationSecurityPolicy`; documentation must not call it process-local. Local credentials remain behind `IAuthenticationProvider` / `LocalAuthenticationProvider`.

Enterprise Identity schema 2 provides provider configuration, exact provider/issuer/subject links and optional provider-bound external assurance requirements. OIDC/Entra sign-in uses Authorization Code + PKCE, validates the ID token and any configured `amr`, `acr`, `auth_time` and `azp` assurance boundary before local identity resolution. Authentication-assurance claim values are runtime evidence and are not persisted on identity links.

Local Depot RBAC remains the only source of roles and effective permissions. External token roles/groups/permissions are not authorization inputs. Documentation must not present a matching `amr` string as universal MFA semantics; assurance values are explicit provider contracts, and Entra Conditional Access / Authentication Strength remains a deployment-side control.

Security Center investigation correlates only identifiers already present in Depot authentication/session data. Response actions delegate to the established session/user services. `SecurityEvents.View`, `SecurityEvents.Manage`, `UserSessions.Terminate`, `Users.Manage` and `Settings.Manage` remain separate permissions.

Session history and Security Event retention are actively enforced by bounded background maintenance. Security Event retention never deletes business Audit evidence. High/Critical notification behavior is behind `SecurityAlertPolicy`; the current default threshold is High.

Security Event export keeps source evidence immutable. Schema 3 stores export targets and durable delivery state separately, including filter identity, checkpoints, fixed snapshot bounds, retry/suspension state and worker leases. Documentation must describe delivery as at-least-once and must not imply that source `SecurityEvents` rows are mutated to track export progress.

## Privacy invariants

The current security implementation does not collect source IP, geolocation, MAC address, hardware fingerprint, typed input, key values, mouse coordinates or external-window activity. `ClientInstanceId` is a generated Depot process/session correlation identifier, not a device fingerprint.

Enterprise Identity may retain issuer/subject plus optional observed tenant, email and display name because these are required identity-link evidence. It may persist administrator-selected assurance requirements, but it does not retain raw `amr`, `acr`, `auth_time`, `azp`, external passwords, access tokens, refresh tokens, ID tokens, authorization codes or device fingerprints.

## Documentation rules

Do not describe password-change invalidation, concurrent-session policy, shared database throttling, provider certification, investigation/response, retention, OIDC sign-in, provider-bound assurance validation, Security Event durable delivery, Saved Views, workspace Favorites/Recents/default landing, supported grid-density persistence, command/search productivity, or the implemented Finance packages as future-only work.

Do not describe database-provider technical support as jurisdiction-specific accounting, tax, legal, accessibility, bank-network or regulatory certification. Remote backup scheduling, retention, off-host copies and restore procedures remain operator responsibilities even though the CI matrix validates a provider-native restore boundary.

Do not pin an exact preview patch version in canonical baseline documents. Use the development line and link exact build/release identity to `Directory.Build.props` and release evidence.

Help manifest **1.22** contains the existing User Sessions and Security Center topics plus the permission-bound `administration.document-designer` topic. The current manifest version is authoritative for embedded Help routing and content.
