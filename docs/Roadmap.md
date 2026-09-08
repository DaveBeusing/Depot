# Depot Roadmap

Updated: 2026-09-08

This roadmap describes product capabilities and acceptance work without coupling the repository to historical implementation tranche names.

## Implemented product capabilities

### Platform and operations

- [x] WPF/MVVM shell, navigation and contextual offline Help
- [x] inventory, warehouse, purchasing, sales and approval workflows
- [x] serial/lot traceability and reversal-safe stock evidence
- [x] database-backed RBAC and service-layer authorization
- [x] provider-neutral persistence for SQLite, SQL Server, MariaDB and MySQL
- [x] real production database-provider acceptance for the exact certified baselines
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

### Finance

- [x] legal entities and functional currencies
- [x] exchange rates
- [x] fiscal calendars and accounting periods
- [x] charts of accounts and accounts
- [x] accounting books and journals
- [x] accounting dimensions
- [x] tax registrations
- [x] Finance number sequences
- [x] immutable balanced journals and reporting-currency/FX evidence
- [x] posting profiles, period/account/dimension validation, idempotency and reversals
- [x] Accounts Receivable, payments/allocation/write-off, aging/statements and dunning
- [x] Accounts Payable, three-way matching, exception approval, settlement/reversal and aging/statements
- [x] FIFO inventory valuation, GRNI/COGS, adjustments, variance, landed cost and Inventory ↔ GL reconciliation
- [x] Banking with immutable statements, payment proposals/execution, reconciliation and cash position
- [x] Financial Reporting with Trial Balance, GL, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS
- [x] deterministic CSV export and immutable SHA-256-bound report snapshots
- [x] provider-neutral Finance Localization with explicit effective-dated assignments and `GENERIC → EU → DE` reference hierarchy
- [x] Finance workspaces and contextual Help

### Security and sessions

- [x] persistent user sessions and heartbeat-derived online presence
- [x] idle/maximum lifetime and concurrent-session policy
- [x] credential/deactivation session invalidation
- [x] shared database authentication throttling
- [x] persisted Security Events, Security Center and bounded retention maintenance

## Database provider production acceptance

The generic database-provider technical gate is complete for the exact baselines in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

- [x] bundled SQLite full acceptance baseline
- [x] SQL Server 2022 / engine 16.x migration/concurrency/recovery/performance matrix
- [x] MariaDB 11.8.9 LTS migration/concurrency/recovery/performance matrix
- [x] MySQL 8.4.11 LTS migration/concurrency/recovery/performance matrix
- [x] MariaDB/MySQL independent execution and support decisions
- [x] provider-native migration serialization
- [x] transaction rollback, lock/deadlock/write-conflict and bounded retry validation
- [x] SQL/type/constraint/date/decimal/timestamp compatibility validation
- [x] Sales and Procurement live business workflows
- [x] Finance GL/AR/AP/FIFO live business workflows
- [x] Banking import/reconciliation/reversal live business workflow
- [x] Financial Reporting/export/snapshot live business workflow
- [x] server restart and post-restart Depot re-entry
- [x] provider-native remote backup/restore and post-restore recognition boundary
- [x] representative 100,000-row indexed provider performance guard

This does not certify versions outside the support matrix and does not close deployment-specific accounting/legal/accessibility/signing gates.

## Production acceptance still required before 1.0

- [ ] accounting-book/chart/calendar/posting-profile/valuation/reporting policy approval
- [ ] AR/AP/inventory/bank reconciliation and period-end procedures
- [ ] segregation-of-duties review for posting, approval, payment and configuration roles
- [ ] jurisdiction-specific accounting/tax/localization acceptance
- [ ] retention/export/backup/restore operating procedures and ownership
- [ ] realistic customer-specific sizing including network latency, concurrent users and large reports/exports
- [ ] keyboard-only, screen-reader and DPI accessibility acceptance
- [ ] production Authenticode signing and installer/upgrade/rollback acceptance
- [ ] remaining electronic-invoice special-tax/channel scenarios
- [ ] qualified GDPR/CRA/legal/organizational review required for marketed deployment scenarios

## Demand-driven extensions

Pricing extensions should build on the current service boundaries rather than introduce parallel formulas. Planned extension points include controlled FX conversion for cost-to-price generation, additional explicit Base Cost source strategies, Target Gross Margin as a distinct pricing method, and commercial rounding strategies such as 0.05/0.10/0.50 or .99 endings.

The existing Finance architecture can host additional regional/country localization packs without a schema change when requirements are metadata/configuration only. Jurisdictions that require new executable workflows, statutory filing formats, additional costing methods, direct bank connectivity or other missing behavior require separately scoped implementation and qualified acceptance.

Provider versions outside the certified matrix are likewise demand-driven certification extensions and must not be inferred from a green baseline for a different version/product.
