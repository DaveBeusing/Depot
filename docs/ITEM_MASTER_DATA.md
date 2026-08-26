# Item master data

Updated: 2026-08-25

## Purpose

The item master is the authoritative mutable product/article description used by Depot workflows. `PartNumber` remains the immutable manufacturer part number (MPN); Depot does not introduce a duplicate manufacturer-part-number field.

The implementation follows the normal architecture:

```text
ItemsView
  -> ItemsViewModel / ItemEditorViewModel
  -> ItemService
  -> ItemRepository
  -> DatabaseAccess
  -> SQLite / SQL Server / MySQL or MariaDB
```

Business validation and permissions remain in `ItemService`. SQL, paging and row mapping remain in `ItemRepository`.

## Master-data groups

### Identity and classification

- part number / MPN and description
- manufacturer, category, unit of measure and packaging
- GTIN
- item type
- model, revision and product family

### Lifecycle

- lifecycle status
- introduction date
- end-of-life date
- last-buy date
- end-of-support date
- optional replacement item

### Trade and compliance

- country of origin
- customs tariff number
- ECCN
- RoHS status
- REACH status
- dangerous-goods flag and UN number
- battery indication

### Traceability and logistics

- tracking mode: none, serial number or lot
- net and gross weight in kilograms
- length, width and height in millimetres
- internal notes

The physical-unit contract is fixed: **kg for weight, mm for dimensions**.

## Validation and integrity

`ItemService` normalizes and validates master data before persistence. Controls include valid/unique GTIN, country-code syntax, physical-value consistency, dangerous-goods classification, bounded strings, lifecycle-date ordering, valid replacement references, enum validation and optimistic concurrency.

Activation/deactivation reads the full item master before writing audit evidence, so the before/after audit payload retains extended master-data fields.

## Provider-neutral schema extension

`DatabaseProviderFactory` decorates the provider's normal database initializer with the item-master schema extension. It is additive and idempotent for SQLite, SQL Server and MySQL/MariaDB. Provider-specific unique-index syntax remains inside the data layer.

## Operational behavior

`ItemType`, `TrackingMode` and lifecycle fields are operational controls, not display-only metadata.

### Item type

Only `StockItem` records may participate in physical stock movements. Service/non-stock records are rejected by traceability-aware physical posting paths rather than silently creating inventory evidence.

### Tracking mode

`None` requires no serial/lot allocation. `SerialNumber` requires one unique serial code per moved unit with allocation quantity 1. `LotNumber` requires the complete movement quantity to be allocated across one or more lot codes.

Tracking identity and mutable quality state live in `ItemTrackingUnits`; signed movement allocations live in `StockMovementTracking`. Current tracked quantity/location is derived from those movements. See `ITEM_TRACEABILITY.md`.

### Lifecycle

Discontinued and obsolete items are blocked from new purchasing/sales decisions where lifecycle enforcement is invoked. Last-buy dates block purchasing after the configured date. End-of-life/end-of-support states can produce operational warnings, and a configured replacement item is included in the guidance rather than silently substituted.

Automatic substitution is intentionally not performed: changing the commercial or physical item identity must remain an explicit user/business decision.

## Supplier/commercial separation

Supplier part numbers, supplier assignment, preference, lead time and supplier-specific commercial data belong to `SupplierItems` / purchasing structures rather than the item master. Customer price lists and transactional pricing likewise remain in the Sales pricing model.

## Testing

Regression coverage protects provider-initializer idempotency, complete repository round-trip, GTIN uniqueness, traceability schema/index creation, capture parsing and ambiguity rejection, physical item-type restrictions and lifecycle purchase/sales policy behavior. Transactional workflow and reversal suites continue to protect the owning stock/document operations.
