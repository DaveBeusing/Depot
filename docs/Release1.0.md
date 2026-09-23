# Depot Version 1.0 Release Checklist

Updated: 2026-09-20

## Status

- [ ] Ready for release

Depot is on the `0.15.x-preview` line with Core database schema **30**, Sales feature schema **14**, Finance feature schema **9**, User Sessions schema **3**, Security Events schema **3**, User Preferences schema **2**, Document Templates schema **1**, Enterprise Identity feature schema **2**, and Help manifest **1.24**. `Directory.Build.props` is authoritative for the exact application patch/version. Checked items represent implemented technical controls/evidence only; they do not replace legal, accounting, accessibility, signing, localization or deployment acceptance.

## Implemented Finance baseline

- [x] legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations and number sequences
- [x] immutable balanced journals, reporting-currency/FX snapshots, posting profiles, validation, idempotency, number allocation, Audit evidence and linked reversals
- [x] Sales → Accounts Receivable → GL, open items, payments/allocations, write-offs/reversals, aging/statements and dunning
- [x] supplier documents/open items, Accounts Payable → GL, three-way match, explicit exceptions, payments/reversals, aging/statements and segregation of duties
- [x] FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation
- [x] bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position
- [x] Trial Balance, GL, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS
- [x] optional GL dimension filtering and explicit account mappings
- [x] deterministic CSV export and immutable SHA-256-bound report snapshots
- [x] provider-neutral localization packs, Legal Entity assignments and effective registry
- [x] immutable built-in `GENERIC → EU → DE` reference hierarchy
- [x] explicit effective-dated localization activation with country validation and overlap prevention
- [x] software-capability/configuration/external-procedure/reference-only separation
- [x] optimistic concurrency, Audit evidence and Finance RBAC
- [x] Finance workspaces and contextual Help for Receivables, Payables, Inventory Accounting, Banking, Financial Reporting and Localization

## Other implemented technical baseline

- [x] first-run administrator creation, hardened authentication and multi-role RBAC
- [x] service-layer authorization and creator/approver controls where implemented
- [x] immutable/correction-oriented retained business records and Audit evidence
- [x] database-backed visual document-template versions and active-layout rollout across all supported Sales/warehouse PDFs
- [x] User Preferences schema 2 workspace productivity: Saved Views, supported filter/column/sort state, grid density, Favorites, Recents and default landing
- [x] permission-filtered Quick Open, Command Palette and Global Search
- [x] bounded productive visual designers for posting flows, pricing strategy, financial-report mappings, bank reconciliation, localization hierarchy, role permissions and import mapping
- [x] local backup/recovery controls and privacy export
- [x] dependency locks, NuGet audit, SBOM/evidence and release-integrity workflows
- [x] repository vulnerability-reporting, vulnerability-management and production-support policy baselines
- [x] centralized per-item Customer → Region → Global Sales pricing with optional customer assignment and retained document source snapshots
- [x] Sales Invoice seller/buyer/XRechnung finalization and persisted XML integrity evidence
- [x] electronic Sales Credit Note finalization for the bounded advertised XRechnung scope
- [x] ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid artifact generation/persistence with embedded finalized XML and PDF/XML SHA-256 evidence
- [x] enterprise identity with OIDC Authorization Code + PKCE, tenant-bound Entra support and provider-bound assurance validation
- [x] immutable Security Event export projection with provider-neutral filtering/snapshot boundaries
- [x] persisted Security Event export targets with durable at-least-once checkpoint/retry/suspension state
- [x] bounded regression/quality/accessibility CI controls
- [x] source-controlled `master` governance contract and active ruleset PR/deletion/non-fast-forward protection; live aggregate required-check binding remains an H1 acceptance item
- [x] one authoritative Source-to-Release workflow
- [x] explicit Preview/Stable GitHub Release channels with tag/version consistency
- [x] release manifest records versions, Core/feature schema versions, source SHA, channel and final artifact hashes
- [x] release publication reuses the exact tested workflow artifact rather than rebuilding
- [x] Stable publication fails closed when Authenticode signing credentials are unavailable
- [x] deployment DR profile contract with explicit RPO/RTO, retention, off-host, encryption, monitoring, ownership and drill cadence
- [x] provider-specific SQLite/SQL Server/MariaDB/MySQL restore runbooks
- [x] pull-request recovery drills and retained credential-free provider recovery evidence
- [x] DepotManager recovery-readiness support evidence and sanitized ACL/I/O recovery diagnostics
- [x] packaged DepotManager lifecycle acceptance for install, update, repair, rollback safety, manager self-update, uninstall scopes and real Windows registration/shortcut integration
- [x] runtime keyboard-focus fallback plus hardened focus-suppression quality checks
- [x] custom input UI Automation label/required-state forwarding to native focus targets
- [x] UI Automation notifications for operation/error/connection status changes
- [x] explicit Per-Monitor-V2 DPI manifests for Depot and DepotManager
- [x] reproducible exact-RC accessibility acceptance/evidence contract
- [x] centralized fail-closed Track A H1–H5 production-closure evidence contract
- [x] Track C F1–F5B repository implementation and acceptance closure
- [x] Depot 1.0 technical gap reconciliation with no currently known generic implementation or automated-evidence defect inside the advertised repository boundary

