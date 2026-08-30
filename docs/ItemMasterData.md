# Item master data

Updated: 2026-08-30

## Purpose

The item master is the authoritative mutable product/article description used by Depot workflows. `PartNumber` remains the immutable manufacturer part number (MPN); Depot does not introduce a duplicate manufacturer-part-number field.

The implementation follows the normal architecture:

```text
ItemsView
  -> ItemsViewModel / ItemEditorViewModel
  -> ItemService / ItemCostCalculationService
  -> ItemRepository / ItemCostRepository
  -> DatabaseAccess
  -> SQLite / SQL Server / MySQL or MariaDB
```

Business validation and permissions remain in the service layer. SQL, paging and row mapping remain in repositories.

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

## Standard Unit of Measure and Packaging reference data

New installations receive the following Unit of Measure defaults through the normal database initializer:

`EA` Each, `SET` Set, `PAIR` Pair, `M` Meter, `M2` Square Meter, `M3` Cubic Meter, `KG` Kilogram, `G` Gram, `L` Liter, `ML` Milliliter, `H` Hour and `DAY` Day.

`EA` is Depot's canonical built-in unit for one countable article. `PCS` is intentionally not added as a second equivalent system default.

Packaging defaults are `UNIT`, `BAG`, `BOX`, `CARTON`, `CASE`, `PACK`, `BUNDLE`, `TRAY`, `REEL`, `ROLL`, `CRATE` and `PALLET`.

The existing reference-data model has no separate code, system flag or sort-order field, so the code is stored in `Name` and the readable label in `Description`. Existing repository sorting by `Name`, then `Id`, remains authoritative. No schema column is added for this feature.

**Unit of Measure and Packaging are separate concepts.** UoM defines how item quantity is measured; Packaging describes the physical/logistical container or grouping. A cable can therefore use UoM `M` and Packaging `REEL`, while a screw can use UoM `EA` and Packaging `BOX`.

Packaging Types do not carry quantity or conversion semantics. `BOX` does not imply `100 EA`, `REEL` does not imply `305 M`, and `PALLET` does not imply a fixed number of cartons. Such relationships are item-specific and remain future `ItemPackaging` / `ItemUnitConversion` work.

Default seeding is provider-neutral and idempotent. A case-insensitive matching existing value is preserved exactly rather than renamed, reactivated, deactivated or otherwise overwritten. Custom reference data remains untouched. Technical seed creation is not treated as repeated user Audit activity; subsequent user maintenance continues through the existing RBAC/Audit services.

The Item Master continues to load active UoM and Packaging values from persistence. There are no hardcoded UI lists and no Packaging value is forced onto new items. `EA` is canonical, but it is not domain-forced onto cables, liquids, services or weight-based materials that require another UoM.

## Validation and integrity

`ItemService` normalizes and validates master data before persistence. Controls include valid/unique GTIN, country-code syntax, physical-value consistency, dangerous-goods classification, bounded strings, lifecycle-date ordering, valid replacement references, enum validation and optimistic concurrency.

Activation/deactivation reads the full item master before writing audit evidence, so the before/after audit payload retains extended master-data fields.

## Provider-neutral schema extension

`DatabaseProviderFactory` decorates the provider's normal database initializer with the item-master schema extension. It is additive and idempotent for SQLite, SQL Server and MySQL/MariaDB. Provider-specific unique-index syntax remains inside the data layer.

Standard UoM/Packaging initialization reuses the already existing `UnitsOfMeasure` and `Packagings` tables and therefore does **not** increment the core schema version.

Item Cost Build-up is owned by Sales feature schema 10 and adds provider-equivalent `ItemCostProfiles` and `ItemCostComponents`. This is an additive Sales feature migration and does not change the shared core schema version.

## Operational behavior

`ItemType`, `TrackingMode` and lifecycle fields are operational controls, not display-only metadata.

### Item type

Only `StockItem` records may participate in physical stock movements. Service/non-stock records are rejected by traceability-aware physical posting paths rather than silently creating inventory evidence.

### Tracking mode

`None` requires no serial/lot allocation. `SerialNumber` requires one unique serial code per moved unit with allocation quantity 1. `LotNumber` requires the complete movement quantity to be allocated across one or more lot codes.

Tracking identity and mutable quality state live in `ItemTrackingUnits`; signed movement allocations live in `StockMovementTracking`. Current tracked quantity/location is derived from those movements. See `ItemTraceability.md`.

### Lifecycle

Discontinued and obsolete items are blocked from new purchasing/sales decisions where lifecycle enforcement is invoked. Last-buy dates block purchasing after the configured date. End-of-life/end-of-support states can produce operational warnings, and a configured replacement item is included in the guidance rather than silently substituted.

Automatic substitution is intentionally not performed: changing the commercial or physical item identity must remain an explicit user/business decision.

## Supplier/commercial separation

Supplier part numbers, supplier assignment, preference, lead time and supplier-specific commercial data belong to `SupplierItems` / purchasing structures rather than the item master. Customer price lists and transactional pricing likewise remain in the Sales pricing model.

The first Item Cost Build-up source deliberately reuses the active preferred `SupplierItem.PurchasePrice`; Depot does not duplicate purchase price on `Item`. Because the current supplier-item model does not carry currency, `ItemCostProfile` explicitly states the ISO currency in which that preferred-supplier purchase price is to be interpreted. A missing profile, missing/ambiguous preferred supplier or currency mismatch is an error; Depot never assumes zero cost or a 1:1 FX rate.

## Item Cost Build-up

An existing item can have an `ItemCostProfile` and ordered `ItemCostComponent` records. The profile defines the Base Cost source and currency. Components support:

- `Absolute` — adds the configured monetary value;
- `Percentage` with `BaseCost` — applies the percentage only to the original Base Cost;
- `Percentage` with `RunningTotal` — applies the percentage to the subtotal produced by all prior effective components.

Components are evaluated by `Sequence`, then persisted component `Id` as the stable secondary key. Inactive, not-yet-valid and expired components are ignored for the effective calculation date. Negative component values are rejected. The central `ItemCostCalculationService` returns the Base Cost, effective component evidence, calculated cost, currency and calculation date; Views/ViewModels do not duplicate the formula.

Example:

```text
Base Cost               1,000.00 EUR
10 Freight       +         50.00
20 Customs 4%    +         40.00  (BaseCost)
30 Handling      +         15.00
40 Overhead 3%   +         33.15  (RunningTotal)
--------------------------------------
Calculated Cost         1,138.15 EUR
```

Item-cost inspection reuses `Items.View`; changing the cost profile/components requires existing Item edit/manage permissions. Mutations use optimistic concurrency and the established Audit/transaction infrastructure.

## Testing

Regression coverage protects provider-initializer idempotency, complete repository round-trip, GTIN uniqueness, traceability schema/index creation, capture parsing and ambiguity rejection, physical item-type restrictions and lifecycle purchase/sales policy behavior. Standard reference-data tests additionally cover all 12 UoMs and all 12 Packaging Types, repeated initialization, existing matching values, case variants, inactive/custom value preservation, absence of built-in `PCS`, and optional SQL Server/MySQL provider execution. Item-cost tests additionally cover Base Cost only, Absolute and Percentage components, `BaseCost` versus `RunningTotal`, mixed/deterministic sequencing, effective dates/activity, decimal rounding, invalid values, missing Base Cost, currency mismatch, cancellation and optimistic concurrency. Transactional workflow and reversal suites continue to protect the owning stock/document operations.
