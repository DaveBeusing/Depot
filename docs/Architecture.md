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
- purchase-order lifecycle and delivery-note-based goods receipts;
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

Productive list and report paths are asynchronous and do not use unbounded synchronous `GetAll()` reads. Obsolete `GetAll()` APIs were removed. Small reference-data choices remain deliberately bounded or cached.

## Database providers

The application supports three provider implementations behind `IDatabaseConnectionFactory` and `IDatabaseInitializer`:

| Provider | Driver | Initialization and migrations | Locking strategy |
|---|---|---|---|
| SQLite | `Microsoft.Data.Sqlite` | `DepotDatabase` | immediate write transaction |
| Microsoft SQL Server | `Microsoft.Data.SqlClient` | `SqlServerDatabase` | serializable transaction and `UPDLOCK`/`HOLDLOCK` |
| MySQL/MariaDB | `MySqlConnector` | `MySqlDatabase` | serializable transaction and `FOR UPDATE` |

Repository SQL uses shared parameter conventions. Provider wrappers normalize parameter syntax, generated-ID queries, and case-insensitive comparison details. Connection failures are translated into explicit safe errors and logged without credentials.

SQLite is covered by integration tests. SQL Server and MySQL/MariaDB have provider-factory and SQL-normalization tests plus optional environment-configured procurement concurrency contracts. Broader live migration, maintenance, and multi-client verification remains a version 1.0 requirement.

## Schema and migrations

The current database schema version is **20**. It is independent from the application SemVer version.

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
- `ReasonCode`, whose immutable unique `Code` is separate from its editable display `Name`; seeded system codes are protected when required by active workflows
- `Warehouse`
- `StorageLocation`
- `SupplierCategory`
- `Supplier`
- `SupplierItem`

Master data uses activation/deactivation rather than hard deletion. Services perform validation and reference checks before deactivation.

### Inventory and movements

- `Inventory` represents an item, purpose, and storage-location context.
- The warehouse is derived through the storage location.
- `StockMovement` references `Inventory` and can reference an optional `ReasonCode`; repositories resolve workflow reasons through the technical code rather than the display name.
- Stock is derived from movements rather than stored as a mutable balance.

Posted movements are immutable. Schema version 20 adds an optional, unique `ReversalOfMovementId` self-reference plus reversal reason, timestamp, and user metadata. A reversal is a new movement with the negated quantity and the original document reference; the original row is neither updated nor deleted. The unique relationship prevents duplicate full reversals, and services reject reversal chains and reversal movements as cancellation targets.

### Procurement

- `PurchaseOrder` owns `PurchaseOrderLine` records.
- Status values are Draft, Ordered, PartiallyReceived, Received, and Cancelled.
- `GoodsReceipt` references exactly one purchase order and owns `GoodsReceiptLine` records.
- A goods receipt is a warehouse document with a supplier delivery-note number, receipt date, receiving user, notes, and destination inventory per line.
- Supplier invoices are intentionally not part of the goods-receipt domain. A separate `SupplierInvoice` entity can be introduced later without changing the receipt contract.

Legacy `InvoiceNumber`, `InvoiceDate`, and `InvoiceDocumentPath` database columns remain nullable for transition and backup compatibility. They are not exposed by the current domain model or UI and are not populated by new receipts. Version-17 migration preserves their existing values, assigns `LEGACY-GR-…` delivery-note numbers, and derives the receiving user from the original receipt audit entry where possible.

Purchase-order creation, draft editing, ordering, and cancellation commit their business change and before/after audit entry in one transaction. Goods-receipt posting remains atomic across the receipt, receipt lines, purchase-order received quantities, stock movements, purchase-order status, and receipt audit entry. Purchase-order locking prevents concurrent over-receipt.

Reversing a posted goods receipt creates counter-movements, reduces every affected purchase-order line's received quantity, recalculates the purchase-order status, marks the receipt as reversed, and writes its audit entry in one transaction. The workflow locks affected inventories and rejects a reversal that would produce negative stock.

### Stock transfers

Schema version 18 introduces `StockTransfer` and `StockTransferLine` for warehouse-to-warehouse transfers. Draft transfers validate distinct warehouses, matching source/destination items, inventory-to-warehouse assignments, positive quantities, and unique inventory pairs. Draft editing and cancellation use optimistic concurrency and commit their audit entry atomically.

Posting locks the transfer and all source/destination inventories in a stable order, validates aggregate source availability, resolves the immutable `TRANSFER` reason code, and creates a paired `TransferOut` and `TransferIn` movement for every line. Movements, Posted status, posting user, and audit entry share one provider-neutral transaction. The service prevents concurrent transfers from overdrawing a shared source.

