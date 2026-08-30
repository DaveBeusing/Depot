# Standard Units of Measure and Packaging Types

Updated: 2026-08-30

## Purpose

New Depot installations receive a compact, international and industry-neutral reference-data baseline for Units of Measure and Packaging. The defaults are inserted through the normal provider initializer and remain ordinary reference data used by the existing Item Master UI and services.

No parallel reference-data model is introduced. Depot continues to use the existing `UnitsOfMeasure` and `Packagings` tables with `Id`, `Name`, `Description`, `IsActive` and `Version`. Because these tables currently have no separate code, system/default flag or sort order, the standard code is stored in `Name` and the human-readable label in `Description`.

This work does not change the database schema. Core database schema remains version 30.

## Standard Units of Measure

| Code | Description | Meaning |
| --- | --- | --- |
| EA | Each | Count |
| SET | Set | Count |
| PAIR | Pair | Count |
| M | Meter | Length |
| M2 | Square Meter | Area |
| M3 | Cubic Meter | Volume |
| KG | Kilogram | Weight |
| G | Gram | Weight |
| L | Liter | Liquid volume |
| ML | Milliliter | Liquid volume |
| H | Hour | Time |
| DAY | Day | Time |

`EA` is Depot's canonical built-in unit for a single countable article. Depot does not seed `PCS`, `PC` or `Piece` as equivalent defaults. Users may create additional Units of Measure through the existing reference-data administration when required.

## Standard Packaging Types

| Code | Description |
| --- | --- |
| UNIT | Unit |
| BAG | Bag |
| BOX | Box |
| CARTON | Carton |
| CASE | Case |
| PACK | Pack |
| BUNDLE | Bundle |
| TRAY | Tray |
| REEL | Reel |
| ROLL | Roll |
| CRATE | Crate |
| PALLET | Pallet |

## UoM and Packaging are different concepts

A Unit of Measure answers **how the item's quantity is measured**. Packaging answers **how the item is physically or logistically packaged**.

Examples:

```text
CAT6 Cable
Unit of Measure: M
Packaging: REEL
```

```text
M4 Screw
Unit of Measure: EA
Packaging: BOX
```

Packaging does not imply quantity. `BOX` does not mean `100 EA`, `REEL` does not mean `305 M`, and `PALLET` does not mean a fixed number of cartons. Item-specific packaging quantities and unit conversions are deliberately outside this work package and belong in a future item-level structure such as `ItemPackaging` or `ItemUnitConversion`.

## Initialization and migration behavior

`DatabaseProviderFactory` runs the standard reference-data seed after the normal provider schema initializer and item feature-schema initialization. The seeder uses the existing database connection/transaction abstractions and the same logic for SQLite, SQL Server and MySQL/MariaDB.

Seeding is idempotent. Before inserting a standard value Depot performs a case-insensitive lookup on the existing natural name key. If a matching value already exists, Depot leaves it completely unchanged. This includes its description, active/inactive state and version. Existing custom values are never renamed, activated, deactivated or deleted by the seed.

This means an existing custom `EA` or `BOX` is treated as authoritative for that database and no second equivalent default is created. Initialization itself is technical bootstrap data and does not generate user-change Audit records. Later user changes continue through the existing reference-data services and therefore retain RBAC, optimistic concurrency and Audit behavior.

## Item Master integration

The Item Master already loads active Units of Measure and Packaging from their repositories/services. The newly seeded values therefore appear automatically in the existing dropdowns without hardcoded UI lists.

Depot does not force a Packaging value onto new items. `EA` is the canonical piece-unit reference, but the domain does not automatically assign it to every new item because cables, liquids, services and weight-based materials can require a different Unit of Measure. Users select the appropriate persisted value in the existing Item editor.

## Sorting

The existing reference-data repositories sort active values by `Name`, then `Id`. No dedicated `SortOrder` column is added only for these defaults, so the established repository sorting remains authoritative.
