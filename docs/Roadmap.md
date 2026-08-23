# Depot Roadmap

This roadmap reflects the implementation on the current `compliance` branch. “Implemented” means the workflow/control exists in code and automated evidence where practical; it does not by itself mean production certification or legal conformity.

## Implemented foundations

### Architecture and platform

- [x] .NET 10 WPF application with MVVM layering
- [x] provider-neutral `DatabaseAccess`
- [x] SQLite, SQL Server, and MySQL/MariaDB provider implementations
- [x] encrypted persistent database settings
- [x] asynchronous data-access patterns, cancellation, paging, and debounced search
- [x] shared dark design system and workspace/tab shell

### Security and compliance

- [x] first-run administrator bootstrap; no shared production default password
- [x] database-backed multi-role RBAC with service-layer authorization
- [x] password policy, login throttling, PBKDF2-HMAC-SHA256 versioned hashing
- [x] DPAPI-protected database secrets and encrypted remote database transport
- [x] audit viewer, filtering, CSV export, sanitized details, and structured evidence export
- [x] automatic backup retention and documented recovery controls
- [x] privacy data discovery/export with secret exclusion
- [x] business-record classification, immutable final states, traceable correction/reversal/credit workflows
- [x] CycloneDX SBOM, dependency locks, NuGet vulnerability audit, license/dependency evidence
- [x] CRA risk/evidence/update/vulnerability-management baseline
- [x] release-integrity workflow with source binding, SHA-256 manifests, and prepared Authenticode/timestamp support

### Inventory, warehouse, purchasing, and sales

- [x] item/inventory/master-data management
- [x] immutable stock movements and counter-movement corrections
- [x] transfers, inventory counts, material issues/returns, shipping, picking/packing, and customer returns
- [x] suppliers, supplier items, purchase orders, approvals, goods receipts, and supplier returns
- [x] customers/contacts, quotes, pricing, sales orders, approvals, reservations/backorders, shipments, invoices, credit notes, and timelines
- [x] creator/approver separation and audited administrator overrides

### Privacy and records

- [x] personal-data inventory and retention/lifecycle model
- [x] Administration > Privacy Data discovery and JSON export
- [x] GoBD-oriented technical procedural documentation
- [x] business-record JSON evidence package from Audit Log
- [x] atomic business mutation + audit persistence for reviewed retained workflows

### Electronic invoicing

- [x] EN 16931-oriented semantic invoice model
- [x] deterministic UN/CEFACT CII generation targeted at XRechnung 3.0
- [x] invoice and credit-note type handling
- [x] application-level business-term validation
- [x] representative XRechnung fixture and pinned KoSIT conformance workflow

### Quality and accessibility

- [x] Windows Server 2022/2025 quality matrix on .NET 10
- [x] zero-warning build gate
- [x] bounded regression suites and 100,000-record SQLite performance baseline
- [x] static accessibility checks for focus visibility, key contrast pairs, automation names, and non-color status semantics
- [x] WCAG 2.2 AA / EN 301 549 inspired desktop engineering baseline

## Remaining production/release acceptance

These items require real infrastructure, signing identities, interactive desktop testing, organization-specific legal decisions, or a marketed-product decision rather than additional generic repository code.

### Providers and recovery

- [ ] live SQL Server clean-install/migration matrix
- [ ] live MySQL/MariaDB clean-install/migration matrix
- [ ] live backup/restore/recovery drills for every advertised provider/version
- [ ] multi-client concurrency and representative latency/load tests
- [ ] Windows ACL-denied recovery scenario

### Accessibility and desktop acceptance

- [ ] keyboard-only walkthrough of all critical workflows
- [ ] focus-order/no-keyboard-trap review
- [ ] Accessibility Insights or equivalent automation inspection
- [ ] Windows Narrator baseline
- [ ] visual DPI/scaling acceptance at 100%, 125%, 150%, and 200%
- [ ] manual disabled/selected/hover/error/warning/success visual-state review

### Release engineering

- [ ] production Authenticode certificate configured and validated
- [ ] production timestamp validation
- [ ] installer/package, upgrade, rollback, and uninstall acceptance
- [ ] final supported Windows/database matrix
- [ ] release notes and known limitations

### Electronic invoicing

- [ ] integrate structured invoice XML into the operational persisted invoice workflow
- [ ] retain issued XML immutably with correction/reference linkage
- [ ] validate every advertised tax/profile/channel scenario with the applicable production KoSIT/XRechnung release
- [ ] implement and validate PDF/A-3 before claiming ZUGFeRD/Factur-X support

### Legal/organizational acceptance

- [ ] deployment-specific GDPR lawful bases, notices, retention periods, processor arrangements, and data-subject procedure
- [ ] organization-specific GoBD procedural documentation and tax-relevance determination
- [ ] final CRA scope/classification/economic-operator/conformity assessment and CE/Declaration steps where applicable
- [ ] production vulnerability-reporting and regulatory incident contacts

## Phase 8 — Enterprise readiness

Planned based on customer demand:

- [ ] MFA
- [ ] Microsoft Entra ID / OIDC
- [ ] SAML where justified
- [ ] centralized audit/SIEM integration
- [ ] enterprise deployment/hardening guide
- [ ] ISO/IEC 27001 customer-control mapping and security-questionnaire evidence
- [ ] NIS2-influenced customer/supply-chain requirements

## Out of current scope

- barcode scanning/generation
- label template design and printing
- payment collection
- accounts receivable
- general ledger/accounting
