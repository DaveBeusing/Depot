# 📦 Depot

Depot is a desktop application for managing local inventory and assets.

## Vision

The goal is to replace spreadsheet-based inventory tracking with a reliable system that provides:

- Accurate stock management
- Complete inventory movement history
- Stock valuation
- Multi-location support
- Future multi-user operation
- Migration path from local to server-based deployment

## Technology Stack

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-10-purple)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Database](https://img.shields.io/badge/Database-SQLite-green)
![Architecture](https://img.shields.io/badge/Architecture-MVVM-orange)
![License](https://img.shields.io/badge/License-MIT-yellow)

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

Built with ❤️ using .NET, WPF and SQLite

