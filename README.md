# Depot

Depot is a Windows desktop application for managing items, inventories, stock movements, master data, users, imports, and reports. It is built with .NET 10, WPF, MVVM, and SQLite.

The project started as a replacement for an Excel-based inventory and is under active development toward version 1.0. Database schema and domain model changes are still possible before the first stable release.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-10-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Database](https://img.shields.io/badge/database-SQLite-0F80CC)
![Architecture](https://img.shields.io/badge/architecture-MVVM-orange)
![License](https://img.shields.io/badge/license-MIT-yellow)

## Current features

### Dashboard

- Total item count, stock quantity, inventory value, and movement count
- German Euro formatting
- Recent stock movements
- Reusable metric cards and responsive dashboard layout

### Inventory

- Inventory overview by item, purpose, and location
- Current stock calculation
- Weighted average cost and inventory valuation
- Detailed inventory information and recent movements
- Live search across inventory data

### Items

- Create and edit items
- Deactivate items
- Part number, description, manufacturer, and category data
- Live search

### Stock movements

- Opening balances
- Purchases
- Withdrawals
- Corrections
- Transfer support in the domain model
- Unit prices, references, and notes
- Live search and stock validation

### Excel import

- `.xlsx` file selection
- Import preview and summary statistics
- Duplicate detection and validation warnings
- Purpose and location resolution
- Inventory creation and opening balance import
- Import result summary

### Reports and export

- Inventory Value
- Stock by Location
- Stock by Purpose
- Stock by Category
- Stock by Manufacturer
- Search and filtering
- Excel export with German Euro number formats

### Administration

- Role-based Administration navigation
- Purpose management
- Location management
- User management
- Activate and deactivate users
- Administrator role assignment
- Excel import workspace
- Settings placeholder for future application preferences
- Encrypted local and SQL Server connection configuration
- Local SQLite fallback for prepared SQL Server installations

### Session and users

- Email and password authentication at startup
- PBKDF2-SHA256 password hashing with per-user salts
- Editable user email addresses and administrator-managed password changes
- Administrator and standard user roles
- Administration visibility based on permissions
- Current user panel in the sidebar
- Logout and session switching
- Database connection status in the login window and sidebar

## UI design system

Depot includes a reusable WPF design system under `src/Depot/Resources` and `src/Depot/Controls`.

Current controls include:

- `Card`
- `MetricCard`
- `SearchBox`
- `PageHeader`
- `StatusBadge`
- `EmptyState`
- `SidebarBrand`
- `SidebarUserPanel`
- `PasswordInput`
- `ConnectionStatusIndicator`

The resource dictionaries provide shared colors, typography, spacing, buttons, inputs, navigation, cards, DataGrid styling, dialogs, status presentation, and empty states.

## Architecture

Depot follows a layered MVVM architecture:

```text
Views
  |
ViewModels
  |
Services
  |
Repositories
  |
SQLite
```

- Views contain layout and bindings.
- ViewModels contain presentation state and UI commands.
- Services contain business logic and application workflows.
- Repositories contain SQLite access and mapping.
- `App.xaml.cs` is the composition root.

Native file dialogs are accessed through `IFileDialogService`, keeping WPF dialog creation outside the ViewModels.

## Technology

- .NET 10 for Windows
- WPF
- SQLite via `Microsoft.Data.Sqlite`
- ClosedXML for Excel import and export
- Nullable reference types enabled

## Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio, JetBrains Rider, or the .NET CLI

## Getting started

Clone the repository:

```powershell
git clone https://github.com/DaveBeusing/Depot.git
cd Depot
```

Build the application:

```powershell
dotnet build src/Depot/Depot.csproj
```

Run the application:

```powershell
dotnet run --project src/Depot/Depot.csproj
```

The SQLite database is created and migrated automatically as `depot.db` in the working directory. The current schema version is 6.

Database connection settings are stored in `depot.settings`. The file is a JSON envelope whose payload is encrypted with Windows DPAPI for the current Windows user. A first installation always starts with the local SQLite database. SQL Server server, database, user, password, encryption, and certificate settings can be prepared under Administration > Database; the local database remains active until the SQL Server repository provider is implemented.

For a new database, sign in with `admin@depot.local` and the initial password `Depot123!`. Change the password in Administration > Users after the first sign-in. Existing version 5 users are migrated to an email ending in `@depot.local` and receive the same initial password.

The solution can also be opened through `Depot.slnx`.

## Project structure

```text
src/Depot/
  Controls/       Reusable WPF controls
  Data/           Database initialization and migrations
  Models/         Domain and report models
  Repositories/   SQLite persistence
  Resources/      Design system resource dictionaries
  Services/       Business logic and application services
  ViewModels/     Presentation logic and commands
  Views/          WPF views and windows
```

## Development status

Implemented workflows are usable, but Depot remains in active development. The release checklist has not yet been completed.

Planned work includes:

- Database maintenance, backup, and restore
- Application settings
- Manufacturer, category, and packaging master data
- Barcode and label support
- Audit logging
- Release hardening and automated tests

See the project documentation for additional context:

- [Architecture](docs/Architecture.md)
- [Coding Standard](docs/CodingStandard.md)
- [Roadmap](docs/Roadmap.md)
- [Version 1.0 release checklist](docs/RELEASE_1_0.md)

## License

Depot is released under the MIT License. See [LICENSE.md](LICENSE.md) for details.
