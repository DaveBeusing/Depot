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
- Unsaved-changes protection across workspace changes, section changes, tab closing, sign-out, and application closing
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

Sales is a separate commercial and fulfillment domain rather than procurement documents used in reverse.

```text
Customer + Contacts
   ↓
Quote / Customer Pricing (optional)
   ↓
Sales Order Draft
   ↓
Submit / Approval
   ↓
Inventory Reservation
   ↓
Release
   ↓
Picking / Packed / Shipment
   ↓
SalesShipment stock movement
   ↓
Sales Invoice
   ↓
Completed
```

Corrections remain separate and auditable:

```text
Incorrect shipment posting → Shipment Reversal
Physical goods returned    → Customer Return
Posted invoice correction  → Credit Note
```

Implemented Sales capabilities include:

- Sales Commercial Hub combining operational overview, Quotes and customer Pricing
- Dedicated Customer workspace with search, customer editor, multiple normalized addresses and multiple contacts
- Customer contact roles for General, Commercial, Purchasing, Logistics, Accounting and Technical use cases, including a primary-contact flag
- Billing, Shipping and Other customer address types with per-type default selection
- Sales Order billing/shipping address picker and immutable address snapshots
- Customer-specific price lists with validity windows, item prices, discounts and customer assignments
- Price resolution for Quotes and an explicit **Apply customer price** action in Sales Orders
- Sales Quotes with Draft, Sent, Accepted, Rejected and Converted lifecycle, contact/address snapshots and conversion into a Sales Order draft
- Quote PDF generation and local `.eml` email drafts with the PDF attached
- Sales orders and lines with automatic numbering, item snapshots, prices, discounts, tax, requested delivery dates, and approval workflow
- Sales Order Timeline spanning approval, release, shipment, invoice, return and credit-note events
- Creator/approver separation with Administrator override
- Inventory reservations that reduce **available** stock without changing physical stock
- Concurrency-safe reservation checks against current on-hand quantity and reservations from other sales orders
- Partial reservation and release; unreserved quantities remain visible as **Backorder** and can be allocated later
- Backorder workflow notifications when an order is released with unreserved demand
- Per-line Ordered, Reserved, Backorder, Shipped, and Invoiced quantities
- Dedicated Shipping workspace with shipment Draft state plus **Not Started → Picking → Packed** packing workflow
- Shipment posting is blocked until the shipment is Packed
- Pick List, Packing Slip and Delivery Note PDF generation
- Atomic shipment posting with negative `SalesShipment` stock movements and reservation consumption
- Shipment reversal through immutable positive `SalesShipmentReversal` counter-movements
- Customer Returns for real physical returns with positive `CustomerReturn` movements and workflow notifications
- Customer Return Receipt PDF generation
- Shipment-based invoice creation with pricing/tax and billing-address snapshots
- Invoice Due Status with Not Due, Due Today and Overdue states
- Invoice PDF generation and local email-draft creation with PDF attachment
- Draft invoice cancellation
- Full and partial immutable Credit Notes with cumulative quantity validation
- Credit Note PDF generation and workflow notifications
- Sales Dashboard metrics including approvals, reservation/backorder attention, fulfillment workload, returns, credits and monthly net sales
- Sales records in Quick Open and workflow actions in the Command Palette
- Notification deep links into Sales Orders, Approvals, Shipping, Customer Returns, Invoices and Credit Notes
- Dedicated offline Help topics for Sales, Customers, Quotes, Pricing, Orders, Shipping and Invoices

### Sales roles

Default system roles include:

- **Sales User** — customers, contacts, quotes and sales-order creation/submission
- **Sales Manager** — quote conversion, customer pricing management, approval, release and fulfillment monitoring
- **Warehouse Operator** — shipment picking/packing/posting/reversal and customer returns
- **Finance** — invoices, credit notes and pricing visibility
- **Administrator** — all permissions and all role-oriented dashboard views

All actions remain permission-based; role definitions are defaults rather than hard-coded workflow identities.

### Database providers and migrations

