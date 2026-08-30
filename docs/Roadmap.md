# Depot Roadmap

Updated: 2026-08-29

This roadmap describes product capabilities and acceptance work without coupling the repository to historical implementation tranche names.

## Implemented product capabilities

### Platform and operations

- [x] WPF/MVVM shell, navigation and contextual offline Help
- [x] inventory, warehouse, purchasing, sales and approval workflows
- [x] serial/lot traceability and reversal-safe stock evidence
- [x] database-backed RBAC and service-layer authorization
- [x] provider-neutral persistence for SQLite, SQL Server and MySQL/MariaDB
- [x] structured Audit evidence and correction-oriented retained business records
- [x] company/document identity controls and persisted XRechnung evidence

### Costing and sales pricing

- [x] scoped Global, Region and Customer PriceLists with Customer → Region → Global item resolution
- [x] preferred-supplier Purchase Price as explicit first Base Cost source
- [x] explicit Item Cost currency with fail-closed cross-currency handling
- [x] Absolute and Percentage Cost Components
- [x] Percentage bases `BaseCost` and `RunningTotal`
- [x] deterministic Sequence + persisted identity calculation order
- [x] effective-dated and active/inactive Cost Components
- [x] central `ItemCostCalculationService` with calculation evidence
- [x] Percentage Markup bulk generation without conflating Markup and Gross Margin
- [x] All Active, Category, Manufacturer and Selected Item bulk filters
- [x] mandatory Preview with Create/Update/Skip/Error decisions and calculation evidence
- [x] Replace, Only Increase and Only Missing Apply modes
- [x] atomic Bulk Apply with optimistic concurrency, Audit and service-layer RBAC
- [x] historical Sales-document price snapshots remain immutable

### Finance Foundation

- [x] legal entities and functional currencies
- [x] exchange rates
- [x] fiscal calendars and accounting periods
- [x] charts of accounts and accounts
- [x] accounting books and journals
- [x] accounting dimensions
- [x] tax registrations
- [x] Finance number sequences

### General Ledger and posting

- [x] immutable balanced journals
- [x] transaction/reporting currency and posting-time FX evidence
- [x] posting profiles
- [x] period/account/dimension validation
- [x] idempotent source/operation posting
- [x] transactional number allocation
- [x] linked reversals and Audit evidence

### Accounts Receivable

- [x] Sales Invoice/Credit Note → AR → GL
- [x] customer open items
- [x] payments and allocations
- [x] write-offs and reversals
- [x] aging, statements and dunning

### Accounts Payable

- [x] supplier invoices and credit notes
- [x] supplier open items
- [x] three-way matching and controlled exceptions
- [x] payments, allocations and reversals
- [x] aging and supplier statements
- [x] segregation-of-duties controls

### Inventory Accounting

- [x] FIFO valuation layers and consumptions
- [x] Goods Receipt inventory/GRNI posting
- [x] shipment COGS posting
- [x] inventory adjustments
- [x] purchase-price variance
- [x] landed-cost allocation and reversal controls
- [x] historical as-of valuation
- [x] Inventory ↔ GL reconciliation

### Banking and Payments

- [x] bank-account configuration
- [x] immutable CSV and ISO 20022 camt.053 statement import
- [x] payment proposals and controlled execution
- [x] AR/AP/GL reconciliation and reversal evidence
- [x] cash position

### Financial Reporting

- [x] Trial Balance and GL detail
- [x] Balance Sheet and Profit & Loss
- [x] Cash Flow
- [x] AR/AP Aging
- [x] Tax Summary
- [x] historical Inventory Valuation and COGS
- [x] accounting-dimension filters
- [x] explicit report classification mappings
- [x] deterministic CSV export
- [x] immutable SHA-256-bound report snapshots

### Finance Localization

- [x] provider-neutral localization packs and legal-entity assignments
- [x] explicit effective-dated activation
- [x] built-in `GENERIC → EU → DE` reference hierarchy
- [x] country validation and overlap prevention
- [x] inherited effective profile resolution
- [x] capability/configuration/procedure/reference support levels
- [x] immutable built-in definitions and extensible custom packs
- [x] optimistic concurrency, Audit evidence and RBAC
- [x] **Finance > Localization** workspace and contextual Help

## Production acceptance before 1.0

- [ ] live SQL Server migration/concurrency/recovery/performance matrix
- [ ] live MySQL/MariaDB migration/concurrency/recovery/performance matrix
- [ ] representative Finance and bulk-pricing load/deadlock/retry testing
- [ ] accounting-book/chart/calendar/posting-profile/valuation/reporting policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end procedures
- [ ] segregation-of-duties review for posting, approval, payment and configuration roles
- [ ] jurisdiction-specific accounting/tax/localization acceptance
- [ ] retention/export/backup/restore procedures
- [ ] keyboard-only, screen-reader and DPI accessibility acceptance
- [ ] production Authenticode signing and installer/upgrade/rollback acceptance
- [ ] remaining electronic-invoice special-tax/channel scenarios

## Demand-driven extensions

Pricing extensions should build on the current service boundaries rather than introduce parallel formulas. Planned extension points include controlled FX conversion for cost-to-price generation, additional explicit Base Cost source strategies, Target Gross Margin as a distinct pricing method, and commercial rounding strategies such as 0.05/0.10/0.50 or .99 endings.

The existing Finance architecture can host additional regional/country localization packs without a schema change when requirements are metadata/configuration only. Jurisdictions that require new executable workflows, statutory filing formats, additional costing methods, direct bank connectivity or other missing behavior require separately scoped implementation and qualified acceptance.
