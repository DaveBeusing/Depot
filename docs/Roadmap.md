# Depot Roadmap

Updated: 2026-08-28

This roadmap distinguishes implemented technical controls from production/legal certification. “Implemented” means repository/application behavior exists and is covered by automated evidence where practical.

## Implemented foundations

- [x] .NET 10 WPF / MVVM application
- [x] `Views → ViewModels → Services → Repositories → DatabaseAccess`
- [x] provider-neutral SQLite / SQL Server / MySQL-MariaDB persistence
- [x] cancellation, paging, stale-request protection and provider-controlled transactions
- [x] shared dark WPF design system and workspace shell
- [x] hardened authentication, database-backed RBAC and service authorization
- [x] immutable/correction-oriented retained business records, Audit evidence, backup/recovery controls, privacy export and release evidence
- [x] inventory/warehouse/purchasing/sales operational workflows
- [x] immutable seller/buyer Sales Invoice identity and persisted XRechnung XML integrity evidence

## Finance roadmap

### F0 — International Finance Foundation — complete
- [x] legal entities, currencies/exchange rates, fiscal calendars/accounting periods
- [x] charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences
- [x] localization/tax/exchange-rate extension boundaries
- [x] Finance schema **1**

### F1 — General Ledger & Posting Engine — complete
- [x] immutable balanced journal entries/lines
- [x] transaction/reporting currency and FX snapshots
- [x] posting profiles and amount-key account determination
- [x] period/account/dimension validation, idempotency, Audit evidence and linked reversals
- [x] Finance schema **2**

### F2 — Accounts Receivable — complete
- [x] Sales Invoice/Credit Note → AR → GL integration
- [x] customer open items, payments, allocation, write-off, aging, statements and dunning
- [x] Finance > Receivables workspace and RBAC
- [x] Finance schema **3**

### F3 — Accounts Payable — complete
- [x] supplier invoice/credit-note lifecycle and AP open items
- [x] AP → F1 GL posting/reversal
- [x] three-way matching and explicit exception authority
- [x] supplier payments, allocations, reversal, aging and statements
- [x] Finance > Payables workspace and segregation-of-duties RBAC
- [x] Finance schema **4**

### F4 — Inventory Accounting — complete
- [x] provider-neutral FIFO valuation layers and consumption evidence
- [x] Goods Receipt → inventory/GRNI and Sales Shipment → FIFO/COGS posting
- [x] linked valuation reversals, inventory-count adjustments, purchase-price variance and landed cost
- [x] historical as-of valuation and Inventory ↔ GL reconciliation snapshots
- [x] Finance > Inventory Accounting workspace
- [x] Finance schema **6**
- [x] Help `finance.inventory-accounting`

### F5 — Banking and Payments — complete
- [x] bank accounts, immutable statements and normalized CSV / ISO 20022 camt.053 import
- [x] supplier payment proposals, creator/approver segregation and AP execution
- [x] AR/AP/GL reconciliation and explicit reversal
- [x] cash-position comparison
- [x] Finance > Banking workspace and RBAC
- [x] Finance schema **7**
- [x] Help `finance.banking`

### F6 — Financial Reporting — complete
- [x] trial balance and General Ledger detail
- [x] balance sheet and profit/loss
- [x] cash-flow reporting using explicit cash/counterpart classification
- [x] Accounts Receivable and Accounts Payable aging reports
- [x] tax summary, historical inventory valuation and Cost of Goods Sold
- [x] optional accounting-dimension filtering for GL-derived reports
- [x] explicit per-account financial-statement, cash-flow, tax, cash-account and COGS mappings
- [x] deterministic CSV export
- [x] immutable, idempotent report snapshots with parameter/content SHA-256 hashes
- [x] Finance > Financial Reporting workspace and granular RBAC
- [x] `FinanceReportSnapshot` retained as AuditEvidence
- [x] Finance schema **8** for SQLite, SQL Server and MySQL/MariaDB
- [x] Help manifest **1.15** / `finance.reporting`
- [x] regression coverage for schema, F1 ledger cutoff/reporting currency, mappings, snapshots and export determinism

### F7 — Localization Framework — next
- [ ] Generic reference localization
- [ ] EU layer and German reference implementation
- [ ] additional country packs based on demand
- [ ] effective-dated localization/compliance registry

## Remaining production/release acceptance

- [ ] live SQL Server Finance v1→v8 clean-install/migration/concurrency/recovery matrix
- [ ] live MySQL/MariaDB Finance v1→v8 clean-install/migration/concurrency/recovery matrix
- [ ] provider-specific AR/AP/inventory/banking/reporting locking and deadlock/retry acceptance
- [ ] representative Finance performance/load tests including FIFO history, statements, reconciliation and reporting
- [ ] deployment legal entity/chart/book/calendar/posting-profile/inventory/bank/reporting policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end reporting operating procedures
- [ ] AP/payment-proposal segregation-of-duties role review
- [ ] retention/export and organization-specific accounting/tax/valuation/reporting procedures
- [ ] keyboard/focus/Narrator/DPI acceptance including all Finance workspaces
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

- barcode scanning/generation and label template printing
- Finance package F7 until separately implemented and verified
- costing methods other than FIFO until explicitly implemented and accepted
- direct bank connectivity/payment initiation, EBICS, PSD2/open-banking certification and sanctions/AML decisioning
- jurisdiction-specific statutory filing/localization packages
- statutory certification of F6 report layouts; F6 is a configurable accounting reporting layer, not a jurisdiction-specific filing package
