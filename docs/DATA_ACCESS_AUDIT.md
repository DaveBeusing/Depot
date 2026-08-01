# Depot Data-Access Audit

This audit records the productive read paths after removal of unbounded remote-database `GetAll()` usage. The obsolete repository and service `GetAll()` APIs were removed; legacy synchronous write adapters use only key-based reads.

## Classification

| Area | Category | Implemented decision |
| --- | --- | --- |
| Inventory-value report UI | 1. Paging | Server-side pages of 100 rows with deterministic `PartNumber`, context, and `InventoryId` ordering. Search remains debounced and server-side. |
| Grouped reports | 3. Aggregation | Warehouse, storage location, purpose, category, and manufacturer grouping, distinct counts, quantities, and values are calculated by the database. |
| Inventory-value Excel export | 4. Streaming/page slices | Reads deterministic slices of 500 rows and writes them incrementally to the workbook. Progress and cancellation are reported between slices; no complete result collection is created. |
| Items, Inventory, StockMovements, PurchaseOrders, Transfers, InventoryCounts | 1 and 2 | Existing server-side paging and debounced search remain the productive list paths. Record updates continue to replace only affected rows. |
| Inventory-count lines | 1 and 2 | Count positions use count-scoped server paging and filters. Complete line loading remains limited to atomic posting transactions where every line is a required business input. |
| Goods receipts | scoped child query | Receipts are loaded only for one selected purchase order. Their lines are batch-loaded with one query instead of one query per receipt. |
| Supplier administration and SupplierItem | 2. Search | Results are server-filtered, deterministically ordered, and capped at 200 rows. Users refine the search instead of loading the tables completely. |
| Procurement supplier and item choices | 2. Search | Debounced server-side searches return at most 50 active suppliers or items. Existing selected suppliers are loaded individually when required. |
| Transfer inventory choices | bounded selection | Warehouse-scoped inventory options are deterministically ordered and capped at 200 rows. They never load the complete Inventory table. |
| Goods-receipt inventory choices | bounded selection | Item-scoped active destinations are deterministically ordered and capped at 100 rows. |
| Purpose, ReasonCode, Warehouse, StorageLocation, Manufacturer, Category, UnitOfMeasure, Packaging, SupplierCategory | 5. Deliberately retained | These are small administrative reference sets. Active choices are cached where supported and always use stable name ordering. Their management screens use server-side search where available. |
| Audit | no productive read list | Audit entries are transactionally written. There is no audit viewer or full-table read path yet. A future viewer must start with server paging and filters. |
| Import compatibility | targeted access | Import resolves items and master data by key and writes rows individually. It has no productive Item, Inventory, StockMovement, or Audit `GetAll()` path. |

## Invariants

- Page and slice queries always include a unique final sort key.
- Long reads accept and propagate `CancellationToken`.
- Search initiated by text input uses a 300 ms debounce.
- Report totals are independent database aggregates and are not calculated from a partial UI page.
- Export memory is bounded with respect to database materialization. ClosedXML still owns the generated workbook in memory, which is a library constraint and must be considered in very large export acceptance tests.
- SQL Server and MySQL/MariaDB live performance and query-plan verification remains required before version 1.0.
