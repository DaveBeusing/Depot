# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.14.x-preview** line and is not yet production-certified. Security/compliance roadmap phases 1-7 have their technically implementable repository/application controls in place; production, legal, provider, signing, accessibility, and environment acceptance gates remain where documented.

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
- ISO/IEC-25010-inspired software-quality gates and automated accessibility baselines

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware activity-bar navigation and closeable workspace tabs. Navigation supports stable routes, `Alt+Left` / `Alt+Right` history, `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, `Ctrl+W`, tab cycling, and F1 context Help.

## Workspaces

```text
Dashboard
Inventory
  Overview | Items | Movements
  Overview includes Serial / lot traceability balance + history
Warehouse
  Transfers | Inventory Counts | Material Issues | Material Returns | Shipping
Purchasing
  Purchase Orders | Goods Receipts | Supplier Returns
Sales
  Overview | Quotes | Pricing | Customers | Sales Orders | Invoices
Approvals
  Purchase Approvals | Sales Approvals
Reports
Administration
```

Administration includes Company master data, users/roles, database configuration, backup/restore, Audit Log, Privacy Data, About/application information, Notification Center, and the offline Help Center.

## Item master data and traceability

**Inventory > Items** keeps the part number as the immutable manufacturer part number (MPN) and adds structured identification, lifecycle, trade/compliance and logistics attributes. GTIN is checksum-validated and unique, replacement references target active items, lifecycle dates are consistency-checked, and dangerous goods require a UN number.

`ItemType`, `TrackingMode` and `LifecycleStatus` are operational master-data controls. Physical inventory workflows are restricted to stock items. Serial-number tracking requires one unique serial allocation per moved unit; lot tracking requires the complete movement quantity to be allocated across one or more lots. Discontinued/obsolete items are blocked for new purchase and sales orders, and purchasing also enforces the configured last-buy date.

Traceability is **movement-derived**. `ItemTrackingUnits` stores serial/lot identity, expiry and block state; `StockMovementTracking` stores signed allocations to posted movements. Current serial/lot quantity and location are calculated from those allocations rather than maintained as a second stock balance.

Tracked workflow grids provide a transient **Serial / lot** entry. Serial syntax is `SERIAL` or `SERIAL|yyyy-MM-dd`; lot syntax is `LOT|quantity` or `LOT|quantity|yyyy-MM-dd`, with one allocation per line. The same syntax is available for manual purchase, withdrawal and correction movements. The posting transaction validates the authoritative tracking mode and requires the allocation total to match the movement quantity.

Tracked outbound operations validate availability at the exact source inventory and reject blocked or expired units. Reversals preserve the exact original serial/lot identity with inverted quantities and fail if the required tracked stock is no longer available at the original location. **Inventory > Overview > Serial / lot traceability** provides searchable current balances, movement history and audited block/unblock controls for authorized users.

Legacy Excel opening-balance import intentionally remains fail-closed for tracked items because its format contains no serial/lot allocation columns. See `docs/ITEM_MASTER_DATA.md` and `docs/ITEM_TRACEABILITY.md`.

## Business-record integrity

Finalized operational records are historical evidence. Corrections use explicit reversal, return, cancellation, close, or credit-note transactions rather than silently rewriting finalized history. Tracking allocations follow the same rule and are immutable movement evidence.

## Privacy

**Administration > Privacy Data** provides authorized discovery for person-related data and machine-readable JSON export. Authentication hashes, connection credentials, and protected settings are excluded by design.

## Electronic invoicing

Depot includes an EN 16931-oriented semantic electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Sales-invoice posting freezes seller/buyer identity, generates the structured XML and stores a SHA-256 fingerprint. Representative XML is validated in CI with a pinned KoSIT validator/configuration.

## Database providers

SQLite is the default provider. Microsoft SQL Server and MySQL/MariaDB implementations are also present. Supported remote-provider settings enforce encrypted transport. Live-server migration, backup/restore, recovery, concurrency, and version-matrix acceptance remain required before a server configuration is advertised as production-supported.

The core database schema is currently **29** plus additive provider-neutral feature schema extensions. Application release versions and database schema versions are independent.

## Offline Help Center

Depot ships an embedded Markdown Help Center rendered natively in WPF. It is permission-filtered, locally searchable, uses stable topic links, and opens as a workspace tab. F1 resolves the current Help context.

Help manifest **1.8** includes the serial/lot traceability guide and cross-links it with Items, inventory, goods receipts, transfers, material flows, shipping/customer returns, supplier returns, stock troubleshooting and audit topics. See `docs/HELP_CENTER.md` for authoring rules.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain layout/bindings, ViewModels presentation state/commands, Services business rules and transactions, and Repositories persistence SQL/mapping. Traceability follows the same layering and participates in the transaction that posts the owning business document and stock movement.

## Technology

- .NET 10 for Windows
- WPF and MVVM
- SQLite via `Microsoft.Data.Sqlite`
- SQL Server via `Microsoft.Data.SqlClient`
- MySQL/MariaDB via `MySqlConnector`
- ClosedXML for Excel import/export
- PDFsharp-WPF for Sales and fulfillment documents
- Nullable reference types enabled

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

Production Authenticode signing requires the real protected signing identity and remains a release acceptance gate.

## Keyboard navigation

| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Quick Open |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+W` | Close active tab |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Alt+Left` | Navigate backward |
| `Alt+Right` | Navigate forward |
| `F1` | Context-sensitive Help |

## Project structure

```text
src/Depot/
  Controls/       Reusable WPF controls
  Data/           Provider factories, initialization, migrations
  Help/           Embedded offline Help Center
  Models/         Domain, status, report and e-invoice models
  Repositories/   Provider-neutral persistence
  Resources/      Design system and branding
  Services/       Business/application workflows
  ViewModels/     Presentation logic and commands
  Views/          WPF views and windows
tests/Depot.Tests/
  Unit, integration, security, quality and conformance tests
scripts/
  e-invoice, quality and security/compliance automation
```

## Remaining work before 1.0

Major remaining acceptance work is primarily production/environment specific:

- live SQL Server/MySQL/MariaDB migration, recovery, concurrency, performance, and supported-version matrices
- Windows ACL-denied recovery test
- production code-signing certificate and timestamp validation
- interactive keyboard/focus, Narrator/Accessibility Insights, and DPI acceptance
- representative production sizing/load tests
- explicit EN 16931 tax-category/exemption semantics for zero-rated, exempt, and reverse-charge invoice scenarios
- buyer/XRechnung finalization for electronic credit notes
- production recipient/channel routing and full KoSIT/XRechnung scenario validation
- PDF/A-3 implementation before any ZUGFeRD/Factur-X claim
- operator/legal acceptance for GDPR, GoBD, CRA classification/conformity, retention periods, and organization-specific procedures
- installer/package, upgrade, rollback, and uninstall acceptance

Barcode scanning/generation, label design/printing, payment collection, accounts receivable, and general-ledger functionality remain outside current scope.

## Documentation

- `docs/Architecture.md`
- `docs/ITEM_MASTER_DATA.md`
- `docs/ITEM_TRACEABILITY.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/SECURITY_ROADMAP.md`
- `docs/HELP_CENTER.md`
- `docs/compliance/`

## License

Depot is licensed under the MIT License.