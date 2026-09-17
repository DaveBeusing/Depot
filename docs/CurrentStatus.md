# Current project status

Updated: 2026-09-17

Depot is on the `0.15.x-preview` development line. Finance, inventory, purchasing, sales, reporting, localization, notifications, Audit, persistent user sessions, operational security monitoring, enterprise identity/authentication and the completed Track C feature set are integrated in the repository.

## Repository governance

Repository governance exposes five stable aggregate GitHub Actions checks intended for `master` protection:

- `CI Required Gate`;
- `Quality Required Gate`;
- `Security Required Gate`;
- `Packaged E2E Required Gate`;
- `Database Provider Required Gate`.

The source-controlled ruleset template is `.github/rulesets/MasterGovernance.json`. `scripts/operations/Test-RepositoryGovernance.ps1` now validates the complete template contract and verifies that all five aggregate workflow job names remain present. CI retains this repository-side governance evidence on every change.

The same validator provides a fail-closed `-RequireActiveRuleset` mode that queries the live GitHub rulesets API and records the matching active ruleset ID. This is the authoritative H1 activation evidence path.

**Administrative H1 closure is still required.** `master` is not currently protected and the repository rulesets API currently exposes no active ruleset enforcing the source-controlled template. H1 remains `ADMIN_REQUIRED` until the live GitHub ruleset is activated and `Test-RepositoryGovernance.ps1 -RequireActiveRuleset` succeeds against the real repository settings.

See [Repository Governance](RepositoryGovernance.md) and [Track A – Final Acceptance Closure](TrackAAcceptanceClosure.md).

## Release pipeline and production acceptance

`.github/workflows/release-integrity.yml` remains the authoritative Source-to-Release path. It performs locked restore, warning-free Release build, regression testing, shared packaged publishing, channel validation, signing policy, manifest/hash/evidence generation and GitHub Release publication from the exact validated artifact.

Preview and Stable channels remain distinct. Preview may remain unsigned. Stable requires the production signing acceptance path to report `PASS` before publication.

Track A repository implementation is complete, but production closure intentionally remains evidence-gated:

- H1 Repository Governance: `ADMIN_REQUIRED`;
- H2 Release Pipeline & Channels: `PASS` at the repository implementation boundary;
- H3 Production Signing: `PRODUCTION_RC_REQUIRED`;
- H4 Production Operations & DR: `DEPLOYMENT_REQUIRED`;
- H5 Accessibility & Desktop Acceptance: `MANUAL_REQUIRED`.

Repository CI validates the closure contracts but does not manufacture missing production evidence. See [Track A – Final Acceptance Closure](TrackAAcceptanceClosure.md), [Release Pipeline](ReleasePipeline.md), [Production Signing Acceptance](ProductionSigningAcceptance.md), [Production Operations & Disaster Recovery](ProductionOperationsDisasterRecovery.md) and [Accessibility & Desktop Production Acceptance](AccessibilityProductionAcceptance.md).

## Database provider production status

The dedicated provider matrix certifies the current technical baselines when the full provider gate is green:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The matrix covers provisioning/migration, transaction semantics, concurrency/deadlock/retry behavior, representative Sales/Procurement/Finance flows, Banking/reconciliation, Financial Reporting/snapshots, service restart, provider-native backup/restore and representative indexed-load acceptance. MariaDB and MySQL are independently certified; versions outside the listed baselines remain untested/best-effort until separately accepted.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

## Track C repository closure

Track C F1 through F5B are complete at the repository implementation/acceptance boundary. [Track C acceptance and product status](TrackCStatus.md) is the authoritative feature-level record.

### Electronic invoicing

