# Depot Roadmap

Updated: 2026-08-28

This roadmap reflects the current implementation state. “Implemented” means the workflow/control exists in code and automated evidence where practical; it does not by itself mean production certification or legal conformity.

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

- [x] enriched item/inventory/master-data management
- [x] movement-derived serial/lot traceability with workflow capture, block/expiry controls and reversal-safe identity
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

### Company and document identity

- [x] Administration > Company as authoritative legal seller/document profile
- [x] seller identity projection into generated sales and fulfillment documents
- [x] immutable issuer snapshots for posted sales invoices and credit notes
- [x] no fallback from historical posted documents to mutable current seller master data

### Electronic invoicing

- [x] EN 16931-oriented semantic invoice model
- [x] deterministic UN/CEFACT CII generation targeted at XRechnung 3.0
- [x] invoice and credit-note type handling in the semantic/generator layer
- [x] application-level business-term validation
- [x] representative XRechnung fixture and pinned KoSIT conformance workflow
- [x] structured Customer buyer identity including Buyer Reference, endpoint/scheme, tax identity, and structured billing address
- [x] atomic sales-invoice posting/finalization with seller snapshot, buyer snapshot, and generated XML
- [x] immutable retention of exact issued sales-invoice XML with SHA-256 integrity verification
- [x] verified XRechnung XML export from the posted Invoice workspace without regeneration
- [x] fail-closed posting for incomplete invoice identity and unsupported ambiguous tax scenarios

### Finance F0 — International Finance Foundation

- [x] legal entities with explicit country and functional currency
- [x] currency contracts with ISO 4217-style syntax and no default currency
- [x] sourced/effective exchange-rate model and `IExchangeRateSource`
- [x] fiscal calendars and accounting periods
- [x] charts of accounts and account master contracts
- [x] accounting books with configurable accounting-standard code
- [x] journal-definition master data
- [x] accounting dimensions and values
- [x] structured tax registrations
- [x] Finance number sequences
- [x] `ITaxDeterminationService` and `IFinanceLocalizationProvider` boundaries
- [x] Finance feature schema v1 for SQLite, SQL Server, and MySQL/MariaDB
- [x] granular Finance RBAC and Finance system-role assignment
- [x] Finance architecture/compliance documentation and embedded Help topic

### Finance F1 — General Ledger & Posting Engine

- [x] immutable journal-entry headers and lines
- [x] balanced double-entry invariant in transaction and reporting currency
- [x] transaction/reporting currency plus exchange-rate snapshot
- [x] posting profiles with named amount-key account determination
- [x] source-document and operation idempotency
- [x] accounting-period open/date/legal-entity enforcement
- [x] active account/chart/direct-posting and required-dimension validation
- [x] Finance General Ledger number-sequence allocation inside the posting transaction
- [x] explicit linked reversal/correction transactions with exact counter amounts
- [x] transactionally persisted Audit Log evidence and rollback on audit failure
- [x] optimistic posting-profile concurrency and database uniqueness boundaries
- [x] separate sensitive permission for free manual journals
- [x] Finance feature schema v2 for SQLite, SQL Server, and MySQL/MariaDB
- [x] General Ledger Help topic
- [x] regression coverage for balance, idempotency, closed periods, audit rollback, profile posting and reversals

### Finance F2 — Accounts Receivable

- [x] customer subledger / receivable debit and credit open items
- [x] Sales Invoice/Credit Note → AR → F1 GL integration through configured posting profiles
- [x] atomic source/subledger/ledger/audit transaction when AR is configured
- [x] explicit F2 dependency on current Sales feature schema
- [x] payment posting and allocation including partial payments and overpayments
- [x] later allocation of unapplied customer credit
- [x] payment reversal restoring every active allocation from the payment credit
- [x] due-date/outstanding state and customer statement projection
- [x] write-offs with dedicated post/reverse authorization and linked GL reversals
- [x] configurable dunning levels and idempotent retained dunning runs
- [x] aged receivables with separate unapplied-credit visibility
- [x] Finance > Receivables workspace and context Help
- [x] Finance feature schema v3 for SQLite, SQL Server, and MySQL/MariaDB
- [x] Help manifest 1.11 with `finance.receivables`
- [x] regression coverage for schema, source idempotency, balance linkage, allocations/reversals, write-offs, aging, dunning, RBAC and record classification

## Finance roadmap

### F3 — Accounts Payable

