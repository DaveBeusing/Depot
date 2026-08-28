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
- [x] customer open items, payments, overpayments and later allocation
- [x] payment/write-off reversal, aging, statements and dunning
- [x] Finance > Receivables workspace and granular RBAC
- [x] Finance schema **3**

### F3 — Accounts Payable — complete
- [x] supplier invoice/credit-note lifecycle and AP open items
- [x] AP → F1 GL posting/reversal
- [x] PO / goods-receipt / supplier-invoice matching and explicit exceptions
- [x] supplier payments, allocations, overpayments, reversal, aging and statements
- [x] Finance > Payables workspace and segregation-of-duties RBAC
- [x] Finance schema **4**

### F4 — Inventory Accounting — complete
- [x] provider-neutral FIFO valuation layers and consumption evidence
- [x] Goods Receipt → inventory/GRNI and Sales Shipment → FIFO/COGS posting
- [x] linked valuation reversals and inventory-count adjustments
- [x] purchase-price variance and landed-cost allocation/reversal
- [x] historical as-of valuation and inventory ↔ GL reconciliation snapshots
- [x] Finance > Inventory Accounting workspace
- [x] Finance schema **6**
- [x] Help manifest **1.13** / `finance.inventory-accounting`

### F5 — Banking and Payments — complete
- [x] bank account master/configuration tied to legal entity, accounting book, GL account and currency
- [x] immutable bank statements and statement lines
- [x] CSV statement import with deterministic normalization
- [x] ISO 20022 `camt.053` statement import
- [x] import operation/content idempotency and exact balance validation
- [x] payment proposals from supplier open items
- [x] creator/approver segregation and explicit payment-run approval
- [x] idempotent supplier-payment execution through the existing F3 AP service
- [x] bank-line reconciliation against AR payment, AP payment or configured-bank-account GL evidence
- [x] explicit reconciliation reversal preserving original evidence
- [x] cash-position comparison between latest statement balance and bank GL balance
- [x] Finance > Banking workspace and Banking/Payment RBAC
- [x] Finance schema **7** for SQLite, SQL Server and MySQL/MariaDB
- [x] Help manifest **1.14** / `finance.banking`
- [x] regression coverage for schema, CSV/camt.053 parsing, currency fail-closed behavior, RBAC and retention

### F6 — Financial Reporting — next
- [ ] trial balance and General Ledger report
- [ ] balance sheet and profit/loss
- [ ] cash-flow and subledger aging reports
- [ ] tax summary, inventory valuation and COGS
- [ ] dimension-aware reporting and exports
- [ ] retained report parameters/snapshots where required

### F7 — Localization Framework
- [ ] Generic reference localization
- [ ] EU layer and German reference implementation
- [ ] additional country packs based on demand
- [ ] effective-dated localization/compliance registry

## Remaining production/release acceptance

- [ ] live SQL Server Finance v1→v7 clean-install/migration/concurrency/recovery matrix
- [ ] live MySQL/MariaDB Finance v1→v7 clean-install/migration/concurrency/recovery matrix
- [ ] provider-specific AR/AP/inventory/banking locking and deadlock/retry acceptance
- [ ] representative Finance performance/load tests including FIFO history, statement imports and reconciliation
- [ ] deployment legal entity/chart/book/calendar/posting-profile/inventory/bank-account policy approval
- [ ] AR/AP/inventory/bank reconciliation operating procedures
- [ ] AP/payment-proposal segregation-of-duties role review
- [ ] external bank/payment-channel integration and recovery procedure where a deployment adds one
- [ ] retention/export and organization-specific accounting/tax/valuation/payment procedures
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

- barcode scanning/generation
- label template design/printing
- Finance packages F6-F7 until separately implemented and verified
- costing methods other than FIFO until explicitly implemented and accepted
- direct bank connectivity/payment initiation, EBICS, PSD2/open-banking certification, sanctions/AML decisioning until separately implemented
- jurisdiction-specific statutory filing/localization packages until explicitly implemented and accepted
