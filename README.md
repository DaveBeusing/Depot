# 📦 Depot

Depot is a desktop application for managing local inventory and assets.

## Goals

* Replace the current Excel-based inventory management process
* Provide accurate and traceable inventory tracking
* Support inventory movements and stock valuation
* Use a local database with the option to migrate to a central server later
* Support future multi-user operation
* Keep external dependencies to a minimum

## Technology Stack

* .NET 10
* WPF
* SQLite
* MVVM Architecture

## Architecture

```text
Views
  ↓
ViewModels
  ↓
Services
  ↓
Repositories
  ↓
Database
```

## Planned Features

### Version 1

* Item Management
* Goods Receipts
* Stock Withdrawals
* Inventory Overview
* Stock Valuation

### Version 2

* Locations
* Inventory Counting
* Projects
* Reservations
* Barcode / QR Code Support

### Version 3

* Server Database
* Multi-User Support
* Roles and Permissions

## Inventory Principles

### Inventory Tracking

Inventory quantities are never edited directly.

Current stock levels are always derived from inventory movements.

## Project Status

Under Development

## License

Depot is released under the MIT License.

You are free to use, modify, distribute, and sublicense this software in accordance with the terms of the MIT License.

See the [LICENSE](LICENSE.md) file for the full license text.

