# Depot

Depot is a Windows desktop application for inventory, warehouse, supplier, procurement, administration, reporting, and operational workflows. It is built with .NET 10, WPF, MVVM, and a provider-neutral ADO.NET persistence layer.

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
- `Ctrl+P` Quick Open for workspaces, sections, items, purchase orders, and suppliers
- Grouped Quick Open results with type badges and session-recent records
- `Ctrl+Shift+P` Command Palette for navigation and direct workflow actions
- Direct commands for New Item, New Purchase Order, Start Inventory Count, Transfer Stock, and Receive Goods
- F1 context Help in its own workspace tab
- Notification Center and signed-in user details as workspace tabs
- Permission-aware unread notification badge
- Unsaved-changes protection for Items, Purchase Orders, Suppliers, Supplier Items, and Roles across workspace changes, section changes, tab closing, sign-out, and application closing
- Action-oriented dashboard links into operational workspaces
- Administrator dashboard visibility across all available role-oriented overview sections

### Fully implemented workflows

- Email/password authentication, PBKDF2-SHA256 password hashing, database-backed multi-role RBAC, session switching, and administrator-managed users and roles
- Dashboard metrics, operational attention links, recent movements, and inventory valuation
- Item, inventory, purpose, warehouse, storage-location, and stock-movement workflows
- Normalized item master data: manufacturer, category, unit of measure, and packaging
- Reason codes with immutable technical keys, editable display names, protected workflow system codes, search, activation, and movement references
- Supplier categories, suppliers, and many-to-many `SupplierItem` assignments with supplier-specific commercial data
- Purchase orders and lines with automatic numbering, separation-of-duties approval workflow, search, filtering, status history, and direct navigation
- Dedicated permission-restricted purchase-order approval work queue
- Delivery-note-based goods receipts with partial receipts and automatic purchase-order status updates
- Atomic goods-receipt posting across receipt records, received quantities, stock movements, and order status
- Supplier-return documents linked to received positions with atomic negative stock movements
- Warehouse stock transfers with draft editing, search, status filtering, paging, atomic posting, paired movements, and concurrency-safe stock checks
- Inventory counts with warehouse snapshots, counting, review, optimistic concurrency, and atomic correction posting
- Material issue and material return workflows
- Audited reversal workflows for posted goods receipts, material issues, stock transfers, and inventory counts
- Excel import and export
- Inventory and grouped reporting with search, paging, aggregation, and export
- Audit persistence and a filtered read-only Audit Log administration workspace
- Optimistic concurrency using version columns and explicit conflict errors
- Database administration with provider/schema/connection overview, backup validation, backup, restore with safety backup, scheduled backups, integrity checks, and SQLite compaction
- Encrypted `depot.settings` storage using Windows DPAPI
- Connection testing, connection-state UI, safe provider-specific errors, and database logging without exposed credentials
- Integrated offline Help Center with permission-aware topics, search, related topics, diagnostics, and context-sensitive F1 routing
- Internal Notification Center with server-side search and paging, read/archive actions, unread badge, record navigation, and workflow-generated notifications

### Database providers

- SQLite is the default first-installation provider and is covered by automated integration tests.
- Microsoft SQL Server has a dedicated connection factory, database initializer, schema migrations, locking SQL, connection tests, and error normalization.
- MySQL/MariaDB has a dedicated connection factory, database initializer, schema migrations, locking SQL, connection tests, and error normalization.

SQL Server and MySQL/MariaDB support is implemented in code, but live-server migration, backup/restore, concurrency, and long-running acceptance tests are still required before version 1.0. Provider support must therefore not yet be interpreted as a production certification.

### Remaining work before version 1.0

- Live SQL Server and MySQL/MariaDB installation and migration matrices
- Live server backup/restore and failure-recovery drills
- Multi-client concurrency and long-running load tests against server providers
- Large-data acceptance tests with at least 100,000 records
- Complete UI runtime, accessibility, keyboard-navigation, localization, packaging, and upgrade testing
- Security review of deployment defaults, credentials, logs, retained legacy data, and backup retention
- Resolve the currently long-running automated test-process behavior in CI
- General application-preferences module
- Barcode scanning and barcode generation
- Label design and printing

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