The bounded XRechnung 3.0 CII path includes Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`), Reverse-charge (`AE`) invoices and Standard-rated Sales Credit Note (`381`). Sales feature schema **14** also persists the ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid artifact with exact finalized XML, PDF bytes and PDF/XML SHA-256 evidence.

PR #49 repaired the remaining veraPDF trailer-ID and embedded-MIME defects. The independent `Electronic invoice conformance` workflow then completed successfully on the exact PR #49 head for the bounded advertised matrix, closing both the KoSIT XML and veraPDF PDF/A-3B repository conformance boundary for that scope.

This repository acceptance does **not** imply jurisdiction-wide tax/legal certification, arbitrary PDF conversion, unsupported Factur-X profiles or unimplemented special-tax/channel scenarios.

### Enterprise identity and authentication

Enterprise Identity feature schema **2** is the current provider-neutral external-identity and authentication-assurance boundary. The implemented path includes Authorization Code + PKCE, system-browser sign-in, loopback callback handling, OIDC discovery/signing-key validation, issuer/audience/lifetime/nonce validation, tenant-bound Microsoft Entra ID, optional provider-bound `amr` / `acr` / `auth_time` requirements and `azp` validation.

Local Depot RBAC remains authoritative. External roles/groups/permission claims are not authorization inputs, and raw protocol tokens or runtime assurance claims are not persisted on identity links. PR #47 is merged and closes the earlier deterministic F4C acceptance regressions.

See [Enterprise Identity and OpenID Connect](EnterpriseIdentity.md).

### Security Event export and delivery

F5A established immutable Security Event export projection, normalized filters, filter-bound checkpoints, fixed snapshot upper bounds and bounded deterministic batches without mutating source event meaning.

F5B advances Security Events feature schema to **3** and persists export targets separately from source events together with durable checkpoints, in-flight snapshot state, retry/suspension evidence and short worker leases. Delivery is explicitly **at-least-once**: the checkpoint advances only after sink success, and a crash after remote acceptance but before local commit may duplicate a batch. `X-Depot-Delivery-Id` is the receiver deduplication boundary.

The `http-json-v1` adapter requires HTTPS and does not persist endpoint credentials, tokens or response bodies. Normal retention protects events not yet consumed by enabled targets.

See [Security Event Export](SecurityEventExport.md).

## Final repository acceptance repairs

AP-01 identified two evidence blockers after F5B: a transient Windows executable/image lock in DepotManager packaged replacement and stale canonical Security Events schema documentation. PR #51 merged both repairs with green defined merge gates.

AP-04 then closed the remaining DepotManager shipped-artifact evidence gap: PR #53 adds real GitHub-hosted Windows integration acceptance for the current-user uninstall registration, Start menu and desktop shortcuts, `InstallationInspector`, repair behavior and idempotent cleanup while refusing mutation on non-clean/non-hosted profiles.

The replacement path continues to retry only bounded `IOException` / `UnauthorizedAccessException` failures caused by short-lived Windows locks. Persistent locks remain fail-closed; the original executable is preserved and the staged `.new` artifact is cleaned.

## Versions

- Application: **0.15.x-preview**
- DepotManager: **0.1.23-preview**
- Core database schema: **30**
- Sales feature schema: **14**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **3**
- User Preferences feature schema: **2**
- Enterprise Identity feature schema: **2**
- Help manifest: **1.21**

`Directory.Build.props` is authoritative for the exact Depot application patch/version; `src/DepotManager/DepotManager.Version.props` is authoritative for DepotManager. Schema migration constants and `src/Depot/Help/manifest.json` are authoritative for the remaining baseline values. Every repository commit increments `DepotVersionPatch`.

## Validation boundary

Repository build/test/security/provider/conformance evidence proves the implemented technical boundary only. It does not replace deployment-specific accounting/tax/legal review, accessibility acceptance, production signing, customer-specific sizing, backup-retention ownership, operational DR evidence or organizational compliance work.

## Next steps

The repository-side H1 governance contract is now machine-verifiable. The remaining H1 action is administrative activation of the source-controlled ruleset in GitHub followed by retained live-ruleset evidence.

After H1 activation, the remaining Track A production closure work is external/evidence-driven: H3 production-signed Stable RC acceptance, H4 deployment disaster-recovery acceptance and H5 exact-RC manual desktop accessibility acceptance. Product/legal/deployment items remain separately tracked in [Release 1.0](Release1.0.md) and the [Roadmap](Roadmap.md).
