# Depot

Depot is a Windows desktop application for inventory, warehouse, supplier, procurement, user, import, and reporting workflows. It is built with .NET 10, WPF, strict MVVM, and a provider-neutral ADO.NET persistence layer.

The project is under active development on the `0.13.0-preview` line. Implemented workflows are not described as production-ready until the version 1.0 verification checklist has been completed.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-10-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Database](https://img.shields.io/badge/database-SQLite%20%7C%20SQL%20Server%20%7C%20MySQL%2FMariaDB-0F80CC)
![Architecture](https://img.shields.io/badge/architecture-MVVM-orange)
![License](https://img.shields.io/badge/license-MIT-yellow)

## Current implementation status

### Fully implemented in the application

- Email/password authentication, PBKDF2-SHA256 password hashing, fixed Administrator/Purchasing/Approver/WarehouseOperator/User roles, session switching, and administrator-managed users
- Dashboard metrics, recent movements, inventory valuation, and German Euro formatting
- Item, inventory, purpose, warehouse, storage-location, and stock-movement workflows
- Reason codes with immutable technical keys, editable display names, protected workflow system codes, search, activation, and movement references
- Normalized item master data: manufacturer, category, unit of measure, and packaging
- Supplier categories, suppliers, and many-to-many `SupplierItem` assignments with supplier-specific commercial data
- Supplier-return documents linked to received positions, with net-return validation, atomic negative stock movements, and counter-booking
- Purchase orders and lines with automatic `PO-xxxxxx` numbering, separation-of-duties approval workflow, search, and filtering
- Dedicated permission-restricted approval work queue with server-side filters, paging, totals, details, and status history
- Delivery-note-based goods receipts with receipt date, receiving user, partial receipts, and automatic purchase-order status updates
- Atomic goods-receipt posting across receipt records, received quantities, stock movements, and order status
- Warehouse stock transfers with draft editing, server-side search, status filtering, paging, atomic posting, paired transfer movements, and concurrency-safe stock checks
- Inventory counts with atomic warehouse snapshots, paged counting and review, optimistic concurrency, and atomic correction posting through stock movements
- Audited reversal workflows for posted goods receipts, material withdrawals, stock transfers, and inventory counts; reversals create immutable counter-movements and correct their document state atomically
- Excel import, report search, grouped reports, and Excel export
- Audit persistence for relevant create/update operations
- Optimistic concurrency using version columns and explicit conflict errors
- Database administration: overview, provider/schema/connection display, backup validation, backup, restore with safety backup, scheduled backups, integrity checks, and SQLite compaction
- Encrypted `depot.settings` storage using Windows DPAPI
- Connection testing, connection-state UI, safe provider-specific errors, and database logging without exposed credentials

### Database providers

- SQLite is the default first-installation provider and is covered by automated integration tests.
- Microsoft SQL Server has a dedicated connection factory, database initializer, schema migrations, locking SQL, connection tests, and error normalization.
- MySQL/MariaDB has a dedicated connection factory, database initializer, schema migrations, locking SQL, connection tests, and error normalization.

SQL Server and MySQL/MariaDB support is implemented in code, but live-server migration, backup/restore, concurrency, and long-running acceptance tests are still required before version 1.0. Provider support must therefore not yet be interpreted as a production certification.

### Partially implemented

- Most interactive list loading is asynchronous and cancellable.
- Items, inventory, movements, users, and purchase-order searches use server-side paging infrastructure.
- Search debounce is used across the main large-data and master-data screens.
- Productive list and report paths no longer use synchronous, unbounded `GetAll()` reads against remote databases.
- Inventory reports use server-side paging and aggregation; large Excel exports read deterministic database slices and report progress.
- The purchase-order screen currently loads a bounded server-side page without full user-facing page navigation.
- Audit records are persisted, but there is no administration UI for browsing or exporting the audit trail.
- General application preferences remain a placeholder; database and backup settings are implemented separately.

### Not started

- Barcode scanning and barcode generation
- Label design and printing
- Dedicated audit-log viewer/export
- General application-preferences module

### To verify before version 1.0

- Live SQL Server and MySQL/MariaDB installation and migration matrices
- Live server backup/restore and failure-recovery drills
- Multi-client concurrency and long-running load tests against server providers
- Large-data acceptance tests with at least 100,000 records
- Complete UI, accessibility, keyboard-navigation, localization, packaging, and upgrade testing
- Security review of deployment defaults, credentials, retained legacy invoice-path data, logs, and backup retention

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

Shared resources live under `src/Depot/Resources`; reusable controls live under `src/Depot/Controls`. The UI kit includes colors, typography, spacing, buttons, inputs, navigation, cards, DataGrid styles, dialogs, status presentation, loading feedback, and reusable controls such as `Card`, `MetricCard`, `SearchBox`, `PageHeader`, `StatusBadge`, and `EmptyState`.

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
- Visual Studio, JetBrains Rider, or the .NET CLI

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
dotnet build Depot.slnx
dotnet run --project src/Depot/Depot.csproj
```

The first installation uses local SQLite and creates `depot.db`. The current database schema version is **27**.

Connection and backup settings are stored in `depot.settings`. The file is a JSON envelope with a DPAPI-encrypted payload for the current Windows user. Administration > Database can configure, test, and activate SQLite, SQL Server, or MySQL/MariaDB connections. Provider changes take effect after restarting Depot. Connection attempts and failures are written to `depot.database.log` without connection strings or passwords.

For a new database, sign in with `admin@depot.local` and `Depot123!`, then change the password in Administration > Users.

## Project structure

```text
src/Depot/
  Controls/       Reusable WPF controls
  Data/           Provider factories, initialization, and migrations
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

## License

Depot is released under the MIT License. See [LICENSE.md](LICENSE.md).