- Views contain layout and bindings.
- ViewModels contain presentation state, loading state, and UI commands.
- Services contain validation and business workflows.
- Repositories contain persistence SQL and mapping.
- `DatabaseAccess` provides shared asynchronous queries, paging, transactions, streaming, and provider normalization.
- `App.xaml.cs` is the composition root.

See [Architecture](docs/Architecture.md) for details.

## UI design system

Shared resources live under `src/Depot/Resources`; reusable controls live under `src/Depot/Controls`.

The current UI system uses a compact dark visual language with centralized colors, typography, 32-pixel interaction sizing, consistent cards and container geometry, dark DataGrid/ListBox/ComboBox states, master/detail workflow layouts, status presentation, loading feedback, and reusable controls such as `Card`, `MetricCard`, `SearchBox`, `PageHeader`, `StatusBadge`, `WorkflowActionBar`, `MasterDetailGrid`, and `EmptyState`.

Shell-specific resources are separated from workflow resources so activity navigation, workspace tabs, context navigation, inputs, cards, buttons, tables, and status components share the same design tokens.

## Technology

- .NET 10 for Windows
- WPF and MVVM
- SQLite via `Microsoft.Data.Sqlite`
- SQL Server via `Microsoft.Data.SqlClient`
- MySQL/MariaDB via `MySqlConnector`
- ClosedXML for Excel import and export
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

Run Depot directly in Debug configuration during development:

```powershell
dotnet run --project src/Depot/Depot.csproj -c Debug
```

The first installation uses local SQLite and creates `depot.db`. The current database schema version is **29**.

Connection and backup settings are stored in `depot.settings`. The file is a JSON envelope with a DPAPI-encrypted payload for the current Windows user. Administration > Database can configure, test, and activate SQLite, SQL Server, or MySQL/MariaDB connections. Provider changes take effect after restarting Depot. Connection attempts and failures are written to `depot.database.log` without connection strings or passwords.

For a new database, sign in with `admin@depot.local` and `Depot123!`, then change the password in Administration > Users.

## Build and publish

Depot targets `net10.0-windows`. Version metadata is taken from `Directory.Build.props`; the current preview version is **0.13.28-preview**.

### Debug build

Use Debug builds for local development and debugging:

```powershell
dotnet build Depot.slnx -c Debug
```

Run the application directly:

```powershell
dotnet run --project src/Depot/Depot.csproj -c Debug
```

The application output is written below:

```text
src\Depot\bin\Debug\net10.0-windows\
```

### Release build

Build the complete solution with compiler optimizations enabled:

```powershell
dotnet build Depot.slnx -c Release
```

The application output is written below:

```text
src\Depot\bin\Release\net10.0-windows\
```

A Release build is useful for validation, but `dotnet publish` should be used when creating files for distribution.

### Framework-dependent Release publish

Create a compact Windows x64 publish that uses an installed .NET 10 Desktop Runtime on the target computer:

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false
```

The published files are written to:

```text
src\Depot\bin\Release\net10.0-windows\win-x64\publish\
```

Distribute the complete contents of this directory. The target computer must have the matching .NET 10 Desktop Runtime installed.

### Self-contained Release publish

Create a Windows x64 deployment that carries its own .NET runtime:

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

This produces a larger publish directory, but the target computer does not need a separately installed .NET runtime.

### Self-contained single-file publish

For a portable single executable, use:

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

The result is written to:

```text
src\Depot\bin\Release\net10.0-windows\win-x64\publish\
```

For this publish mode, `Depot.exe` is the distributable application file. Managed dependencies, the .NET runtime, embedded Help content, and native libraries supported by single-file publishing are bundled into the executable. `IncludeNativeLibrariesForSelfExtract` allows native dependencies to be extracted automatically by the .NET host at runtime when required.

Runtime-generated application data is intentionally not bundled into the executable. Files such as `depot.db`, `depot.settings`, database logs, backups, and exports are created or managed by Depot at runtime.

> **Note:** Do not enable `PublishTrimmed` for release distribution without a dedicated trimming validation pass. Depot is a WPF application and uses XAML, bindings, reflection-sensitive framework behavior, and third-party libraries that may require metadata which trimming can remove.

### Optional publish directory

To produce a clean, predictable output folder, append `-o` to any publish command:

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

This writes the distributable files to:

```text
artifacts\publish\win-x64\
```

Before distributing a Release or single-file build, run the automated tests and perform a clean-machine startup test against the intended database provider.

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
