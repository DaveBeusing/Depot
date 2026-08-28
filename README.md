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

## Finance

The completed Finance baseline now covers **F0 through F4**.

F0 provides legal entities, currencies/exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journals, dimensions, tax registrations, number sequences and localization/tax/exchange-rate extension boundaries.

F1 provides immutable balanced journal entries, posting profiles, transaction/reporting currency and FX snapshots, period/account/dimension validation, operation/source idempotency, transactional Finance number allocation, explicit linked reversals and atomic Audit Log evidence.

F2 provides the customer subledger: Sales Invoice/Credit Note → AR → GL integration, customer open items, payments, partial/full allocations, overpayments, payment/write-off reversals, aging, customer statements and dunning.

F3 provides the supplier subledger: supplier invoices and credit notes, draft/submission/approval/posting/reversal lifecycle, AP open items, supplier payments and allocations, overpayments, payment reversal, aging, supplier statements, and fail-closed PO/goods-receipt/invoice matching. Match exceptions require separate authority and retained reason.

F4 provides Inventory Accounting: FIFO valuation layers, Goods Receipt inventory/GRNI posting, Sales Shipment FIFO consumption/COGS, reversal-safe valuation evidence, inventory-count adjustments, purchase-price variance, landed-cost allocation/reversal, historical as-of valuation and immutable period-end inventory-to-GL reconciliation snapshots.

All accounting entries continue to flow through `FinanceGeneralLedgerService`; AR/AP/Inventory Accounting do not maintain a second ledger. Generic Finance does not infer a jurisdiction, currency, tax rate, chart, accounting standard, bank/AP/expense/inventory account, statutory workflow, matching tolerance, capitalization policy or alternative costing method.

Current schema levels:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **6**
- Help manifest: **1.13**

The next Finance package is **F5 — Banking and Payments**.

See `docs/FINANCE_ARCHITECTURE.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/Roadmap.md`.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain presentation only. ViewModels own UI state, commands, cancellation and stale-request protection. Services own permissions, business/accounting invariants, state transitions and transaction orchestration. Repositories own SQL and row mapping. Provider-specific behavior remains behind the data-access/provider abstractions.

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

F4 regression coverage includes Finance schema 6, Inventory Accounting RBAC, retained F4 accounting/audit records and historical as-of valuation behavior. F3 AP, F2 AR and F1 GL tests remain part of the broad regression baseline.

Provider-neutral Finance v6 code exists for SQLite, SQL Server and MySQL/MariaDB. Live server migration, locking, deadlock/retry, backup/recovery, concurrency and representative FIFO/reconciliation performance acceptance remain production gates.

## Offline Help

Embedded Help manifest **1.13** contains Finance Foundation, General Ledger, Accounts Receivable, Accounts Payable and **Inventory Accounting** (`finance.inventory-accounting`) topics. Help visibility follows central permissions and never grants business access.

## Remaining work before 1.0

Major remaining items include live remote-provider acceptance, production code signing, accessibility/manual desktop acceptance, organization-specific accounting/tax/retention/valuation procedures, remaining electronic-invoice scenarios, installer/upgrade acceptance, F5 Banking and Payments, F6 Financial Reporting and F7 Localization Framework.

## Documentation

- `docs/Architecture.md`
- `docs/CURRENT_STATUS.md`
- `docs/FINANCE_ARCHITECTURE.md`
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
