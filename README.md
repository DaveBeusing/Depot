# Depot

**Modern business operations, without the ERP complexity.**

Depot is a modern Windows business platform that brings **Sales, Purchasing, Inventory, Warehouse, Finance and Operations** together in one consistent system. It is designed for growing organizations that want to replace fragmented tools, spreadsheets and disconnected workflows with a controlled operational foundation.

> **One platform. One source of truth. Complete control.**

Depot is currently in active **0.15.x-preview** development. The repository already contains substantial operational, accounting, security, audit and compliance capabilities, but preview status matters: implemented engineering controls are not a claim of production, legal, tax, accessibility, provider or organization-specific certification. See [Current Status](docs/CurrentStatus.md) and the [Roadmap](docs/Roadmap.md) for the current product boundary.

## Why Depot

Business software should make operations easier to understand, not add another layer of complexity. Depot is built around four principles:

- **Simple** — one coherent desktop workspace with consistent navigation, contextual Help and predictable workflows.
- **Connected** — Sales, Purchasing, Inventory, Warehouse and Finance share the same operational data and business rules instead of becoming separate islands.
- **Controlled** — permissions, approvals, audit evidence, immutable business records and accounting controls are part of the architecture rather than afterthoughts.
- **Adaptable** — provider-neutral persistence and jurisdiction-aware Finance foundations are designed to support different infrastructures, organizations and markets.

**Built for businesses, not borders.** Depot separates core business capabilities from jurisdiction-specific configuration so the product can evolve internationally without hard-wiring one national operating model into the platform.

## What Depot brings together

- **Sales & Pricing** — scoped per-item pricing with Customer → Region → Global fallback, retained document-price sources and controlled pricing workflows.
- **Purchasing** — supplier and purchasing workflows connected to inventory, approvals and financial consequences.
- **Inventory & Warehouse** — enriched item master data, serial/lot traceability, reversal-safe movement history and inventory accounting.
- **Finance** — legal entities, currencies, periods, General Ledger, Accounts Receivable, Accounts Payable, inventory accounting, banking, reconciliation and financial reporting.
- **Operations & Control** — approvals, reporting, administration, RBAC, audit evidence and retained business records.
- **Integrated Help** — contextual offline Help follows central permissions and supports users directly inside the application.

## Product foundation

Depot is a native Windows desktop application built with **.NET 10, WPF and MVVM**. Its persistence architecture is provider-neutral and currently supports **SQLite, SQL Server and MySQL/MariaDB** implementations.

The product foundation includes:

- dark permission-aware workspace shell with Quick Open and Command Palette
- database-backed RBAC with service-layer authorization
- immutable/correction-oriented retained business records and structured audit evidence
- immutable seller/buyer invoice identity with persisted XRechnung XML and SHA-256 verification
- jurisdiction-neutral Finance foundation with legal entities, currencies/FX, periods, charts, books, dimensions, tax registrations and number sequences
- immutable General Ledger and posting profiles
- Accounts Receivable and Accounts Payable subledgers
- FIFO Inventory Accounting with GRNI, COGS, variances, landed cost and Inventory-to-GL reconciliation
- Banking and Payments with statement import, payment runs, reconciliation and cash position
- Financial Reporting with mappings, deterministic CSV and immutable report snapshots
- effective-dated Finance Localization with built-in `GENERIC → EU → DE` references and extensible custom packs

For the architectural model, see [Architecture](docs/Architecture.md).

## Finance

Depot Finance uses one authoritative accounting chain. Operational modules and subledgers create controlled financial consequences through `FinanceGeneralLedgerService`; Financial Reporting reads persisted accounting evidence and never maintains a second ledger. Localization describes effective jurisdiction/configuration boundaries and never posts accounting entries.

Finance currently provides:

- **Finance Foundation** — legal entities, currencies/exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger & Posting** — immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, idempotency, number allocation, audit evidence and linked reversals.
- **Accounts Receivable** — customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales → AR → GL integration.
- **Accounts Payable** — supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and AP → GL integration.
- **Inventory Accounting** — FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments** — bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **Financial Reporting** — Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension filtering, explicit mappings, deterministic CSV and immutable SHA-256-bound snapshots.
- **Finance Localization** — explicit effective-dated legal-entity assignments, hierarchical localization packs and a capability/configuration/procedure registry. `LegalEntity.CountryCode` validates country packs but never activates them automatically.

Localization support levels (`SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired`, `ReferenceOnly`) describe responsibility and capability boundaries, not legal/compliance pass/fail status. Depot does not invent tax rates, statutory charts, filing classifications or accounting-policy choices.