## Database provider production acceptance

The generic technical provider gate is complete for the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md):

- [x] supported database technical-acceptance baselines defined
- [x] bundled SQLite full provider acceptance
- [x] SQL Server 2022 / engine 16.x live migration, concurrency, locking, recovery and performance matrix
- [x] MariaDB 11.8.9 LTS live migration, concurrency, locking, recovery and performance matrix
- [x] MySQL 8.4.11 LTS live migration, concurrency, locking, recovery and performance matrix
- [x] MariaDB and MySQL independently exercised rather than inferred from one another
- [x] provider-specific transaction rollback, concurrent mutation and deadlock/write-conflict retry acceptance
- [x] Sales and Procurement live provider business acceptance
- [x] Finance GL/AR/AP/FIFO live provider business acceptance
- [x] Finance Banking/reconciliation live provider business acceptance
- [x] Finance Financial Reporting/export/snapshot live provider business acceptance
- [x] remote service restart and Depot re-entry acceptance
- [x] provider-native remote backup/restore and post-restore recognition boundary
- [x] isolated SQLite backup-copy/hash/integrity/schema restore drill
- [x] structured recovery evidence retained for every supported provider family
- [x] representative 100,000-row indexed provider performance guard

Versions outside these baselines are not implicitly supported. The provider gate is a technical database/runtime acceptance boundary only.

## Track C repository acceptance

The Track C repository boundary is closed. [Track C acceptance and product status](TrackCStatus.md) records the detailed scope.

- [x] F1 integrated
- [x] bounded XRechnung 3.0 CII implementation and retained conformance fixtures
- [x] independent KoSIT acceptance for every currently advertised XRechnung production scenario
- [x] ZUGFeRD/Factur-X XRECHNUNG-profile hybrid generation and immutable PDF/XML evidence
- [x] independent pinned veraPDF PDF/A-3B acceptance for the bounded advertised hybrid matrix
- [x] enterprise identity foundation, OIDC/Entra sign-in and provider-bound assurance validation
- [x] immutable Security Event export foundation
- [x] durable at-least-once Security Event delivery/checkpoint/retry/suspension state
- [x] final CI, Software Quality, Security Supply Chain, Database Provider and DepotManager packaged-E2E closure after F5B

This repository acceptance does not certify unimplemented tax/channel scenarios or replace deployment/legal review.

## Required production acceptance before 1.0

### Finance, deployment and operations

- [ ] deployment Legal Entity/chart/book/calendar/posting-profile/inventory/bank/reporting/localization-policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end reporting procedures
- [ ] AP/payment-proposal segregation-of-duties review
- [ ] accounting/report/localization evidence retention/export/backup/restore operating procedures
- [ ] ACTIVE deployment DR profile accepted with explicit RPO/RTO and named ownership
- [ ] real deployment backup infrastructure proves retention, encryption, monitoring and off-host copies
- [ ] real isolated restore drill completes within accepted RPO/RTO and evidence is retained
- [ ] qualified review of each enabled country pack and every `ConfigurationRequired` / `ExternalProcedureRequired` item
- [ ] jurisdiction-specific accounting/tax/localization acceptance
- [ ] customer-specific volume/network/concurrency sizing beyond the generic provider regression guard

### UI/accessibility

Technical controls and the evidence contract are implemented. The exact release candidate still requires human desktop acceptance:

- [ ] keyboard-only critical-workflow walkthrough including Login, first-run admin, shell/workspaces, Finance and DepotManager
- [ ] logical focus order, visible focus, no-keyboard-trap and focus-restoration review
- [ ] Narrator baseline for inputs, grids, dialogs, validation/errors and dynamic status updates
- [ ] Accessibility Insights baseline with no unresolved blocking findings
- [ ] 100/125/150/200% DPI acceptance with retained evidence for each scale
- [ ] `Test-AccessibilityAcceptance.ps1 -RequirePass` succeeds for the exact release-candidate source SHA

