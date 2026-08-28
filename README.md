# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales, finance, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.15.x-preview** line and is not yet production-certified. Security/compliance roadmap phases 1-7 have their technically implementable repository/application controls in place; production, legal, provider, signing, accessibility, accounting-localization, and environment acceptance gates remain where documented.

## Highlights

- Inventory, warehouse, purchasing, sales, approvals, reporting, and administration workspaces
- dedicated **Finance > Receivables** workspace for customer open items, payments, allocations, aging, write-offs, and dunning
- enriched Item master data with GTIN, revision/model/product family, lifecycle, customs/export, RoHS/REACH, dangerous-goods/battery and explicit kg/mm logistics attributes
- serial-number and lot/batch traceability tied directly to the stock-movement ledger, including capture UI, balance/history browser, expiry/block controls and reversal-safe history
- SQLite plus SQL Server and MySQL/MariaDB provider implementations
- database-backed multi-role RBAC with service-layer authorization
- first-run administrator bootstrap with no shared production default password
- immutable/correction-oriented business-record workflows and structured audit evidence
- backup validation, restore, automatic backup retention, integrity checks, and SQLite compaction
- CycloneDX SBOM, NuGet vulnerability audit, dependency lock verification, CRA evidence generation, and release-integrity workflows
- immutable seller/buyer invoice identity with persisted XRechnung XML and SHA-256 integrity verification
- international Finance F0 foundation with legal entities, currencies/exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, dimensions, tax registrations, number sequences and localization boundaries
- Finance F1 General Ledger & Posting Engine with immutable balanced journals, posting profiles, transaction/reporting currencies, period enforcement, idempotency, explicit reversals and atomic audit persistence
- Finance F2 Accounts Receivable with Sales Invoice/Credit Note integration, receivable open items, partial/overpayment allocation, payment/write-off reversals, aging, statements, dunning, and granular RBAC

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware activity-bar navigation and closeable workspace tabs. Navigation supports stable routes, `Alt+Left` / `Alt+Right` history, `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, `Ctrl+W`, tab cycling, and F1 context Help.

Current primary workspaces are Dashboard, Inventory, Warehouse, Purchasing, Sales, Finance, Approvals, Reports, and Administration. Finance currently exposes the F2 **Receivables** page; the General Ledger remains an accounting service boundary rather than a free-form workspace.

## Finance

The current F2 completion baseline is **0.15.6-preview**. It completes **F2 — Accounts Receivable** on top of **F1 — General Ledger & Posting Engine** and **F0 — International Finance Foundation**.

The generic Finance core is jurisdiction-neutral. It has no implicit Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, bank account, write-off account, or statutory dunning default.

F0 provides legal entities, currencies/minor units, exchange rates, fiscal calendars/periods, charts/accounts, accounting books, journal definitions, accounting dimensions, tax registrations, Finance number sequences, and localization/tax/exchange-rate extension contracts.

F1 provides immutable double-entry journals, transaction/reporting currency plus FX snapshots, posting profiles, operation/source idempotency, open-period/date/legal-entity enforcement, account/dimension validation, transactional number allocation, explicit reversals, and atomic Audit Log persistence.

F2 provides:

- provider-neutral Finance feature schema **3** for SQLite, SQL Server, and MySQL/MariaDB;
- an explicit F2 dependency on the current Sales feature schema because the customer subledger consumes the existing Customer master and Sales Invoice/Credit Note sources;
- Finance > Receivables with open-item search and aging;
- debit invoice open items and credit-note/payment credit open items;
- Sales Invoice/Credit Note → AR → GL integration through configured F1 posting profiles;
- partial/full payment allocations and unapplied overpayments;
- later allocation of customer credit to another invoice;
- payment reversal that restores every active allocation made from the payment credit;
- controlled receivable write-offs and explicit GL-backed write-off reversal;
- customer statement rows;
- configurable dunning policies and retained idempotent dunning runs;
- granular Receivables, payment, write-off, and dunning permissions.

When an active AR configuration exists, Sales Invoice/Credit Note posting, AR open-item creation/allocation, F1 GL posting, Finance number allocation, and Audit Log evidence participate in one database transaction. If Finance validation or persistence fails, the source Sales posting rolls back. If AR has not been configured, Depot keeps the existing Sales workflow and does not invent accounting configuration.

The default Finance system role receives normal Receivables operations, customer payment post/reverse, and dunning rights. Sensitive write-off post/reverse rights remain separate. Free manual journals likewise continue to require the dedicated `FinanceManualJournals.Post` permission.

Finance uses the existing `DepotFeatureVersions` mechanism. Current schema levels are:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **3**

The next Finance package is **F3 — Accounts Payable**, covering supplier invoices/credit notes, supplier open items, PO/goods-receipt/invoice matching, approval, and controlled GL integration.

See `docs/FINANCE_ARCHITECTURE.md` and `docs/FINANCE_COMPLIANCE.md`.

## Business-record integrity

Finalized operational and accounting records are historical evidence. Corrections use explicit reversal, return, cancellation, close, credit-note, allocation, write-off-reversal, or other correction transactions instead of silently rewriting finalized history.

Finance journal entries, receivable open items, customer payments, write-offs, and dunning runs are classified as retained accounting/audit evidence according to their role. Posted GL entries remain immutable; payment/write-off corrections create linked GL reversals and controlled subledger corrections.

## Electronic invoicing

Depot includes an EN 16931-oriented semantic electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Sales-invoice posting freezes seller/buyer identity, persists the exact generated XML, and stores a SHA-256 fingerprint. Representative XML is validated in CI with pinned KoSIT/XRechnung assets.

Electronic invoicing remains separate from the jurisdiction-neutral Finance core. F2 consumes the finalized Sales document values but does not reinterpret XRechnung as a generic tax/accounting determination engine.

## Database providers

SQLite is the default provider. Microsoft SQL Server and MySQL/MariaDB implementations are also present. Supported remote-provider settings enforce encrypted transport. Live-server migration, recovery, concurrency, locking, performance, and supported-version acceptance remain required before a server configuration is advertised as production-supported.

Current schema levels:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **3**

## Offline Help Center

Depot ships an embedded Markdown Help Center rendered natively in WPF. It is permission-filtered, locally searchable, uses stable topic links, and opens as a workspace tab.

Help manifest **1.11** contains Finance Foundation, **General Ledger and Posting**, and **Accounts Receivable** topics. The F2 Help covers configuration, Sales→AR→GL atomicity, open items, payments/allocations/overpayments, reversals, write-offs, aging, statements, dunning, permissions, and the F3 boundary.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Services own business/accounting invariants, permissions, state transitions, and transaction orchestration. Repositories own persistence SQL and row mapping. Provider-specific behavior stays behind the established data-access abstractions.

`FinanceGeneralLedgerService` remains the authoritative accounting posting boundary. `FinanceAccountsReceivableService` owns the customer-subledger invariants and invokes the GL boundary within the same transaction for F2 financial mutations.

## Getting started

Requirements: Windows 10/11 and the .NET 10 SDK.

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
dotnet restore Depot.slnx --locked-mode
dotnet run --project src/Depot/Depot.csproj -c Debug
```

