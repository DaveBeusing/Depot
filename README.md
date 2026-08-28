# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales, finance, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.15.x-preview** line and is not yet production-certified. Security/compliance roadmap phases 1-7 have their technically implementable repository/application controls in place; production, legal, provider, signing, accessibility, accounting-localization, and environment acceptance gates remain where documented.

## Highlights

- Inventory, warehouse, purchasing, sales, approvals, reporting, and administration workspaces
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

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware activity-bar navigation and closeable workspace tabs. Navigation supports stable routes, `Alt+Left` / `Alt+Right` history, `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, `Ctrl+W`, tab cycling, and F1 context Help.

Current primary workspaces are Dashboard, Inventory, Warehouse, Purchasing, Sales, Approvals, Reports, and Administration. Finance F1 is currently service/repository-first; a dedicated Finance workspace is intentionally deferred until source integrations and user workflows can be exposed without partial accounting behavior.

## Finance

The current documentation baseline is **0.15.2-preview**. It documents the completed **F1 — General Ledger & Posting Engine** on top of the **F0 — International Finance Foundation**.

The generic Finance core is jurisdiction-neutral. It has no implicit Germany, EUR, 19%, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, or other local default.

F0 provides:

- legal entities with explicit functional currency;
- currencies with explicit minor units;
- sourced/effective exchange rates;
- fiscal calendars and accounting periods;
- charts of accounts and accounts;
- accounting books and journal definitions;
- accounting dimensions and values;
- structured tax registrations;
- Finance number sequences;
- exchange-rate, tax-determination, and localization extension contracts.

F1 provides:

- immutable General Ledger journal entries and lines;
- double-entry validation in transaction and reporting currency;
- transaction/reporting currency plus persisted exchange-rate snapshot;
- posting profiles that map named amount keys to configured debit/credit accounts;
- operation and source-document idempotency;
- open-period/date/legal-entity enforcement;
- active-account/chart/direct-posting validation;
- required accounting dimensions;
- Finance number-sequence allocation inside the posting transaction;
- explicit linked reversal entries instead of destructive corrections;
- atomic Audit Log persistence with rollback of the full accounting transaction if audit persistence fails;
- optimistic posting-profile concurrency and database uniqueness boundaries.

Free manual journals require the dedicated `FinanceManualJournals.Post` permission in addition to normal General Ledger posting permission. The default Finance system role receives controlled General Ledger posting/reversal and posting-profile permissions but does **not** receive the sensitive manual-journal permission automatically.

Finance uses the existing `DepotFeatureVersions` mechanism. **Finance feature schema 2** is available for SQLite, SQL Server, and MySQL/MariaDB. The core database schema remains **29** and Sales feature schema remains **8**; application SemVer and database/feature schema versions are independent.

F1 deliberately does not force Sales, Purchasing, or Inventory to create accounting entries until their complete source-integration/subledger package exists. The next package is **F2 — Accounts Receivable**, covering Sales Invoice/Credit Note ledger integration, receivable open items, payment allocations, write-offs, dunning, and aging.

See `docs/FINANCE_ARCHITECTURE.md` and `docs/FINANCE_COMPLIANCE.md`.

## Business-record integrity

Finalized operational and accounting records are historical evidence. Corrections use explicit reversal, return, cancellation, close, credit-note, or other correction transactions instead of silently rewriting finalized history.

Finance journal entries are retained accounting records. Once posted, an entry is not updated or deleted by the F1 workflow. A correction creates a new linked reversal entry while preserving the original entry and its currency/rate/source snapshots.

## Electronic invoicing

Depot includes an EN 16931-oriented semantic electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Sales-invoice posting freezes seller/buyer identity, persists the exact generated XML, and stores a SHA-256 fingerprint. Representative XML is validated in CI with pinned KoSIT/XRechnung assets.

Electronic invoicing remains separate from the jurisdiction-neutral Finance core. F1 does not reinterpret XRechnung or the existing Sales tax model as a generic accounting/tax engine.

## Database providers

SQLite is the default provider. Microsoft SQL Server and MySQL/MariaDB implementations are also present. Supported remote-provider settings enforce encrypted transport. Live-server migration, recovery, concurrency, locking, performance, and supported-version acceptance remain required before a server configuration is advertised as production-supported.

Current schema levels:

- core database schema: **29**
- Sales feature schema: **8**
- Finance feature schema: **2**

## Offline Help Center

Depot ships an embedded Markdown Help Center rendered natively in WPF. It is permission-filtered, locally searchable, uses stable topic links, and opens as a workspace tab.

Help manifest **1.10** contains the Finance Foundation and **General Ledger and Posting** topics. The F1 Help explains posting invariants, currency/FX handling, idempotency, posting profiles, manual-journal authorization, reversals, atomic audit behavior, and the boundary to later AR/AP/inventory-accounting/banking packages.

The manifest remains at 1.10 in this documentation refresh because no topic ID, permission contract, or content-file mapping changed.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Services own business/accounting invariants, permissions, state transitions, and transaction orchestration. Repositories own persistence SQL and row mapping. Provider-specific behavior stays behind the established data-access abstractions.

`FinanceGeneralLedgerService` is the authoritative F1 posting boundary. Future AR/AP/inventory/banking workflows must call that boundary instead of maintaining a second accounting truth.

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

Finance F1 regression coverage includes balance validation, operation/source idempotency, closed-period rejection, audit rollback, posting-profile posting, and reversal behavior. Live SQL Server/MySQL/MariaDB Finance migration/concurrency acceptance remains a production gate.

Production Authenticode signing requires the real protected signing identity and remains a release acceptance gate.

## Remaining work before 1.0

Major remaining items include:

- live SQL Server/MySQL/MariaDB migration, recovery, concurrency, performance, and supported-version matrices;
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
- Finance F2 Accounts Receivable, followed by Accounts Payable, inventory accounting, banking, financial reporting, and localization packages.

Barcode scanning/generation and label design/printing remain outside current scope. Finance functionality beyond F1 is tracked in `docs/Roadmap.md` rather than claimed as implemented.

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