### Security/release engineering

- [x] single authoritative release pipeline and explicit Preview/Stable channel policy
- [x] source SHA, versions, schema versions and final hashes retained in release evidence
- [x] production DR runbook/profile/recovery-evidence technical boundary implemented
- [x] desktop accessibility technical/evidence boundary implemented
- [x] Track A closure validator rejects incomplete H1–H5 evidence
- [x] generic packaged lifecycle acceptance covers install/update/repair, rollback safety, manager self-update, uninstall data scopes and Windows shell integration
- [ ] H1 live GitHub `master` ruleset `23590604` again requires all five aggregate gates and fresh fail-closed governance evidence passes
- [ ] production backup/security ownership and vulnerability-reporting process accepted for the deployment
- [ ] release-specific support statement published with support dates, supported Windows versions, exact database baselines, upgrade path and security-update channel
- [ ] production Authenticode publisher identity and timestamp verified with a real Stable release candidate
- [ ] certificate expiry/rotation and signing-recovery procedure accepted
- [ ] exact production-signed Stable RC lifecycle acceptance completed with the final release artifacts
- [ ] exact-RC Track A closure evidence passes `Test-TrackAAcceptance.ps1 -RequirePass`
- [ ] final release notes, exact-RC Known Limitations, hashes, SBOM and support information published

### Electronic invoicing

The repository conformance boundary for the currently advertised XRechnung and ZUGFeRD/Factur-X XRECHNUNG-profile matrix is complete. Remaining items below are deliberately outside that bounded implemented scope or require deployment/business acceptance.

- [ ] remaining EN 16931 special-tax semantics beyond the advertised bounded matrix accepted before they are marketed
- [x] electronic credit-note finalization for the advertised Standard-rated Credit Note (`381`) scenario
- [ ] recipient/channel routing acceptance for any production channel that is marketed
- [x] every currently advertised production XML scenario validated against the applicable KoSIT/XRechnung release
- [x] PDF/A-3 hybrid artifact generation embeds finalized `xrechnung.xml` and retains immutable PDF/XML hash evidence
- [x] representative advertised ZUGFeRD/Factur-X artifacts independently validated with pinned veraPDF acceptance

### Legal/organizational

- [x] canonical release compliance claim boundary prepared and linked to authoritative technical evidence
- [x] canonical Known Limitations document prepared for exact-RC review/publication
- [ ] GDPR/DSGVO deployment assessment and retention procedures
- [ ] organization-specific GoBD/accounting/reporting/localization procedures
- [ ] final CRA applicability/classification/conformity work
- [ ] qualified accounting/tax/legal review for each marketed Finance localization

## Track A implementation and closure status

All five Track A repository implementation packages have been addressed: repository governance, release pipeline/channels, production-signing acceptance path, production operations/disaster recovery, and accessibility/desktop production acceptance.

The unified closure procedure is documented in [Track A – Final Acceptance Closure](TrackAAcceptanceClosure.md). The controlled template remains top-level `BLOCKED`: H1 is currently `BLOCKED` by live ruleset drift, H2 is repository-level `PASS`, H3 is `PRODUCTION_RC_REQUIRED`, H4 is `DEPLOYMENT_REQUIRED`, and H5 is `MANUAL_REQUIRED`.

Track A becomes production-accepted only after a controlled evidence file for the exact Stable RC passes `Test-TrackAAcceptance.ps1 -RequirePass`. Repository CI validates the contract structure but never promotes missing external/manual evidence to `PASS`.

## Technical reconciliation result

The read-only-first Depot 1.0 gap reconciliation is complete. [Depot 1.0 Release Candidate Readiness](ReleaseCandidateReadiness.md) records the classification and decision boundary.

The 2026-09-20 documentation reconciliation keeps the post-stabilization repository baseline aligned with current `master`: H1 governance is reopened because the live ruleset no longer requires the five aggregate checks, while persisted workspace productivity, the current designer set and Finance Help are implemented rather than deferred. Restore and validate H1 before H3. H3/H4/H5 and deployment/legal acceptance remain separate blockers.

Do not open another generic feature/hardening package solely because the product has not yet reached 1.0. New repository repair work should be evidence-driven by a reproducible failing gate or RC acceptance finding.

## Demand-driven extensions

Additional country/statutory packs are supported by the localization framework when metadata/configuration is sufficient. They must not be marketed as compliant/certified without separate qualified acceptance. Direct bank connectivity/payment initiation, jurisdiction-specific statutory filing implementations, additional costing methods and other executable behavior require separately scoped implementation.