A new installation defaults to local SQLite and creates `depot.db`; protected settings are stored in `depot.settings`. If the selected database has no usable application user, Depot requires creation of the initial administrator with an individual login and policy-compliant password.

## Build and publish

```powershell
dotnet build Depot.slnx -c Debug
dotnet build Depot.slnx -c Release -warnaserror
```

Self-contained single-file publish:

```powershell
dotnet restore src/Depot/Depot.csproj -r win-x64
dotnet publish src/Depot/Depot.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false
```

Runtime data remains external. Do not enable `PublishTrimmed` without dedicated WPF/XAML trimming validation.

## CI and assurance

The repository includes bounded regression CI, security supply-chain checks, dependency locks, CycloneDX SBOM/license evidence, release-integrity checks, electronic-invoice conformance, zero-warning Release builds, regression suites, performance baselines, and static accessibility checks.

Finance F2 regression coverage includes schema migration, Sales source idempotency, balanced GL linkage, overpayment/later allocation, payment reversal across all active allocations, write-off authorization/reversal, aging, dunning idempotency, RBAC, and retained-record classification.

Acceptance distinguishes Finance-introduced failures from pre-existing repository test failures. Live SQL Server/MySQL/MariaDB Finance v3 migration/concurrency acceptance remains a production gate.

Production Authenticode signing requires the real protected signing identity and remains a release acceptance gate.

## Remaining work before 1.0

Major remaining items include:

- live SQL Server/MySQL/MariaDB migration, recovery, concurrency, performance, and supported-version matrices, including Finance v3;
- Windows ACL-denied recovery testing;
- production code-signing certificate and timestamp validation;
- interactive keyboard/focus, Narrator/Accessibility Insights, and DPI acceptance;
- representative production sizing/load tests;
- explicit EN 16931 tax-category/exemption semantics for zero-rated, exempt, and reverse-charge invoice scenarios;
- buyer/XRechnung finalization for electronic credit notes;
- production recipient/channel routing and full KoSIT/XRechnung scenario validation;
- PDF/A-3 implementation before any ZUGFeRD/Factur-X claim;
- operator/legal acceptance for GDPR, GoBD, CRA classification/conformity, retention periods, and organization-specific procedures;
- installer/package, upgrade, rollback, and uninstall acceptance;
- Finance F3 Accounts Payable, followed by inventory accounting, banking, financial reporting, and localization packages.

Barcode scanning/generation and label design/printing remain outside current scope. Finance functionality beyond F2 is tracked in `docs/Roadmap.md` rather than claimed as implemented.

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
