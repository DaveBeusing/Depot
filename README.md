# Depot

Depot is a Windows desktop application for inventory, warehouse, procurement, sales fulfillment, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the **0.13.28-preview** line. Implemented workflows are not described as production-ready until the version 1.0 verification checklist has been completed.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-10-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Database](https://img.shields.io/badge/database-SQLite%20%7C%20SQL%20Server%20%7C%20MySQL%2FMariaDB-0F80CC)
![Architecture](https://img.shields.io/badge/architecture-MVVM-orange)
![License](https://img.shields.io/badge/license-MIT-yellow)

## Current implementation status

### Application shell and UX

Depot uses a dark workspace-oriented shell with an activity bar, persistent tabs, contextual section navigation, and a compact status bar.

Implemented shell features include:

- Activity-bar navigation for top-level workspaces
- Persistent workspace tabs with close buttons, middle-click close, overflow handling, and tab context actions
- `Ctrl+W` to close the active tab
- `Ctrl+Tab` / `Ctrl+Shift+Tab` to move between open tabs
- `Ctrl+P` Quick Open for workspaces, sections, operational records, purchase records, and sales records
- Grouped Quick Open results with type badges and session-recent records
- `Ctrl+Shift+P` Command Palette for navigation and direct workflow actions
- F1 context Help in its own workspace tab
- Notification Center and signed-in user details as workspace tabs
- Permission-aware unread notification badge
- Unsaved-changes protection across workspace changes, section changes, tab closing, sign-out, and application closing, including Sales customer, sales-order, and shipment drafts
- Action-oriented dashboard links into operational workspaces
- Administrator dashboard visibility across all available role-oriented overview sections

### Inventory, warehouse, and procurement

Depot currently includes:

- Email/password authentication, PBKDF2-SHA256 password hashing, database-backed multi-role RBAC, session switching, and administrator-managed users and roles
- Dashboard metrics, operational attention links, recent movements, and inventory valuation
- Item, inventory, purpose, warehouse, storage-location, and immutable stock-movement workflows
- Normalized item master data: manufacturer, category, unit of measure, and packaging
- Reason codes with immutable technical keys and editable display names
- Supplier categories, suppliers, and many-to-many `SupplierItem` assignments
- Purchase orders with approval, ordering, closing, search, status history, and direct navigation
- Goods receipts with partial receipts and atomic stock posting
- Supplier returns with negative stock movements
- Warehouse transfers with paired movements and concurrency-safe stock checks
- Inventory counts with snapshots, review, optimistic concurrency, and atomic correction posting
- Material issue and material return workflows
- Audited reversal workflows for posted warehouse documents
- Excel import and export
- Inventory and grouped reporting
- Filtered read-only Audit Log
- Database administration, backup, restore, scheduled backups, integrity checks, and SQLite compaction
- Integrated offline Help Center and Notification Center

### Sales and order-to-cash

The Sales workspace implements a separate order-to-cash domain rather than reusing procurement documents in reverse.

The primary flow is:

```text
Customer
   ↓
Sales Order Draft
   ↓
Submit / Approval
   ↓
Inventory Reservation
   ↓
Release
   ↓
Shipment
   ↓
SalesShipment stock movement
   ↓
Sales Invoice
   ↓
Completed
```

Implemented Sales capabilities include:

- Customer master data with billing/shipping information, payment terms, tax ID, contact data, currency, activation, and optimistic concurrency
- Sales orders and lines with automatic numbering, customer/item snapshots, pricing, discounts, tax, requested delivery dates, and status workflow
- Creator/approver separation with Administrator override
- Inventory reservations that reduce **available** stock without changing physical stock
- Concurrency-safe reservation checks against current on-hand quantity and reservations from other sales orders
- Partial reservation and release; unreserved quantities remain visible as **Backorder** and can be allocated later
- Per-line Ordered, Reserved, Backorder, Shipped, and Invoiced quantities
- Shipment drafts with editable carrier, tracking number, notes, and delivery-note PDF generation
- Atomic shipment posting that creates negative `SalesShipment` stock movements and consumes reservations
- Shipment reversal before invoicing through positive `SalesShipmentReversal` counter-movements; original movements remain immutable
- Customer Returns for physical returns after shipment, creating positive `CustomerReturn` stock movements
- Shipment-based invoice creation with pricing/tax snapshots and invoice PDF generation
- Draft invoice cancellation before posting
- Posted invoice correction through immutable Sales Credit Notes instead of editing the original invoice
- Sales Overview metrics and central Dashboard integration
- Sales records in Quick Open and workflow actions in the Command Palette
- Notification deep links into Sales Orders, Approvals, Shipping, and Invoices
- Sales-specific offline Help topics

### Sales roles

Default system roles now include:

- **Sales User** — maintains customers and creates/submits sales orders
- **Sales Manager** — reviews, approves, releases, and monitors sales fulfillment
- **Warehouse Operator** — creates/edits/posts/reverses shipments and processes customer returns
- **Finance** — creates/posts invoices and creates/posts credit notes
- **Administrator** — retains every Depot permission and sees every role-oriented dashboard overview

All Sales actions remain permission-based; the role definitions are defaults, not hard-coded workflow identities.

### Database providers and migrations

- SQLite is the default first-installation provider and is covered by automated integration tests.
- Microsoft SQL Server has a dedicated connection factory, database initializer, locking SQL, connection tests, and error normalization.
- MySQL/MariaDB has a dedicated connection factory, database initializer, locking SQL, connection tests, and error normalization.

The core Depot database schema is currently **29**. Sales uses a separate versioned feature-migration registry in `DepotFeatureVersions`; the current Sales feature schema is **2**. This keeps the Sales rollout upgradeable across SQLite, SQL Server, and MySQL/MariaDB while the 1.0 migration chain is being consolidated.

SQL Server and MySQL/MariaDB support is implemented in code, but live-server migration, backup/restore, concurrency, and long-running acceptance tests are still required before version 1.0. Provider support must therefore not yet be interpreted as a production certification.

### Remaining work before version 1.0

- Live SQL Server and MySQL/MariaDB installation and migration matrices, including Sales feature migrations
- Live server backup/restore and failure-recovery drills
- Multi-client reservation, shipping, receipt, and inventory concurrency tests against server providers
- Large-data acceptance tests with at least 100,000 records and high stock-movement volumes
- Complete UI runtime, accessibility, keyboard-navigation, localization, packaging, and upgrade testing
- Security review of deployment defaults, credentials, logs, retained legacy data, and backup retention
- Resolve the currently long-running automated test-process behavior in CI
- Consolidate feature migrations into the final 1.0 migration/upgrade policy

Barcode scanning/generation, label design/printing, CRM-style quoting, payment collection, accounts receivable, and general-ledger functionality are intentionally outside the current order-to-cash scope.

## Architecture

```text
Views
  |
ViewModels
  |
Services
  |
Repositories
  |
DatabaseAccess
  |
SQLite / Microsoft SQL Server / MySQL or MariaDB
```

Views contain layout and bindings. ViewModels contain presentation state and UI commands. Services enforce business rules and transactional workflows. Repositories contain persistence SQL and mapping. `DatabaseAccess` provides shared asynchronous queries, paging, transactions, streaming, and provider normalization. `App.xaml.cs` is the composition root.

See [Architecture](docs/Architecture.md) for details.

## UI design system

Shared resources live under `src/Depot/Resources`; reusable controls live under `src/Depot/Controls`.

The UI uses a compact dark visual language with centralized colors, typography, 32-pixel interaction sizing, consistent cards and container geometry, dark DataGrid/ListBox/ComboBox states, master/detail workflow layouts, status presentation, loading feedback, and reusable controls such as `Card`, `MetricCard`, `SearchBox`, `PageHeader`, `StatusBadge`, `WorkflowActionBar`, `MasterDetailGrid`, and `EmptyState`.

## Technology

- .NET 10 for Windows
- WPF and MVVM
- SQLite via `Microsoft.Data.Sqlite`
- SQL Server via `Microsoft.Data.SqlClient`
- MySQL/MariaDB via `MySqlConnector`
- ClosedXML for Excel import and export
- PDFsharp-WPF for generated Sales documents
- Nullable reference types enabled

## Getting started

Requirements:

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio, JetBrains Rider, VS Code, or the .NET CLI

Clone and restore the repository:

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
dotnet restore Depot.slnx
```

Run Depot directly in Debug configuration:

```powershell
dotnet run --project src/Depot/Depot.csproj -c Debug
```

The first installation uses local SQLite and creates `depot.db`. Connection and backup settings are stored in `depot.settings`. Administration > Database can configure, test, and activate SQLite, SQL Server, or MySQL/MariaDB connections. Provider changes take effect after restarting Depot.

For a new database, sign in with `admin@depot.local` and `Depot123!`, then change the password in Administration > Users.

## Build and publish

Depot targets `net10.0-windows`. Version metadata is taken from `Directory.Build.props`; the current preview version is **0.13.28-preview**.

### Debug build

```powershell
dotnet build Depot.slnx -c Debug
```

Run the application:

```powershell
dotnet run --project src/Depot/Depot.csproj -c Debug
```

Output:

```text
src\Depot\bin\Debug\net10.0-windows\
```

### Release build

```powershell
dotnet build Depot.slnx -c Release
```

Output:

```text
src\Depot\bin\Release\net10.0-windows\
```

Use `dotnet publish` rather than the normal build output for distribution.

### Framework-dependent Release publish

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false
```

The target computer needs the matching .NET 10 Desktop Runtime.

### Self-contained Release publish

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

The target computer does not need a separately installed .NET runtime.

### Self-contained single-file publish

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false
```

The default publish directory is:

```text
src\Depot\bin\Release\net10.0-windows\win-x64\publish\
```

For this mode, `Depot.exe` is the distributable application. Managed dependencies, the .NET runtime, embedded Help content, and supported native libraries are bundled into the executable. Runtime data such as `depot.db`, `depot.settings`, logs, backups, generated PDFs, and exports remains external.

Do not enable `PublishTrimmed` for release distribution without a dedicated WPF/XAML trimming validation pass.

### Optional clean publish directory

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o artifacts/publish/win-x64
```

Before distributing a build, run the automated tests and perform a clean-machine startup test against the intended database provider.

## Keyboard navigation

| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Quick Open |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+W` | Close active workspace tab |
| `Ctrl+Tab` | Next workspace tab |
| `Ctrl+Shift+Tab` | Previous workspace tab |
| `F1` | Context-sensitive Help |

## Project structure

```text
src/Depot/
  Controls/       Reusable WPF controls
  Data/           Provider factories, initialization, and migrations
  Help/           Embedded offline Help Center content
  Models/         Domain, status, and report models
  Repositories/   Provider-neutral persistence
  Resources/      Design system resource dictionaries
  Services/       Business and application workflows
  ViewModels/     Presentation logic and commands
  Views/          WPF views and windows
tests/Depot.Tests/
  Automated unit and SQLite integration tests
```

## Versioning and documentation

Depot uses Semantic Versioning from `Directory.Build.props`. Application release versions and database schema versions are independent. See:

- [Architecture](docs/Architecture.md)
- [Coding Standard](docs/CodingStandard.md)
- [Roadmap](docs/Roadmap.md)
- [Version 1.0 release checklist](docs/RELEASE_1_0.md)
- [Versioning](docs/VERSIONING.md)
- [Data-access audit](docs/DATA_ACCESS_AUDIT.md)
- [Offline Help Center](docs/HELP_CENTER.md)
- [Notification Center](docs/NOTIFICATION_CENTER.md)

## License

Depot is released under the MIT License. See [LICENSE.md](LICENSE.md).