Reversing a posted transfer counter-books both sides of every movement pair, retains the transfer number as the reference, and records the transfer's reversed metadata and audit entry atomically. The same stable inventory locking and aggregate stock validation protect the reverse direction.

The Transfers main page exposes a server-paged and server-searched transfer list, status filtering, draft editing, warehouse-filtered inventory selection, item-matched destination selection, stock availability, confirmed posting, cancellation, and the generated movement pair. ViewModels own presentation state and targeted list updates; all validation and posting rules remain in `StockTransferService`.

### Inventory counts

Schema version 19 introduces `InventoryCount` and `InventoryCountLine`. An audited draft belongs to one active warehouse and can be edited or cancelled with optimistic concurrency. Starting a count locks the draft and all active warehouse inventories, snapshots their current movement-derived quantities, creates one unique line per inventory, changes the status to Counting, and writes the audit entry in one provider-neutral transaction.

Counting updates use line-level optimistic concurrency and preserve `ExpectedQuantity`. A count can move to Review only after every line has a counted quantity and can return to Counting until it is posted. Posted counts are immutable through the service.

Posting a Review count locks the count and all referenced inventories in stable ID order, reloads the movement-derived current quantities, and creates only the required `Correction` movements with the `INVENTORY_CORRECTION` system reason code. `ExpectedQuantity` remains the historical start snapshot, but the posted correction is `CountedQuantity - current quantity at posting time`. This prevents stock movements between snapshot creation and posting from being corrected a second time. Correction movements, Posted status, posting user, completion time, and audit entry commit atomically; inventory rows are never updated directly.

A posted count can be reversed without altering its historical snapshot. Only its correction movements are counter-booked; the count receives reversal metadata and its audit entry in the same transaction. Counts that produced no correction movement can still be marked reversed atomically.

The Inventory Counts main page uses server-side search, warehouse/status filters, separate paging for count headers and positions, quick keyboard quantity entry, uncounted/difference filters, and targeted row updates. Recording a quantity loads only the locked count header and affected line; Review completeness is checked with a server-side aggregate rather than loading the complete snapshot.

### Procurement database roundtrips

The remote-database paths use bounded batch reads without introducing an application cache. Item validation for purchase-order lines is performed by one `GetByIdsAsync` query. Goods-receipt order lines are loaded together, while destination inventories are locked by one provider-specific, ID-sorted batch statement and then loaded by one batch query. The deterministic ID order avoids provider lock-order inversions. Paging and server-side search paths are unchanged.

The following command counts are the expected SQL roundtrips for successful 20-line workflows. Transaction begin and commit protocol messages are excluded; each insert with provider-specific identity retrieval counts as one command.

| Workflow | Before | After | Reduction |
| --- | ---: | ---: | ---: |
| Save a new draft purchase order with 20 lines | 47 | 26 | 45% |
| Post a goods receipt with 20 lines and 20 destination inventories | 108 | 69 | 36% |

The purchase-order reduction removes 19 item lookup commands and the two-command post-save reload. The goods-receipt reduction replaces 20 individual inventory locks plus 20 individual inventory reads with one sorted batch lock and one batch read, and combines the order header and its lines after locking. Per-line inserts and optimistic updates remain individual commands because their generated IDs and row-version checks are part of the existing behavioral contract.

## Audit and optimistic concurrency

Mutable entities use `Version` columns where optimistic concurrency is required. Updates include the expected version and throw `ConcurrencyConflictException` when a stale write loses the race.

Audit entries store timestamp, user identity, entity type, entity ID, action, and before/after JSON. Stock movements, purchase-order changes, goods receipts, transfers, inventory counts, and their reversals commit audit data with the corresponding transaction. An audit-log administration viewer has not yet been implemented.

Automated SQLite tests cover stale item updates, concurrent withdrawals, movement/audit atomicity, purchase-order audit commit and rollback, concurrent goods receipts, over-receipt rollback, and supplier/master-data reference rules. Equivalent live-server scenarios remain to be verified before version 1.0.

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

Items, inventory, movements, users, purchase orders, transfers, and inventory counts use bounded or paged server queries. Report summaries and groups are aggregated by the database; inventory-value exports read deterministic 500-row slices with cancellation and progress. Readiness for 100,000+ records remains an acceptance target because ClosedXML retains the generated workbook and live-server load tests are outstanding. See `DATA_ACCESS_AUDIT.md` for the path classification.
