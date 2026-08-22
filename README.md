# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.14.28-preview** line and is not yet production-certified.

## Application shell

Depot uses a dark workspace-oriented shell with permission-aware activity-bar navigation and closeable workspace tabs. After sign-in, no module or tab is selected automatically: a tabless Welcome page greets the user according to the local time of day and shows shortcuts for Quick Open, Command Palette, tab switching, tab closing, and context Help. Closing the final tab returns to this Welcome page.

The status bar shows database connection state; hovering its database indicator exposes the current connection detail. The current application version is shown on the right and opens the existing About page when selected.

Navigation supports stable routes, `Alt+Left` / `Alt+Right` history, `Ctrl+P` Quick Open, `Ctrl+Shift+P` Command Palette, keyed document tabs for supported records, `Ctrl+W`, `Ctrl+Tab` / `Ctrl+Shift+Tab`, and F1 context Help.

## Workspaces

```text
Dashboard
Inventory
  Overview | Items | Movements
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

Shipping lives under **Warehouse** because picking, packing, shipment posting, reversals, and physical Customer Returns are warehouse operations even though they originate from Sales Orders.

## Dashboard

The Dashboard is permission-aware and uses existing business services/repositories rather than maintaining separate dashboard data. Administrators receive all currently implemented overview groups:

- **Inventory** — total items, total stock, inventory value, movements
- **Purchasing** — pending/approved orders, partial receipts, overdue deliveries, Supplier Returns requiring attention
- **Warehouse** — Inventory Counts awaiting review/posting and open transfers
- **Sales** — approvals, reservation/backorder and fulfillment workload, draft invoices/shipments, returns, credits, monthly net sales
- **Approvals** — open Purchase Order approval summary
- **Administration** — active users
- **Reports** — entry into the existing Reports workspace

Recent inventory movements remain available as operational activity. Non-administrator content follows effective permissions.

## Functional scope

**Inventory and Warehouse:** item/inventory master data, immutable stock movements, warehouses/locations, transfers, inventory counts with optimistic concurrency, material issues/returns, shipping, picking, packing, shipment posting/reversal, Customer Returns, fulfillment PDFs, Excel import/export.

**Purchasing:** Suppliers, Supplier Items, Purchase Orders, Purchase Approvals, Goods Receipts, Supplier Returns, partial receipts, status history, and atomic inventory posting.

```text
Supplier → Purchase Order → Submit → Purchase Approval → Ordered → Goods Receipt → Stock Movement
```

**Sales:** Customers and Contacts, Quotes, Customer Pricing, Sales Orders, Sales Approvals, reservations/backorders, Warehouse fulfillment, Invoices, Credit Notes, Customer Returns, PDFs, email drafts, and Sales Order Timeline.

```text
Customer / Quote / Pricing → Sales Order → Sales Approval → Reservation / Backorder
→ Warehouse > Shipping → Picking → Packed → Shipment → Sales > Invoices / Credit Notes
```

Supported Sales records can open as keyed document tabs so reopening the same supported record activates its existing tab instead of creating a duplicate.

**Approvals and security:** central Purchase/Sales approval queues, creator/approver separation enforced in business services, database-backed multi-role RBAC, authentication, session switching, and audited administrator overrides.

**Administration:** users/roles, database provider configuration, backup/restore, scheduled backups, integrity checks, SQLite compaction, Audit Log, About/application information, Notification Center, and offline Help Center.

## Database providers

SQLite is the default provider. Microsoft SQL Server and MySQL/MariaDB provider implementations are also present. Live-server migration, recovery, backup/restore, and concurrency certification remain part of the version 1.0 acceptance work.

The core database schema is currently **29**. Sales uses the versioned `DepotFeatureVersions` registry and is currently schema **6**. Application release versions and database schema versions are independent.

## Offline Help Center

Depot ships an embedded Markdown Help Center rendered natively in WPF. It is permission-filtered, locally searchable, uses stable topic links, and opens as a normal workspace tab. F1 resolves the current Help context.

Help manifest **1.5** documents the current tabless Welcome state, fully closeable tabs, navigation history, database/version status-bar behavior, current workspace structure, and administrator Dashboard overview. See `docs/HELP_CENTER.md` for authoring rules.

## Architecture

```text
Views → ViewModels → Services → Repositories → DatabaseAccess
                                      ↓
                    SQLite / SQL Server / MySQL or MariaDB
```

Views contain layout/bindings, ViewModels presentation state/commands, Services business rules and transactions, and Repositories persistence SQL/mapping. Shared UI resources live under `src/Depot/Resources`; reusable WPF controls under `src/Depot/Controls`.

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
dotnet restore Depot.slnx
dotnet run --project src/Depot/Depot.csproj -c Debug
```

A new installation defaults to local SQLite and creates `depot.db`; settings are stored in `depot.settings`. **Administration > Database** configures SQLite, SQL Server, or MySQL/MariaDB.

For a new database, sign in with `admin@depot.local` / `Depot123!` and change the password in **Administration > Users**.

## Build and publish

Depot targets `net10.0-windows`. WPF XAML items are explicitly compiled by the project.

```powershell
dotnet build Depot.slnx -c Debug
dotnet build Depot.slnx -c Release
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

Runtime data (`depot.db`, `depot.settings`, logs, backups, PDFs, exports) remains external. Do not enable `PublishTrimmed` without dedicated WPF/XAML trimming validation.

CI separates bounded Sales, Inventory/Warehouse, Purchasing, Shell/UX, and Core/Persistence suites, validates Release build/publish, and cancels superseded runs for the same pull request.

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
  Models/         Domain, status, report models
  Repositories/   Provider-neutral persistence
  Resources/      Design system and branding
  Services/       Business/application workflows
  ViewModels/     Presentation logic and commands
  Views/          WPF views and windows
tests/Depot.Tests/
  Unit and SQLite integration tests
```

## Remaining work before 1.0

- Live SQL Server/MySQL/MariaDB installation and migration matrices
- Live-server backup/restore and recovery drills
- Multi-client concurrency tests against server providers
- Large-data acceptance testing
- UI accessibility, scaling, keyboard, localization, packaging, upgrade acceptance
- Security review of deployment defaults, credentials, logs, backup retention
- Consolidated final 1.0 migration/upgrade policy

Barcode scanning/generation, label design/printing, payment collection, accounts receivable, and general-ledger functionality remain outside current scope.

## Documentation

- `docs/Architecture.md`
- `docs/CodingStandard.md`
- `docs/Roadmap.md`
- `docs/RELEASE_1_0.md`
- `docs/VERSIONING.md`
- `docs/DATA_ACCESS_AUDIT.md`
- `docs/HELP_CENTER.md`

## License

Depot is licensed under the MIT License.
