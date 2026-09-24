# Current project status

Updated: 2026-09-20

Depot is on the `0.15.x-preview` development line. Finance includes the General Ledger, AR/AP, Banking, Inventory Accounting, Financial Reporting and the current Fixed Assets/depreciation subledger work; inventory, purchasing, sales including the ERP-native Lead/Opportunity CRM pipeline, localization, notifications, Audit, persistent user sessions, operational security monitoring, enterprise identity/authentication, persisted workspace productivity preferences and the current visual-designer set are integrated in the repository.

## Repository governance

Repository governance defines five stable aggregate GitHub Actions checks for `master`:

- `CI Required Gate`;
- `Quality Required Gate`;
- `Security Required Gate`;
- `Packaged E2E Required Gate`;
- `Database Provider Required Gate`.

The source-controlled ruleset template remains `.github/rulesets/MasterGovernance.json`. The live GitHub ruleset `23590604` is active and still enforces pull-request delivery plus deletion/non-fast-forward protection, but its required-status-check rule is currently absent after the temporary development-phase relaxation. **H1 is therefore reopened / BLOCKED until live enforcement is restored to the template and the live validator passes again.**

## Release pipeline and production acceptance

`.github/workflows/release-integrity.yml` remains the authoritative Source-to-Release path. It performs locked restore, warning-free Release build, regression testing, shared packaged publishing, channel validation, signing policy, manifest/hash/evidence generation and GitHub Release publication from the exact validated artifact.

Pull-request release validation intentionally focuses on release/package/signing/evidence behavior and does not repeat the complete Depot and DepotManager regression suites already covered by the required CI shards. Full Depot and DepotManager regression suites remain mandatory for authoritative `workflow_dispatch` release and Stable acceptance-only runs. The security workflow also keeps Audit/Privacy and Sessions in separate bounded shards to avoid timeout-driven false negatives while preserving the same security coverage.

Preview and Stable channels remain distinct. Preview may remain unsigned. Stable requires the production signing acceptance path to report `PASS` before publication.

Track A repository implementation remains complete, but the current acceptance state is:

- H1 Repository Governance: `BLOCKED` by live ruleset drift until the five aggregate required checks are restored;
- H2 Release Pipeline & Channels: `PASS` at the repository implementation boundary;
- H3 Production Signing: `PRODUCTION_RC_REQUIRED`;
- H4 Production Operations & DR: `DEPLOYMENT_REQUIRED`;
- H5 Accessibility & Desktop Acceptance: `MANUAL_REQUIRED`.

AP-08 verified the Stable acceptance-only path. `scripts/release.ps1 -Channel Stable -AcceptanceOnly` dispatches the authoritative workflow with `publish_release=false`; the production-signing checks and packaged Stable-RC E2E still run, while the GitHub Release publication job is not eligible. The GitHub Actions API currently exposes no `workflow_dispatch` run, so H3 correctly remains `PRODUCTION_RC_REQUIRED` and production signing credentials are not presumed to exist.

Repository CI validates closure contracts but does not manufacture missing production evidence. See [Track A – Final Acceptance Closure](TrackAAcceptanceClosure.md), [Release Pipeline](ReleasePipeline.md), [Production Signing Acceptance](ProductionSigningAcceptance.md), [Production Operations & Disaster Recovery](ProductionOperationsDisasterRecovery.md) and [Accessibility & Desktop Production Acceptance](AccessibilityProductionAcceptance.md).

## Database provider production status

The dedicated provider matrix accepts the current technical baselines when the full provider gate is green:

- bundled SQLite runtime through `Microsoft.Data.Sqlite`;
- SQL Server 2022 / engine 16.x;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

The matrix covers provisioning/migration, transaction semantics, concurrency/deadlock/retry behavior, representative Sales/Procurement/Finance flows, Banking/reconciliation, Financial Reporting/snapshots, service restart, provider-native backup/restore and representative indexed-load acceptance. MariaDB and MySQL are independently accepted; versions outside the listed baselines remain untested/best-effort until separately accepted.

