# Depot Roadmap

Updated: 2026-08-28

This roadmap distinguishes implemented technical controls from production/legal certification. “Implemented” means the repository/application behavior exists and is covered by automated evidence where practical.

## Implemented foundations

- [x] .NET 10 WPF / MVVM application
- [x] `Views → ViewModels → Services → Repositories → DatabaseAccess`
- [x] provider-neutral SQLite / SQL Server / MySQL-MariaDB persistence
- [x] cancellation, paging, stale-request protection and provider-controlled transactions
- [x] shared dark WPF design system and workspace shell
- [x] first-run administrator bootstrap, hardened authentication, database-backed RBAC and service authorization
- [x] immutable/correction-oriented retained business records, Audit evidence, backup/recovery controls, privacy export and supply-chain/release evidence
- [x] inventory/warehouse/purchasing/sales operational workflows
- [x] immutable seller/buyer Sales Invoice identity and persisted XRechnung XML integrity evidence

## Finance roadmap

### F0 — International Finance Foundation — complete

- [x] legal entities, currencies/exchange rates
- [x] fiscal calendars/accounting periods
- [x] charts/accounts, accounting books, journals
- [x] dimensions, tax registrations, number sequences
- [x] localization/tax/exchange-rate extension boundaries
- [x] Finance schema **1**

### F1 — General Ledger & Posting Engine — complete

- [x] immutable balanced journal entries/lines
- [x] transaction/reporting currency and FX snapshots
- [x] posting profiles and amount-key account determination
- [x] open-period/date/legal-entity/account/dimension validation
- [x] operation/source idempotency
- [x] transactional number allocation and Audit evidence
- [x] explicit linked reversals
- [x] separate sensitive manual-journal permission
- [x] Finance schema **2**

### F2 — Accounts Receivable — complete

- [x] Sales Invoice/Credit Note → AR → GL integration
- [x] customer open items
- [x] partial/full payments, overpayments and later allocation
- [x] payment and write-off reversal
- [x] aging and customer statements
- [x] dunning policies/runs
- [x] Finance > Receivables workspace
- [x] granular AR/payment/write-off/dunning RBAC
- [x] Finance schema **3**

### F3 — Accounts Payable — complete

- [x] supplier invoices and supplier credit notes
- [x] draft/submission/approval/rejection/post/reversal lifecycle
- [x] supplier AP open items with source and GL linkage
- [x] configured AP → F1 GL integration in one transaction
- [x] PO / goods-receipt / supplier-invoice matching
- [x] fail-closed matching with no implicit tolerance
- [x] explicit match-exception approval with retained reason
- [x] separate supplier-document and match-exception approval permissions
- [x] partial/full supplier payments and unapplied debit balances
- [x] later allocation and overpayment handling
- [x] supplier-payment reversal restoring all active allocations
- [x] AP aging and supplier statements
- [x] Finance > Payables workspace
- [x] Finance schema **4** for SQLite, SQL Server and MySQL/MariaDB
- [x] Help manifest **1.12** / `finance.payables`
- [x] regression coverage for schema, AP→GL, matching, payment reversal, RBAC and retained-record classification

### F4 — Inventory Accounting — next

- [ ] valuation layers/policies
- [ ] inventory-to-GL posting
- [ ] COGS and GRNI
- [ ] purchase/inventory variances
- [ ] landed-cost allocation
- [ ] period-end inventory/GL reconciliation

### F5 — Banking and Payments

- [ ] bank accounts/statements
- [ ] CSV and ISO 20022 statement import
- [ ] payment proposal/execution abstractions
- [ ] reconciliation and cash-position integration

### F6 — Financial Reporting

- [ ] trial balance and General Ledger report
- [ ] balance sheet and profit/loss
- [ ] cash-flow and subledger aging reports
- [ ] tax summary, inventory valuation and COGS
- [ ] dimension-aware reporting and exports

### F7 — Localization Framework

- [ ] Generic reference localization
- [ ] EU layer and German reference implementation
- [ ] additional country packs based on demand
- [ ] effective-dated localization/compliance registry

## Remaining production/release acceptance

- [ ] live SQL Server Finance v1→v4 clean-install/migration/concurrency/recovery matrix
- [ ] live MySQL/MariaDB Finance v1→v4 clean-install/migration/concurrency/recovery matrix
- [ ] provider-specific AP settlement/reversal/matching locking and deadlock/retry acceptance
- [ ] representative Finance performance/load tests
- [ ] deployment legal entity/chart/book/calendar/posting-profile approval
- [ ] AR/AP subledger-to-GL reconciliation procedures
- [ ] AP approval and match-exception segregation-of-duties role review
- [ ] supplier-payment evidence/reconciliation procedure until Banking is implemented
- [ ] retention/export and organization-specific accounting/tax procedures
- [ ] keyboard/focus/Narrator/DPI acceptance including Finance > Receivables and Finance > Payables
- [ ] production Authenticode/timestamp and installer/upgrade/rollback acceptance
- [ ] remaining electronic-invoice tax/profile/routing scenarios
- [ ] final GDPR/GoBD/CRA/accounting/localization legal/organizational acceptance

## Phase 8 — Enterprise readiness

- [ ] MFA
- [ ] Microsoft Entra ID / OIDC
- [ ] SAML where justified
- [ ] centralized audit/SIEM integration
- [ ] enterprise deployment/hardening guide
- [ ] ISO/IEC 27001 customer-control mapping

## Out of current completed scope

- barcode scanning/generation
- label template design/printing
- Finance packages F4-F7 until separately implemented and verified
- jurisdiction-specific statutory filing/localization packages until explicitly implemented and accepted
