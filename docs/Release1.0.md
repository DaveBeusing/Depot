# Depot Version 1.0 Release Checklist

Updated: 2026-09-08

## Status

- [ ] Ready for release

Depot is on the `0.15.x-preview` line with Core database schema **30**, Sales feature schema **11**, Finance feature schema **9**, User Sessions schema **3**, Security Events schema **2**, and Help manifest **1.21**. Checked items represent implemented technical controls/evidence only; they do not replace legal, accounting, accessibility, signing, localization or deployment acceptance.

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
- [x] local backup/recovery controls and privacy export
- [x] dependency locks, NuGet audit, SBOM/evidence and release-integrity workflows
- [x] centralized per-item Customer → Region → Global Sales pricing with optional customer assignment and retained document source snapshots
- [x] Sales Invoice seller/buyer/XRechnung finalization and persisted XML integrity evidence
- [x] bounded regression/quality/accessibility CI controls

## Database provider production acceptance

The generic technical provider gate is complete for the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md):

- [x] supported database certification baselines defined
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
- [x] representative 100,000-row indexed provider performance guard

Versions outside these baselines are not implicitly supported. The provider gate is a database/runtime certification only.

## Required production acceptance before 1.0

### Finance, deployment and operations

- [ ] deployment Legal Entity/chart/book/calendar/posting-profile/inventory/bank/reporting/localization-policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end reporting procedures
- [ ] AP/payment-proposal segregation-of-duties review
- [ ] accounting/report/localization evidence retention/export/backup/restore operating procedures
- [ ] qualified review of each enabled country pack and every `ConfigurationRequired` / `ExternalProcedureRequired` item
- [ ] jurisdiction-specific accounting/tax/localization acceptance
- [ ] customer-specific volume/network/concurrency sizing beyond the generic provider regression guard

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
- [ ] organization-specific GoBD/accounting/reporting/localization procedures
- [ ] final CRA applicability/classification/conformity work
- [ ] qualified accounting/tax/legal review for each marketed Finance localization

## Demand-driven extensions

Additional country/statutory packs are supported by the localization framework when metadata/configuration is sufficient. They must not be marketed as compliant/certified without separate qualified acceptance. Direct bank connectivity/payment initiation, jurisdiction-specific statutory filing implementations, additional costing methods and other executable behavior require separately scoped implementation.
