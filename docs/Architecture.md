# Depot Architecture

## Overview

Depot is a .NET 10 WPF application using strict MVVM and a layered persistence architecture.

```text
Presentation
  Views
    |
  ViewModels
    |
Business
  Services
    |
Persistence
  Repositories
    |
  DatabaseAccess
    |
  SQLite / Microsoft SQL Server / MySQL or MariaDB
```

`App.xaml.cs` is the composition root. It creates provider infrastructure, repositories, services, and the root ViewModels.

## Presentation layer

The presentation layer contains WPF views, ViewModels, commands, converters, reusable controls, and resource dictionaries.

- Views contain layout and bindings only.
- ViewModels expose state, commands, loading/error feedback, and cancellable UI workflows.
- ViewModels call services and never contain SQL.
- Native file selection and confirmation dialogs are accessed through `IFileDialogService`.
- Shared UI values and styles live in `Resources/`; repeated behavior belongs in reusable controls.

The shell currently contains Dashboard, Inventory, Items, Movements, Procurement, Reports, and role-restricted Administration modules.

Administration contains Import, Master Data, Users, Database, Settings, and About. General Settings remains a placeholder; database/provider/backup settings are implemented in the Database module.

## Business layer

Services own validation and application workflows, including:

- item and normalized item-reference data management;
- inventory, stock calculation, movements, and valuation;
- warehouse and storage-location management;
- supplier and supplier-item management;
- purchase-order lifecycle and invoice-backed goods receipts;
- authentication, authorization, sessions, and password hashing;
- import, reporting, audit creation, settings, and database administration.

Services do not reference ViewModels or WPF controls.

## Persistence layer

Repositories own SQL, data mapping, and persistence operations. They use the shared `DatabaseAccess` abstraction rather than opening provider connections directly.

`DatabaseAccess` provides:

- synchronous compatibility methods still used by legacy paths;
- asynchronous query and command execution;
- cancellation-token propagation;
- server-side page and slice queries;
- asynchronous streaming;
- provider-controlled write transactions;
- transient write-conflict retries;
- provider-specific locking statements.

The intended direction is fully asynchronous access. That migration is incomplete because some services and reports still use synchronous `GetAll()` calls or fully materialized collections.

## Database providers

The application supports three provider implementations behind `IDatabaseConnectionFactory` and `IDatabaseInitializer`:

| Provider | Driver | Initialization and migrations | Locking strategy |
|---|---|---|---|
| SQLite | `Microsoft.Data.Sqlite` | `DepotDatabase` | immediate write transaction |
| Microsoft SQL Server | `Microsoft.Data.SqlClient` | `SqlServerDatabase` | serializable transaction and `UPDLOCK`/`HOLDLOCK` |
| MySQL/MariaDB | `MySqlConnector` | `MySqlDatabase` | serializable transaction and `FOR UPDATE` |

Repository SQL uses shared parameter conventions. Provider wrappers normalize parameter syntax, generated-ID queries, and case-insensitive comparison details. Connection failures are translated into explicit safe errors and logged without credentials.

SQLite is covered by integration tests. SQL Server and MySQL/MariaDB have provider-factory and SQL-normalization tests, but the repository does not yet contain live-server integration suites. Live migration, maintenance, and concurrency verification remains a version 1.0 requirement.

## Schema and migrations

The current database schema version is **15**. It is independent from the application SemVer version.

All providers create the current schema and migrate supported older schemas forward. Migrations cover authentication, inventory-based movements, audit/concurrency, warehouse structure, reason codes, normalized item master data, suppliers, supplier categories, supplier items, purchase orders, and goods receipts.

The application refuses unsupported newer schemas and reports provider-specific migration failures through the shared error layer.

## Domain model

### Master data

- `Item`
- `Manufacturer`
- `Category`
- `UnitOfMeasure`
- `Packaging`
- `Purpose`
- `ReasonCode`
- `Warehouse`
- `StorageLocation`
- `SupplierCategory`
- `Supplier`
- `SupplierItem`

Master data uses activation/deactivation rather than hard deletion. Services perform validation and reference checks before deactivation.

### Inventory and movements

- `Inventory` represents an item, purpose, and storage-location context.
- The warehouse is derived through the storage location.
- `StockMovement` references `Inventory` and can reference an optional `ReasonCode`.
- Stock is derived from movements rather than stored as a mutable balance.

### Procurement

- `PurchaseOrder` owns `PurchaseOrderLine` records.
- Status values are Draft, Ordered, PartiallyReceived, Received, and Cancelled.
- `GoodsReceipt` references exactly one purchase order and owns `GoodsReceiptLine` records.
- A goods receipt requires invoice metadata and an existing invoice-document path.

Goods-receipt posting is atomic: the receipt, receipt lines, purchase-order received quantities, stock movements, purchase-order status, and receipt audit entry commit in one transaction. Purchase-order locking prevents concurrent over-receipt.

## Audit and optimistic concurrency

Mutable entities use `Version` columns where optimistic concurrency is required. Updates include the expected version and throw `ConcurrencyConflictException` when a stale write loses the race.

Audit entries store timestamp, user identity, entity type, entity ID, action, and before/after JSON. Stock movements and goods-receipt audit data are committed with their corresponding transaction. An audit-log administration viewer has not yet been implemented.

Automated SQLite tests cover stale item updates, concurrent withdrawals, movement/audit atomicity, concurrent goods receipts, over-receipt rollback, and supplier/master-data reference rules. Equivalent live-server scenarios remain to be verified before version 1.0.

## Database administration

`DatabaseManagementService` provides:

- provider, schema, connection, size, and last-backup overview;
- portable archive backup and validation;
- restore with pre-validation and automatic safety backup;
- persistent scheduled backups;
- provider-specific integrity checks;
- SQLite `VACUUM` compaction.

SQLite maintenance and backup/restore are integration-tested. Live SQL Server and MySQL/MariaDB recovery testing is still required.

## Loading and large-data behavior

Implemented infrastructure includes asynchronous commands, cancellation, shared loading/error states, debounced server-side search, page queries, slice queries, caching for selected reference data, and streaming APIs.

Items, inventory, movements, users, and purchase orders use bounded or paged server queries in their main list paths. Some older stock, movement, purpose, user, and report operations still use synchronous full-table reads. Therefore readiness for 100,000+ records is a target under active work, not a verified capability.