- SQLite is the default provider and is covered by automated integration tests.
- Microsoft SQL Server has a dedicated connection factory, database initializer, locking SQL, connection tests, and error normalization.
- MySQL/MariaDB has a dedicated connection factory, database initializer, locking SQL, connection tests, and error normalization.

The core Depot database schema is currently **29**. Sales uses a versioned feature registry in `DepotFeatureVersions`; the current Sales schema is **6**:

```text
v1  Initial Sales domain
v2  Shipment corrections, Customer Returns and Credit Notes
v3  Normalized Customer Addresses
v4  Reservation history and repeated backorder allocation
v5  Sales Order billing/shipping address snapshots
v6  Customer Contacts, Price Lists, Customer Pricing, Sales Quotes and Shipment Packing state
```

The feature migration remains intentionally separate until the final version 1.0 migration policy is consolidated. SQL Server and MySQL/MariaDB support exists in code but still requires live-server migration, backup/restore and concurrency certification before 1.0.

### Remaining work before version 1.0

- Live SQL Server and MySQL/MariaDB installation and migration matrices
- Live-server backup/restore and failure-recovery drills
- Multi-client reservation, shipping, receipt, and inventory concurrency tests against server providers
- Large-data acceptance testing with at least 100,000 records and high movement volumes
- UI runtime, accessibility, scaling, keyboard-navigation, localization, packaging, and upgrade acceptance
- Security review of deployment defaults, credentials, logs and backup retention
- Consolidate feature migrations into the final 1.0 migration/upgrade policy

Barcode scanning/generation, label design/printing, payment collection, accounts receivable, and general-ledger functionality remain outside the current scope.

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

Views contain layout and bindings. ViewModels contain presentation state and commands. Services enforce business rules and transactional workflows. Repositories contain persistence SQL and mapping. `DatabaseAccess` provides shared asynchronous queries, paging, transactions, streaming, and provider normalization.

See [Architecture](docs/Architecture.md) for details.

## UI design system

Shared resources live under `src/Depot/Resources`; reusable controls live under `src/Depot/Controls`.

The UI uses a compact dark visual language with centralized colors, typography, consistent interaction sizing, cards, dark grids/lists/combo boxes, master/detail layouts, status presentation, loading feedback, workflow action bars and empty states.

## Technology

- .NET 10 for Windows
- WPF and MVVM
- SQLite via `Microsoft.Data.Sqlite`
- SQL Server via `Microsoft.Data.SqlClient`
- MySQL/MariaDB via `MySqlConnector`
- ClosedXML for Excel import/export
- PDFsharp-WPF for Sales documents
- Nullable reference types enabled

## Getting started

Requirements:

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio, JetBrains Rider, VS Code, or the .NET CLI

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
dotnet restore Depot.slnx
dotnet run --project src/Depot/Depot.csproj -c Debug
```

The first installation uses local SQLite and creates `depot.db`. Connection and backup settings are stored in `depot.settings`. Administration > Database can configure SQLite, SQL Server, or MySQL/MariaDB.

For a new database, sign in with `admin@depot.local` and `Depot123!`, then change the password in Administration > Users.

## Build and publish

Depot targets `net10.0-windows`. Version metadata comes from `Directory.Build.props`; the current preview version is **0.13.28-preview**.

### Debug

```powershell
dotnet build Depot.slnx -c Debug
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

### Framework-dependent publish

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false
```

### Self-contained publish

```powershell
dotnet publish src/Depot/Depot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

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

Default output:

```text
src\Depot\bin\Release\net10.0-windows\win-x64\publish\
```

For this mode, `Depot.exe` is the distributable application. Runtime data such as `depot.db`, `depot.settings`, logs, backups, generated PDFs, and exports remains external. Do not enable `PublishTrimmed` without a dedicated WPF/XAML trimming validation pass.

For a clean output directory:

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

Before distribution, run both test groups and the Release publish:

```powershell
dotnet test tests/Depot.Tests/Depot.Tests.csproj -c Release --filter "FullyQualifiedName~Sales"
dotnet test tests/Depot.Tests/Depot.Tests.csproj -c Release --filter "FullyQualifiedName!~Sales"
```

CI performs both suites separately and only attempts the final self-contained single-file publish after both pass.

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
