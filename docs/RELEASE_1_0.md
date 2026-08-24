# Depot Version 1.0 Release Checklist

## Status

- [ ] Ready for release

Depot is currently on the `0.14.x-preview` line with core database schema **29** and Sales feature schema **8** for invoice finalization. Checked items mean the technical implementation/evidence exists; they do not replace environment-specific, legal, accessibility, provider, signing, tax-profile, routing, or operational acceptance.

## Implemented technical baseline

### Authentication and authorization

- [x] first-run administrator creation; no shared production default credentials
- [x] password policy and login throttling
- [x] versioned PBKDF2-HMAC-SHA256 password hashing
- [x] database-backed multi-role RBAC
- [x] service-layer authorization
- [x] creator/approver separation and audited administrator overrides

### Data protection and audit

- [x] DPAPI-protected persisted database secrets
- [x] encrypted transport enforced by supported remote-provider settings
- [x] Audit Log viewer, filters, paging, sanitized details, CSV export
- [x] structured business-record evidence export
- [x] Privacy Data discovery and machine-readable JSON export
- [x] secret exclusion/redaction requirements

### Business-record integrity

- [x] immutable/final-state workflow rules for reviewed retained business records
- [x] correction/reversal/return/credit workflows instead of destructive edits
- [x] actor/UTC timestamp/state-transition evidence
- [x] atomic business + audit persistence for reviewed retained workflows
- [x] GoBD-oriented technical procedural-documentation baseline
- [x] immutable historical seller snapshots for posted sales invoices and credit notes
- [x] immutable buyer identity and exact issued XML retention for finalized sales invoices

### Backup and recovery

- [x] backup creation and archive validation
- [x] restore with safety-backup behavior
- [x] automatic backup scheduling and retention
- [x] SQLite integrity check and compaction
- [x] clean/corrupt/interrupted/unavailable-target regression coverage where automatable

### Supply chain and release integrity

- [x] NuGet locked restore
- [x] direct/transitive vulnerability audit
- [x] CycloneDX SBOM and dependency/license evidence
- [x] CRA technical-evidence package
- [x] source/tag binding and SHA-256 release manifest
- [x] Authenticode/timestamp workflow support without private key in repository

### Electronic invoicing

- [x] EN 16931-oriented semantic invoice model
- [x] XRechnung 3.0-oriented CII generation
- [x] deterministic invoice/credit-note serialization in the semantic/generator layer
- [x] application-level business-term validation
- [x] pinned KoSIT representative conformance validation
- [x] structured buyer master data for Buyer Reference, electronic endpoint/scheme, tax identity, and billing address
- [x] atomic sales-invoice posting with seller snapshot, buyer snapshot, and XRechnung generation
- [x] exact issued sales-invoice XML retained immutably with SHA-256 tamper detection
- [x] posted Invoice workspace exports the verified persisted XRechnung XML instead of regenerating it
- [x] fail-closed handling for incomplete identity and unsupported ambiguous tax scenarios

### Software quality and accessibility

- [x] release builds with warnings-as-errors in quality gates
- [x] bounded regression suites
- [x] Windows Server 2022/2025 CI quality matrix
- [x] 100,000-record SQLite performance baseline
- [x] static keyboard-focus regression check
- [x] core 4.5:1 contrast checks
- [x] shell automation names and textual status semantics
- [x] accessibility/manual release matrix documented

## Required production acceptance before 1.0

### Database providers

- [ ] supported Windows desktop editions defined and tested
- [ ] SQL Server supported-version clean-install and migration matrix
- [ ] MySQL/MariaDB supported-version clean-install and migration matrix
- [ ] live backup/restore/recovery drills for every advertised provider
- [ ] live multi-client concurrency tests
- [ ] representative network latency/load tests
- [ ] Windows ACL-denied recovery scenario

### User interface/accessibility

- [ ] keyboard-only critical-workflow walkthrough
- [ ] focus-order/no-keyboard-trap verification
- [ ] Accessibility Insights or equivalent scan across production screens
- [ ] Windows Narrator baseline
- [ ] 100%, 125%, 150%, and 200% DPI/scaling acceptance
- [ ] manual disabled/hover/selected/error/warning/success state review
- [ ] localization/formatting acceptance for supported cultures

### Security and operations

- [ ] production backup storage/ACL/encryption configuration accepted
- [ ] production vulnerability-reporting channel operational
- [ ] named incident/security owners and escalation path
- [ ] security update/support period published for the release
- [ ] final dependency/license review for release commit
- [ ] no unresolved Critical vulnerability; High findings resolved or explicitly accepted under policy

### Release engineering

- [ ] all CI/security/quality/e-invoice workflows pass on release commit
- [ ] production Authenticode signing identity configured
- [ ] Authenticode signature verified on produced artifacts
- [ ] production timestamp verified
- [ ] installer/package tested
- [ ] upgrade/rollback/uninstall tested
- [ ] final application version/tag finalized
- [ ] release notes, known limitations, hashes, SBOM, and support information published

### Electronic invoicing

- [x] sales-invoice model integrated into the persisted operational posting workflow
- [x] issued sales-invoice XML retained immutably with invoice linkage and integrity verification
- [x] seller identity and payment data sourced from the controlled Company master and frozen at posting
- [x] buyer/customer identity and structured billing/tax data frozen at posting
- [ ] explicit EN 16931 VAT-category and exemption/reason semantics persisted for zero-rated, exempt, and reverse-charge lines
- [ ] electronic credit-note buyer/XML finalization implemented before advertising that channel
- [ ] organization/recipient-specific electronic routing configuration validated
- [ ] every advertised tax/profile/channel scenario validated against the applicable production KoSIT/XRechnung version
- [ ] controlled remediation procedure approved for legacy posted invoices without historical finalization records
- [ ] PDF/A-3 implemented and validated before any ZUGFeRD/Factur-X support claim

### Compliance/legal/organizational

- [ ] final GDPR/DSGVO controller/deployment assessment
- [ ] concrete retention periods and data-subject procedures approved
- [ ] GoBD tax-relevance and organization-specific procedural documentation approved
- [ ] final CRA scope/economic-operator/classification/conformity assessment completed
- [ ] Declaration/CE/user/manufacturer information completed where applicable
- [ ] regulatory incident-reporting/tabletop readiness completed where applicable

## Out of 1.0 scope unless separately approved

- barcode scanning/generation
- label template design/printing
- payment collection/accounts receivable/general ledger
- enterprise MFA/Entra ID/OIDC/SAML unless Phase 8 scope is pulled forward
