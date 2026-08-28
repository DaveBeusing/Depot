# Depot Version 1.0 Release Checklist

Updated: 2026-08-28

## Status

- [ ] Ready for release

Depot is on the `0.15.x-preview` line with core database schema **29**, Sales feature schema **8**, Finance feature schema **4**, and Help manifest **1.12**. Checked items represent implemented technical controls/evidence only; they do not replace provider, legal, accounting, accessibility, signing, localization or deployment acceptance.

## Implemented Finance baseline

### F0 — International Finance Foundation

- [x] legal entities, currencies/exchange rates, fiscal calendars/periods
- [x] charts/accounts, accounting books, journals, dimensions, tax registrations, number sequences
- [x] localization/tax/exchange-rate extension boundaries
- [x] Finance schema v1

### F1 — General Ledger & Posting Engine

- [x] immutable balanced double-entry journals
- [x] transaction/reporting currency and FX snapshots
- [x] posting profiles
- [x] period/date/legal-entity/account/dimension validation
- [x] source/operation idempotency
- [x] transactional number allocation and Audit evidence
- [x] explicit linked reversals
- [x] sensitive manual-journal permission
- [x] Finance schema v2

### F2 — Accounts Receivable

- [x] Sales Invoice/Credit Note → AR → GL integration
- [x] customer open items and allocations
- [x] partial/full payments, overpayments and later credit allocation
- [x] payment and write-off reversals
- [x] aging/statements and dunning
- [x] Finance > Receivables
- [x] Finance schema v3

### F3 — Accounts Payable

- [x] supplier invoice/credit-note lifecycle
- [x] AP open items with source/journal linkage
- [x] configured AP → F1 GL posting/reversal transaction
- [x] PO / goods-receipt / supplier-invoice matching
- [x] fail-closed match behavior with no implicit tolerance
- [x] explicit match-exception approval and retained reason
- [x] supplier-document approval and match-exception approval separated in RBAC
- [x] supplier payments, partial/full allocation, overpayments and later allocation
- [x] supplier-payment reversal restoring every active allocation
- [x] AP aging and supplier statements
- [x] Finance > Payables
- [x] Finance schema v4 for SQLite, SQL Server and MySQL/MariaDB
- [x] Help manifest 1.12 / `finance.payables`
- [x] regression coverage for schema, AP→GL, matching, payment reversal, RBAC and retained records

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
- [ ] live SQL Server Finance v1→v4 migration, concurrency, locking, recovery and performance matrix
- [ ] live MySQL/MariaDB Finance v1→v4 migration, concurrency, locking, recovery and performance matrix
- [ ] provider-specific AP matching/settlement/reversal deadlock/retry acceptance
- [ ] deployment legal entity/chart/book/calendar/posting-profile approval
- [ ] AR/AP subledger-to-GL reconciliation procedures and exception handling
- [ ] AP supplier-document approval/match-exception segregation-of-duties review
- [ ] customer/supplier payment evidence/reconciliation procedure until Banking is implemented
- [ ] accounting record retention/export/backup/restore procedures
- [ ] jurisdiction-specific accounting/tax/localization acceptance

### UI/accessibility

- [ ] keyboard-only critical-workflow walkthrough including Finance > Receivables and Finance > Payables
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
- [ ] organization-specific GoBD/accounting procedures
- [ ] final CRA applicability/classification/conformity work
- [ ] qualified accounting/tax/legal review for each marketed Finance localization

## Finance packages outside completed baseline

F0-F3 are implemented. Remaining packages are:

- F4 Inventory Accounting / valuation / COGS / GRNI
- F5 Banking and Payments
- F6 Financial Reporting
- F7 Localization/statutory packages

## Other out-of-scope items unless separately approved

- barcode scanning/generation
- label template design/printing
- enterprise MFA/Entra/OIDC/SAML until separately scoped