Explore [Finance Architecture](docs/FinanceArchitecture.md), [Finance Banking](docs/FinanceBanking.md), [Finance Localization](docs/FinanceLocalization.md), [Finance Reporting](docs/FinanceReporting.md) and [Finance Compliance](docs/FinanceCompliance.md).

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain presentation only. ViewModels own UI state, commands, cancellation and stale-request protection. Services own permissions, business/accounting invariants, state transitions and transaction orchestration. Repositories own SQL and row mapping. Provider-specific behavior stays behind data-access/provider abstractions.

Read the full [Architecture](docs/Architecture.md) and [Coding Standard](docs/CodingStandard.md).

## Current engineering status

Current schema levels:

- core database schema: **30**
- Sales feature schema: **9**
- Finance feature schema: **9**
- Help manifest: **1.18**

For implementation and release status, see [Current Status](docs/CurrentStatus.md), [Documentation Status](docs/DocumentationStatus.md), [User-Facing Changes](docs/UserFacingChanges.md) and [Release 1.0](docs/Release1.0.md).

## Build and publish

```powershell
dotnet restore Depot.slnx --locked-mode
dotnet build Depot.slnx -c Release -warnaserror

dotnet restore src/Depot/Depot.csproj -r win-x64
dotnet publish src/Depot/Depot.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false
```

Runtime data remains external. Do not enable WPF trimming without dedicated validation.

## CI, security and assurance

CI includes Release build/publish, bounded regression suites, software-quality/accessibility checks, dependency locks, NuGet vulnerability audit, SBOM/evidence generation, release-integrity checks and electronic-invoice conformance. Test matrices have sufficient job budgets for the current repository breadth while individual hangs remain bounded.

Provider-neutral Finance schema 9 exists for SQLite, SQL Server and MySQL/MariaDB. Live server migration, locking, deadlock/retry, backup/recovery, concurrency and representative Finance/localization acceptance remain production gates.

Security and compliance work is documented transparently rather than presented as certification that has not yet been achieved. Start with the [Security Roadmap](docs/SecurityRoadmap.md), [Compliance Overview](docs/ComplianceOverview.md) and [Compliance Matrix](docs/compliance/ComplianceMatrix.md).

## Offline Help

Embedded Help manifest **1.18** contains scoped Sales pricing guidance plus Finance Foundation, General Ledger, Accounts Receivable, Accounts Payable, Inventory Accounting, Banking, Financial Reporting and Finance Localization topics. Help visibility follows central permissions and never grants business access.

See the [Help Center documentation](docs/HelpCenter.md).

## Road to 1.0

Major remaining items include live remote-provider acceptance, production code signing, accessibility/manual desktop acceptance, organization-specific accounting/tax/retention/valuation/reporting/localization procedures, remaining electronic-invoice scenarios and installer/upgrade acceptance. Additional jurisdiction packs are demand-driven extensions of the localization framework.

Track the path through the [Roadmap](docs/Roadmap.md) and [Release 1.0 plan](docs/Release1.0.md).

## Documentation

### Product and engineering

- [Architecture](docs/Architecture.md)
- [Coding Standard](docs/CodingStandard.md)
- [Current Status](docs/CurrentStatus.md)
- [Documentation Status](docs/DocumentationStatus.md)
- [User-Facing Changes](docs/UserFacingChanges.md)
- [Help Center](docs/HelpCenter.md)
- [Roadmap](docs/Roadmap.md)
- [Release 1.0](docs/Release1.0.md)

### Sales, items and pricing

- [Sales Pricing](docs/SalesPricing.md)
- [Item Master Data](docs/ItemMasterData.md)
- [Item Traceability](docs/ItemTraceability.md)
- [Item Costing and Bulk Pricing](docs/ItemCostingAndBulkPricing.md)
- [Reference Data Defaults](docs/ReferenceDataDefaults.md)

### Finance

- [Finance Architecture](docs/FinanceArchitecture.md)
- [Finance Banking](docs/FinanceBanking.md)
- [Finance Localization](docs/FinanceLocalization.md)
- [Finance Reporting](docs/FinanceReporting.md)
- [Finance Compliance](docs/FinanceCompliance.md)

### Security and compliance

- [Compliance Overview](docs/ComplianceOverview.md)
- [Security Roadmap](docs/SecurityRoadmap.md)
- [Compliance Matrix](docs/compliance/ComplianceMatrix.md)
- [Security](docs/compliance/Security.md)
- [Threat Model](docs/compliance/ThreatModel.md)
- [Vulnerability Management](docs/compliance/VulnerabilityManagement.md)
- [Data Protection](docs/compliance/DataProtection.md)
- [Business Record Integrity](docs/compliance/BusinessRecordIntegrity.md)
- [Release Integrity](docs/compliance/ReleaseIntegrity.md)
- [Software Quality](docs/compliance/SoftwareQuality.md)
- [Full compliance documentation](docs/compliance/)

## License

Depot is licensed under the [MIT License](LICENSE.md).
