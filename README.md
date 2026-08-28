# Depot

Depot is a Windows desktop application for inventory, warehouse, purchasing, sales, finance, approvals, reporting, administration, and operational workflows. It uses .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The active development line is **0.15.x-preview**. Security/compliance controls implemented in the repository are engineering evidence only; production, provider, legal, accessibility, signing, localization/accounting-policy, and organization-specific acceptance gates remain where documented.

## Highlights

- dark permission-aware workspace shell with closeable workspaces, Quick Open, Command Palette and context Help
- inventory, warehouse, purchasing, sales, approvals, reporting and administration workflows
- enriched Item master data plus serial/lot traceability and reversal-safe movement history
- SQLite, SQL Server and MySQL/MariaDB provider implementations
- database-backed RBAC with service-layer authorization
- immutable/correction-oriented retained business records and structured audit evidence
- immutable seller/buyer invoice identity with persisted XRechnung XML and SHA-256 verification
- Finance F0 International Finance Foundation
- Finance F1 immutable General Ledger & Posting Engine
- Finance F2 Accounts Receivable with **Finance > Receivables**
- Finance F3 Accounts Payable with **Finance > Payables**
- Finance F4 FIFO Inventory Accounting with **Finance > Inventory Accounting**
- Finance F5 Banking and Payments with **Finance > Banking**
- Finance F6 Financial Reporting with **Finance > Financial Reporting**
- Finance F7 Localization Framework with **Finance > Localization**

## Finance

The completed Finance baseline covers **F0 through F7**.

F0 provides legal entities, currencies/exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations, number sequences and localization/tax/exchange-rate extension boundaries.

F1 provides immutable balanced journal entries, posting profiles, transaction/reporting currency and FX snapshots, period/account/dimension validation, operation/source idempotency, transactional Finance number allocation, explicit linked reversals and atomic Audit evidence.

F2 provides the customer subledger: Sales Invoice/Credit Note → AR → GL integration, customer open items, payments/allocations, overpayments, write-offs, aging, statements and dunning.

F3 provides the supplier subledger: supplier invoices/credit notes, approval/posting/reversal lifecycle, AP open items, payments/allocations, aging/statements and fail-closed PO/goods-receipt/invoice matching with explicit exception authority.

F4 provides FIFO Inventory Accounting: Goods Receipt inventory/GRNI, Sales Shipment COGS, reversals, inventory-count adjustments, purchase-price variance, landed cost, historical valuation and Inventory ↔ GL reconciliation.

F5 provides Banking and Payments: bank accounts, immutable CSV/camt.053 statements, supplier payment proposals/execution, AR/AP/GL reconciliation, reconciliation reversal and cash position.

F6 provides Financial Reporting: Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation, COGS, dimension-aware GL reporting, deterministic CSV export, explicit account mappings and immutable SHA-256-bound report snapshots.

F7 provides an explicit effective-dated localization framework. Built-in reference packs resolve as `GENERIC → EU → DE`; legal-entity country never activates a pack automatically. Finance users can assign a root pack, add custom regional/country packs without schema changes, and maintain an effective-dated capability/compliance registry. Registry support levels distinguish software capability, required deployment configuration, external procedures and reference-only guidance. F7 does **not** claim legal/tax/accounting compliance or invent tax rates, charts of accounts, filing classifications or statutory accounting decisions.

F1 remains the sole General Ledger authority. F6 reads persisted accounting evidence rather than maintaining a second ledger. F7 describes effective localization/configuration boundaries and does not post accounting entries.

Current schema levels:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **9**
- Help manifest: **1.16**

See `docs/FINANCE_ARCHITECTURE.md`, `docs/FINANCE_LOCALIZATION.md`, `docs/FINANCE_REPORTING.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/Roadmap.md`.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain presentation only. ViewModels own UI state, commands, cancellation and stale-request protection. Services own permissions, business/accounting invariants, state transitions and transaction orchestration. Repositories own SQL and row mapping. Provider-specific behavior remains behind data-access/provider abstractions.

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

CI includes Release build/publish, bounded regression suites, software-quality/accessibility checks, dependency locks, NuGet vulnerability audit, SBOM/evidence generation, release-integrity checks and electronic-invoice conformance.

F7 regression coverage verifies Finance schema 9, built-in reference packs, explicit assignment semantics, `GENERIC → EU → DE` resolution, country mismatch rejection, overlapping-assignment rejection, built-in immutability, custom country-pack extensibility without another schema change, Finance RBAC and retained localization evidence. Earlier F1-F6 suites remain part of the broad baseline.

Provider-neutral Finance v9 code exists for SQLite, SQL Server and MySQL/MariaDB. Live server migration, locking, deadlock/retry, backup/recovery, concurrency and representative Finance/localization acceptance remain production gates.

## Offline Help

Embedded Help manifest **1.16** contains Finance Foundation, General Ledger, Accounts Receivable, Accounts Payable, Inventory Accounting, Banking, Financial Reporting and **Finance Localization** (`finance.localization`) topics. Help visibility follows central permissions and never grants business access.

## Remaining work before 1.0

Major remaining items include live remote-provider acceptance, production code signing, accessibility/manual desktop acceptance, organization-specific accounting/tax/retention/valuation/reporting/localization procedures, remaining electronic-invoice scenarios and installer/upgrade acceptance. Additional jurisdiction packs are demand-driven extensions of F7 rather than a prerequisite for the generic F7 framework.

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