- [ ] supplier invoices/credit notes
- [ ] supplier subledger/open items
- [ ] purchase-order/goods-receipt/supplier-invoice matching
- [ ] approval and GL integration
- [ ] supplier settlement/payment-run preparation with segregation of duties

### F4 — Inventory Accounting

- [ ] valuation layers/policies
- [ ] inventory-to-GL posting
- [ ] COGS, GRNI, variance and landed-cost accounting
- [ ] period-end inventory/GL reconciliation

### F5 — Banking and payments

- [ ] bank accounts/statements
- [ ] CSV and ISO 20022 statement import
- [ ] payment proposal/execution abstractions
- [ ] reconciliation and cash-position integration

### F6 — Financial reporting

- [ ] trial balance and General Ledger report
- [ ] balance sheet and profit/loss
- [ ] cash-flow and subledger aging reports
- [ ] tax summary, inventory valuation and COGS
- [ ] dimension-aware reporting and exports

### F7 — Localization framework

- [ ] Generic reference localization
- [ ] EU layer and German reference implementation
- [ ] additional country packs based on product demand
- [ ] effective-dated localization/compliance registry

### Quality and accessibility

- [x] Windows Server 2022/2025 quality matrix on .NET 10
- [x] zero-warning build gate
- [x] bounded regression suites and 100,000-record SQLite performance baseline
- [x] static accessibility checks for focus visibility, key contrast pairs, automation names, and non-color status semantics
- [x] WCAG 2.2 AA / EN 301 549 inspired desktop engineering baseline

## Remaining production/release acceptance

These items require real infrastructure, signing identities, interactive desktop testing, organization-specific legal decisions, additional commercial tax/accounting semantics, or a marketed-product decision.

### Providers and recovery

- [ ] live SQL Server clean-install/migration matrix
- [ ] live MySQL/MariaDB clean-install/migration matrix
- [ ] live backup/restore/recovery drills for every advertised provider/version
- [ ] multi-client concurrency and representative latency/load tests
- [ ] Finance v1 -> v3 live-server migration and concurrent GL/AR posting acceptance
- [ ] provider-specific Finance settlement/reversal locking/deadlock/retry acceptance
- [ ] Windows ACL-denied recovery scenario

### Accessibility and desktop acceptance

- [ ] keyboard-only walkthrough of all critical workflows including Finance > Receivables
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

- [ ] persist explicit EN 16931 tax-category and exemption/reason semantics for zero-rated, exempt, and reverse-charge commercial lines
- [ ] extend buyer/XML finalization to electronic credit notes where advertised
- [ ] configure organization/recipient-specific routing and delivery channels
- [ ] validate every advertised tax/profile/channel scenario with applicable production KoSIT/XRechnung release
- [ ] define controlled remediation procedures for legacy posted invoices without historical finalization records
- [ ] implement and validate PDF/A-3 before claiming ZUGFeRD/Factur-X support

### Finance/accounting operational acceptance

- [ ] deployment-specific legal entity, chart/book, fiscal calendar, AR posting-profile approval
- [ ] subledger-to-GL reconciliation procedures and exception handling
- [ ] customer-payment evidence/import/reconciliation procedure until F5 Banking is available
- [ ] write-off policy, approval thresholds, evidence and tax treatment
- [ ] dunning/collections wording, fees/interest, delivery proof and legal escalation review
- [ ] accounting-record retention/export procedures

### Legal/organizational acceptance

- [ ] deployment-specific GDPR lawful bases, notices, retention periods, processor arrangements, and data-subject procedure
- [ ] organization-specific GoBD procedural documentation and tax-relevance determination
- [ ] final CRA scope/classification/economic-operator/conformity assessment and CE/Declaration steps where applicable
- [ ] production vulnerability-reporting and regulatory incident contacts
- [ ] deployment-specific accounting/tax/localization validation for every advertised Finance jurisdiction
- [ ] deployment-specific period-close/reopen and segregation-of-duties acceptance

## Phase 8 — Enterprise readiness

Planned based on customer demand:

- [ ] MFA
- [ ] Microsoft Entra ID / OIDC
- [ ] SAML where justified
- [ ] centralized audit/SIEM integration
- [ ] enterprise deployment/hardening guide
- [ ] ISO/IEC 27001 customer-control mapping and security-questionnaire evidence
- [ ] NIS2-influenced customer/supply-chain requirements

## Out of current completed scope

- barcode scanning/generation
- label template design and printing
- Finance packages F3-F7 until their roadmap items are implemented and verified
- jurisdiction-specific statutory filing/localization packages until explicitly implemented and accepted
