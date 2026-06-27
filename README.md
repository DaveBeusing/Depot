# 📦 Depot

**Modern inventory management**

Depot is a desktop inventory management application built with **.NET 10**, **WPF** and **SQLite**.

It was originally created as a replacement for a large Excel-based inventory and is evolving into a modular inventory management system.

> ⚠️ **Development Status**
>
> Depot is currently under active development.
> Database schema and domain model may change before version **1.0**.

---
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-10-purple)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Database](https://img.shields.io/badge/Database-SQLite-green)
![Architecture](https://img.shields.io/badge/Architecture-MVVM-orange)
![License](https://img.shields.io/badge/License-MIT-yellow)
---

# Features

## Dashboard

* Inventory overview
* Current inventory value
* Total stock quantity
* Recent stock movements

## Inventory

* Current stock
* Average cost calculation
* Inventory valuation
* Inventory details
* Recent movements

## Item Management

* Create items
* Edit items
* Deactivate items
* SQL-based search

## Stock Movements

* Purchase
* Withdrawal
* Correction
* Opening Balance
* SQL-based search

## Excel Import

* Import preview
* Duplicate detection
* Validation
* Summary statistics
* Import execution

---

# Planned Features

* Administration
* Master Data
* Purposes
* Locations
* Inventory model
* Barcode support
* Reports
* Backup & Restore
* User management


## Architecture

```text
┌─────────────┐
│    Views    │
└──────┬──────┘
       │
┌──────▼──────┐
│ ViewModels  │
└──────┬──────┘
       │
┌──────▼──────┐
│  Services   │
└──────┬──────┘
       │
┌──────▼──────┐
│Repositories │
└──────┬──────┘
       │
┌──────▼──────┐
│   SQLite    │
└─────────────┘
```

# Documentation

Further documentation can be found in the **docs** directory.

* [Architecture](docs/Architecture.md)
* [Coding Standard](docs/CodingStandard.md)
* [Roadmap](docs/Roadmap.md)

# Getting Started

Clone the repository.

```powershell
git clone https://github.com/DaveBeusing/Depot.git
```

Open the solution.

```powershell
Depot.slnx
```

Run the application.

```powershell
dotnet run --project src/Depot/Depot.csproj
```

## License

Depot is released under the MIT License.

You are free to use, modify, distribute, and sublicense this software in accordance with the terms of the MIT License.

See the [LICENSE](LICENSE.md) file for the full license text.


Built with ❤️ using .NET, WPF and SQLite