See [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

## Deployment sizing and operations evidence

The repository now includes `scripts/performance/Measure-DeploymentSizing.ps1` and [Deployment Sizing and Operations Readiness](DeploymentSizing.md). The tooling records exact build identity, provider, representative data volume, network latency, concurrent users, p50/p95/max scenario timings, CPU/memory, optimizations, known limits and CI state. It can also aggregate structural Home first-content and eligible My Work provider timings from `performance.log`.

This adds a reproducible acceptance path; it does **not** create customer-specific capacity results by itself. No universal maximum user count, database size or hardware recommendation is claimed. Final sizing still requires execution in the representative target environment.

## Release compliance and known limitations

The repository now has a canonical [Release Compliance Boundary](ReleaseComplianceBoundary.md) and [Known Limitations](KnownLimitations.md). They consolidate release-facing claims across GDPR/DSGVO, CRA, Finance/accounting/localization, electronic invoicing, Banking, database support, retention/DR, vulnerability handling and production support.

This documentation does not close external/manual acceptance. It explicitly records that Preview is not production-supported, the `DE` localization hierarchy is not a certified country pack, the electronic-invoice matrix remains bounded, direct bank connectivity is outside the current baseline, database support is limited to exact accepted versions and customer sizing/DR/legal procedures remain deployment-specific.

## Track C repository closure

Track C F1 through F5B are complete at the repository implementation/acceptance boundary. [Track C acceptance and product status](TrackCStatus.md) is the authoritative feature-level record.

### Electronic invoicing

The bounded XRechnung 3.0 CII path includes Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`), Reverse-charge (`AE`) invoices and Standard-rated Sales Credit Note (`381`). Sales feature schema **15** adds the ERP-native CRM Lead, Opportunity, Stage and Activity persistence while retaining the schema-14 ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid artifact with exact finalized XML, PDF bytes and PDF/XML SHA-256 evidence.

PR #49 repaired the remaining veraPDF trailer-ID and embedded-MIME defects. The independent `Electronic invoice conformance` workflow then completed successfully on the exact PR #49 head for the bounded advertised matrix, closing both the KoSIT XML and veraPDF PDF/A-3B repository conformance boundary for that scope.

This repository acceptance does **not** imply jurisdiction-wide tax/legal certification, arbitrary PDF conversion, unsupported Factur-X profiles or unimplemented special-tax/channel scenarios.

### Enterprise identity and authentication

Approval Policies feature schema **1** is the current bounded configurable-approval boundary for Purchase Orders, Sales Orders, Accounts Payable match exceptions and payment proposals. Resolution is deterministic and fails closed on equal-priority ambiguity; new approval chains retain immutable policy snapshots, while domain services and their existing approval permissions remain authoritative for final business transitions. The Administration designer provides validation and non-mutating preview, and My Work/role-center action eligibility follows the current snapshot stage.

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

AP-06 completed the 1.0 technical reconciliation and found no currently known generic implementation defect or generic automated-evidence defect inside the advertised repository boundary.

AP-07 historically activated ruleset `23590604`, and AP-08 recorded H1 as closed against that then-current live configuration. The live ruleset was later relaxed and currently omits the five aggregate required checks. The current governance section above supersedes that historical snapshot: H1 is reopened / `BLOCKED` until the intended live contract is restored and revalidated.

PR #56 then repaired CI execution boundaries without changing runtime behavior or persisted schemas: repository-governance evidence parsing is valid again, the former combined Audit-Privacy-Sessions security shard is split into bounded Audit-Privacy and Sessions jobs, and PR release validation no longer duplicates the complete regression suites. Authoritative manual release/acceptance runs retain the full regression path.

## Versions

- Application: **0.15.x-preview**
- DepotManager: **0.1.23-preview**
- Core database schema: **30**
- Sales feature schema: **15**
- Finance feature schema: **12**
- User Sessions feature schema: **3**
- Security Events feature schema: **3**
- User Preferences feature schema: **2**
- Document Templates feature schema: **1**
- Enterprise Identity feature schema: **2**
- Approval Policies feature schema: **1**
- Business Attachments feature schema: **1**
- Procurement Sourcing feature schema: **1**
- Project Accounting feature schema: **1**
- Help manifest: **1.31**

`Directory.Build.props` is authoritative for the exact Depot application patch/version; `src/DepotManager/DepotManager.Version.props` is authoritative for DepotManager. Schema migration constants and `src/Depot/Help/manifest.json` are authoritative for the remaining baseline values. Every repository commit increments `DepotVersionPatch`.

## Validation boundary

Repository build/test/security/provider/conformance evidence proves the implemented technical boundary only. It does not replace deployment-specific accounting/tax/legal review, accessibility acceptance, production signing, customer-specific sizing, backup-retention ownership, operational DR evidence or organizational compliance work.

## Next steps

H2 remains closed, but H1 must be restored first. Reapply the five aggregate required checks to live ruleset `23590604` and pass the fail-closed governance validation. Only after H1 returns to `PASS` is the next Track A acceptance action H3: execute `scripts/release.ps1 -Channel Stable -AcceptanceOnly` from clean current `master` with the real production signing secrets and publisher-subject variable configured in GitHub.

A successful H3 run must retain `ProductionSigningAcceptance.json`, `ReleaseEvidence.json`, manifest/hashes and Stable RC test evidence. Until that run exists and passes, H3 remains `PRODUCTION_RC_REQUIRED`.

After H3, the remaining Track A closure work is H4 deployment disaster-recovery acceptance and H5 exact-RC manual desktop accessibility acceptance. Product/legal/deployment items remain separately tracked in [Release 1.0](Release1.0.md) and the [Roadmap](Roadmap.md).

## Business attachments

Business Attachments feature schema **1** provides one reusable attachment workflow for Customer, Supplier, Item, Sales Quote, Sales Order, Sales Invoice, Purchase Order, Goods Receipt and Supplier/AP Document records. V1 content is database-backed behind a replaceable content-store abstraction, revisioned and SHA-256 verified. Service-layer domain permissions and Audit remain authoritative.

The provider acceptance matrix exercises schema provisioning and binary content round-trip on every supported provider. The product boundary remains 25 MiB per file, blocks known executable/script extensions and requires deployment-level malware scanning where applicable.

See [Business Attachments](BusinessAttachments.md).

## Finance budgeting

Finance feature schema **11** adds controlled versioned budgeting, account/period/dimension lines, deterministic annual spreading, CSV preview/atomic import/export, configurable policy approval, immutable Approved/Locked history, Finance > Budgeting UI, My Work projections and Actual-vs-Budget variance backed by the existing Financial Reporting/GL authority.

Forecasting, workforce planning, treasury cash forecasting, consolidation budgeting and spreadsheet formulas remain outside this scope.


## Finance SEPA SCT export

Finance Banking includes deterministic `pain.001.001.09` SEPA SCT export from approved payment runs, explicit structured debtor/creditor payment profiles, immutable retained XML/SHA-256 evidence, exact re-download, explicit supersede semantics and manual external-status evidence. External bank submission, bank certification and automated status polling remain outside the repository boundary.


## Procurement sourcing

The repository now contains the controlled Purchase Requisition -> approval -> RFQ -> Supplier Quote Response -> explicit comparison/selection -> Purchase Order draft workflow. Procurement Sourcing feature schema **1** provides provider-neutral persistence, Approval Policy integration, optimistic concurrency, explicit requisition and sourcing permissions, Audit evidence, Business Attachments, My Work/Buyer Workbench projections and a dedicated Purchasing > Sourcing workspace.

Supplier comparison is informational and deterministic; Depot does not automatically award a supplier. Purchase Order conversion is explicit and idempotent and continues through the existing Purchase Order authority. Resulting Purchase Orders retain source links to the requisition, RFQ and selected quote response.

## Inventory replenishment

Inventory Replenishment feature schema **1** adds controlled per-item/per-warehouse policies, deterministic stock/demand/supply snapshots, MOQ-aware explainable purchase suggestions, explicit Blocked/Superseded lifecycle states, Buyer/My Work visibility and idempotent user-approved conversion into the existing Purchase Requisition sourcing workflow.

The feature does not forecast demand, allocate suppliers automatically, create Purchase Orders automatically or implement full MRP.



## Project and cost accounting

Project Accounting feature schema **1** adds provider-neutral Project and Project Phase lifecycle persistence, controlled attribution of Purchase Orders, supplier documents, Sales Orders, Sales Invoices and manual journals, read-only GL actuals, open purchasing commitments, Finance Budget line links, Actual-vs-Budget variance, Business Attachments, Audit, optimistic concurrency, a Projects workspace and owned-project My Work projections.

General Ledger, Finance subledgers, Financial Reporting and Finance Budgeting remain authoritative. Project Accounting does not maintain a second ledger or independent accounting-calculation engine. Real-provider smoke acceptance verifies the `ProjectAccounting` feature migration on SQLite, SQL Server, MariaDB and MySQL.
