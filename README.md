# Depot

Depot is a Windows desktop application for inventory, warehouse, purchasing, sales, finance, approvals, reporting, administration, and operational workflows. It uses .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The active development line is **0.15.x-preview**. Security, compliance, accounting, localization and audit controls implemented in the repository are engineering capabilities and evidence; production, provider, legal, accessibility, signing and organization-specific acceptance remain where documented.

## Highlights

- dark permission-aware workspace shell with Quick Open, Command Palette and contextual offline Help
- inventory, warehouse, purchasing, sales, approvals, reporting and administration workflows
- enriched Item master data plus serial/lot traceability and reversal-safe movement history
- SQLite, SQL Server and MySQL/MariaDB provider implementations
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

## Finance

Depot Finance uses one authoritative accounting chain. Operational modules and subledgers create controlled financial consequences through `FinanceGeneralLedgerService`; Financial Reporting reads persisted accounting evidence and never maintains a second ledger. Localization describes effective jurisdiction/configuration boundaries and never posts accounting entries.

Finance currently provides:

- **Finance Foundation** — legal entities, currencies/exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations and number sequences.
- **General Ledger & Posting** — immutable balanced journals, reporting-currency snapshots, posting profiles, period/account/dimension validation, idempotency, number allocation, Audit evidence and linked reversals.
- **Accounts Receivable** — customer open items, payments/allocations, write-offs, aging/statements, dunning and Sales → AR → GL integration.
- **Accounts Payable** — supplier documents/open items, three-way matching, exception approval, payments/allocations/reversals, aging/statements and AP → GL integration.
- **Inventory Accounting** — FIFO valuation, GRNI/COGS, inventory adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.
- **Banking and Payments** — bank accounts, immutable CSV/camt.053 statements, payment proposals/execution, AR/AP/GL reconciliation and cash position.
- **Financial Reporting** — Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension filtering, explicit mappings, deterministic CSV and immutable SHA-256-bound snapshots.
- **Finance Localization** — explicit effective-dated legal-entity assignments, hierarchical localization packs and a capability/configuration/procedure registry. `LegalEntity.CountryCode` validates country packs but never activates them automatically.

Localization support levels (`SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired`, `ReferenceOnly`) describe responsibility and capability boundaries, not legal/compliance pass/fail status. Depot does not invent tax rates, statutory charts, filing classifications or accounting-policy choices.

Current schema levels:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Help manifest: **1.17**

See `docs/FINANCE_ARCHITECTURE.md`, `docs/FINANCE_LOCALIZATION.md`, `docs/FINANCE_REPORTING.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/Roadmap.md`.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain presentation only. ViewModels own UI state, commands, cancellation and stale-request protection. Services own permissions, business/accounting invariants, state transitions and transaction orchestration. Repositories own SQL and row mapping. Provider-specific behavior stays behind data-access/provider abstractions.

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

## CI and assurance

CI includes Release build/publish, bounded regression suites, software-quality/accessibility checks, dependency locks, NuGet vulnerability audit, SBOM/evidence generation, release-integrity checks and electronic-invoice conformance. Test matrices have sufficient job budgets for the current repository breadth while individual hangs remain bounded.

Provider-neutral Finance schema 9 exists for SQLite, SQL Server and MySQL/MariaDB. Live server migration, locking, deadlock/retry, backup/recovery, concurrency and representative Finance/localization acceptance remain production gates.

## Offline Help

Embedded Help manifest **1.17** contains Finance Foundation, General Ledger, Accounts Receivable, Accounts Payable, Inventory Accounting, Banking, Financial Reporting and Finance Localization topics. Help visibility follows central permissions and never grants business access.

## Remaining work before 1.0

Major remaining items include live remote-provider acceptance, production code signing, accessibility/manual desktop acceptance, organization-specific accounting/tax/retention/valuation/reporting/localization procedures, remaining electronic-invoice scenarios and installer/upgrade acceptance. Additional jurisdiction packs are demand-driven extensions of the localization framework.

## Documentation

- `docs/Architecture.md`
- `docs/CURRENT_STATUS.md`
- `docs/FINANCE_ARCHITECTURE.md`
- `docs/FINANCE_LOCALIZATION.md`
- `docs/FINANCE_REPORTING.md`
- `docs/FINANCE_COMPLIANCE.md`
- `docs/DOCUMENTATION_STATUS.md`
- `docs/USER_FACING_CHANGES.md`
- `docs/HELP_CENTER.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/compliance/`

## License

Depot is licensed under the MIT License.
