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

These are product/item attributes. They are intentionally separate from Company legal-entity master data and from supplier-specific commercial data.

### Traceability and logistics

- intended tracking mode: none, serial number or lot
- net and gross weight in kilograms
- length, width and height in millimetres
- internal notes

The physical-unit contract is fixed: **kg for weight, mm for dimensions**. Persisted property/column names make that contract explicit.

## Validation and integrity

`ItemService` normalizes and validates master data before persistence. Current controls include:

- valid GTIN-8/12/13/14 checksum;
- GTIN uniqueness checked by the service and protected by a unique database index for race safety;
- two-letter uppercase country-code syntax;
- non-negative weights and dimensions;
- gross weight not lower than net weight;
- dangerous goods requiring `UN1234`-style classification;
- bounded string lengths for revision/model/product family/trade fields/notes;
- lifecycle dates that cannot move backwards relative to their prerequisite dates;
- replacement item must exist, be active and cannot reference itself;
- enum values are validated before persistence;
- optimistic concurrency remains enforced by item `Version`.

Activation/deactivation reads the full item master before writing audit evidence, so the before/after audit payload does not lose extended master-data fields.

## Provider-neutral schema extension

`DatabaseProviderFactory` decorates the provider's normal database initializer with `ItemMasterDataSchema.Ensure`. The additive schema extension is idempotent and has explicit SQLite, SQL Server and MySQL/MariaDB column definitions.

The current branch previously introduced unit-ambiguous physical columns. The schema extension copies those values into the explicit `*Kg` / `*Mm` columns when those legacy branch columns are present. The old columns are not used by the current repository contract.

GTIN uniqueness is created provider-specifically:

- SQLite: filtered unique index for non-null GTIN values;
- SQL Server: filtered unique index for non-null GTIN values;
- MySQL/MariaDB: nullable unique index (multiple null values remain supported by the provider).

## Current operational boundary

Some master-data values describe intent but do not by themselves alter every transaction workflow:

- `TrackingMode` does not yet create or enforce serial/lot capture on receipts, issues, transfers or shipments.
- `ItemType` does not yet universally suppress physical stock workflows for service/non-stock records.
- `LifecycleStatus` and lifecycle dates do not yet automatically block purchasing/sales or substitute the replacement item.

Those behaviors require explicit cross-workflow business rules and dedicated transaction/schema support. They must be implemented as separate features rather than inferred silently from the master record.

## Supplier/commercial separation

Supplier part numbers, supplier assignment, preference, lead time and supplier-specific commercial data belong to `SupplierItems` / purchasing structures rather than the item master. Customer price lists and transactional pricing likewise remain in the Sales pricing model.

## Testing

Item master regression coverage includes provider-initializer idempotency, complete repository round-trip and GTIN uniqueness at the database boundary. Existing repository/provider tests continue to verify the shared data-access architecture.
