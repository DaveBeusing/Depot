# Depot Version 1.0 Release Checklist

Updated: 2026-08-28

## Status

- [ ] Ready for release

Depot is on the `0.15.x-preview` line with core database schema **29**, Sales feature schema **8**, Finance feature schema **8**, and Help manifest **1.15**. Checked items represent implemented technical controls/evidence only; they do not replace provider, legal, accounting, accessibility, signing, localization or deployment acceptance.

## Implemented Finance baseline

### F0 — International Finance Foundation
- [x] legal entities, currencies/FX, fiscal calendars/periods, charts/accounts, books, journals, dimensions, tax registrations and number sequences
- [x] Finance schema v1

### F1 — General Ledger & Posting Engine
- [x] immutable balanced journals, reporting-currency/FX snapshots, posting profiles, validation, idempotency, number allocation, Audit evidence and linked reversals
- [x] Finance schema v2

### F2 — Accounts Receivable
- [x] Sales → AR → GL, open items, payments/allocations, write-offs/reversals, aging/statements and dunning
- [x] Finance > Receivables / schema v3

### F3 — Accounts Payable
- [x] supplier documents/open items, AP → GL, three-way match, explicit exceptions, payments/reversals, aging/statements and segregation of duties
- [x] Finance > Payables / schema v4

### F4 — Inventory Accounting
- [x] FIFO valuation, GRNI/COGS, reversals, inventory adjustments, PPV, landed cost, historical valuation and Inventory ↔ GL reconciliation
- [x] Finance > Inventory Accounting / schema v6

### F5 — Banking and Payments
- [x] bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position
- [x] Finance > Banking / schema v7

### F6 — Financial Reporting
- [x] Trial Balance and General Ledger detail
- [x] Balance Sheet and Profit & Loss
- [x] Cash Flow with explicit account classification
- [x] AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS
- [x] optional GL dimension filtering
- [x] explicit financial-statement/cash-flow/tax/cash/COGS mappings
- [x] deterministic CSV export
- [x] immutable report snapshots with SHA-256 parameter/content hashes and operation idempotency
- [x] Finance > Financial Reporting and granular RBAC
- [x] Finance schema v8 for SQLite, SQL Server and MySQL/MariaDB
- [x] Help manifest 1.15 / `finance.reporting`
- [x] regression coverage for schema, F1 reporting currency/cutoff, mappings, snapshots and export determinism

## Other implemented technical baseline

- [x] first-run administrator creation, hardened authentication and multi-role RBAC
- [x] service-layer authorization and creator/approver controls where implemented
- [x] immutable/correction-oriented retained business records and Audit evidence
- [x] backup/recovery controls and privacy export
- [x] dependency locks, NuGet audit, SBOM/evidence and release-integrity workflows
- [x] Sales Invoice seller/buyer/XRechnung finalization and persisted XML integrity evidence
- [x] bounded regression/quality/accessibility CI controls

## Required production acceptance before 1.0

### Providers and Finance
- [ ] supported Windows/database versions finalized
- [ ] live SQL Server Finance v1→v8 migration, concurrency, locking, recovery and performance matrix
- [ ] live MySQL/MariaDB Finance v1→v8 migration, concurrency, locking, recovery and performance matrix
- [ ] provider-specific AR/AP/FIFO/Banking/reporting deadlock/retry acceptance
- [ ] deployment legal entity/chart/book/calendar/posting-profile/inventory/bank/reporting-policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end reporting procedures
- [ ] AP/payment-proposal segregation-of-duties review
- [ ] accounting/report-snapshot retention/export/backup/restore procedures
- [ ] jurisdiction-specific accounting/tax/localization acceptance

### UI/accessibility
- [ ] keyboard-only critical-workflow walkthrough including all Finance workspaces
- [ ] focus/no-keyboard-trap review
- [ ] Narrator/Accessibility Insights baseline
- [ ] 100/125/150/200% DPI acceptance

### Security/release engineering
- [ ] production backup/security ownership and vulnerability-reporting process accepted
- [ ] production Authenticode identity and timestamp verified
- [ ] installer/package upgrade/rollback/uninstall accepted
- [ ] final release notes, known limitations, hashes, SBOM and support information published

### Electronic invoicing
- [ ] remaining EN 16931 special-tax semantics accepted
- [ ] electronic credit-note finalization where advertised
- [ ] recipient/channel routing acceptance
- [ ] every advertised production scenario validated against applicable KoSIT/XRechnung release
- [ ] PDF/A-3 before any ZUGFeRD/Factur-X claim

### Legal/organizational
- [ ] GDPR/DSGVO deployment assessment and retention procedures
- [ ] organization-specific GoBD/accounting/reporting procedures
- [ ] final CRA applicability/classification/conformity work
- [ ] qualified accounting/tax/legal review for each marketed Finance localization

## Remaining Finance package

F0-F6 are implemented. Remaining Finance package:

- F7 Localization/statutory extension framework and country packs

## Other out-of-scope items unless separately approved

- barcode scanning/generation
- label template design/printing
- enterprise MFA/Entra/OIDC/SAML until separately scoped
- direct bank connectivity/payment initiation and jurisdiction-specific statutory filing certification until separately scoped
