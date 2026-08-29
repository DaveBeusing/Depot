# Item master data

Updated: 2026-08-29

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

Business validation, RBAC, transactions and Audit remain in services. SQL and row mapping remain in repositories.

## Master-data groups

Identity/classification includes part number, description, manufacturer, category, unit, packaging, GTIN, item type, model, revision and product family. Lifecycle includes lifecycle status and product dates plus optional replacement item. Trade/compliance includes origin, tariff number, ECCN, RoHS/REACH, dangerous-goods/UN and battery state. Traceability/logistics includes tracking mode, weight in kg, dimensions in mm and notes.

## Cost build-up

The item master now also owns an optional `ItemCostProfile` and ordered `ItemCostComponents`. These records configure the derived commercial cost of an item; the calculated result is not persisted as a second uncontrolled item price.

The first `BaseCostSource` is the active preferred `SupplierItem.PurchasePrice`. Supplier commercial data remains in `SupplierItems`; the cost profile only states which supported source is used and the source currency. An explicit currency is required because existing supplier prices do not store currency metadata.

Cost Components support:

- `Absolute` — adds the configured monetary value;
- `Percentage` with `BaseCost` — percentage of the original Base Cost;
- `Percentage` with `RunningTotal` — percentage of the subtotal immediately before that component.

Only active components valid on the effective date participate. Ordering is `Sequence`, then component `Id` as a stable secondary key. Negative values and invalid validity windows are rejected. Percentage-specific calculation-base controls are not editable for Absolute components.

The central `ItemCostCalculationService` is the only calculation implementation. Bulk Sales pricing calls this service instead of reproducing the formula in a ViewModel or another pricing service. Full semantics are documented in `ITEM_COSTING_AND_BULK_PRICING.md`.

## Validation and integrity

`ItemService` continues to normalize and validate core master data. `ItemCostCalculationService` validates cost profiles/components, service-layer permissions and optimistic concurrency. Cost changes use the existing Audit infrastructure.

If no active preferred supplier price exists, if multiple preferred rows create ambiguity, or if a requested target currency differs from the explicit cost currency, calculation fails closed. Depot never substitutes zero or silently assumes currency conversion.

## Provider-neutral schema

The existing core item-master extension remains unchanged. Sales feature schema 10 adds `ItemCostProfiles` and `ItemCostComponents` because the new cost build-up feeds the Sales pricing domain. Provider-specific DDL is isolated in the data layer for SQLite, SQL Server and MySQL/MariaDB.

## Operational behavior

`ItemType`, `TrackingMode` and lifecycle fields remain operational controls. Only stock items may participate in physical movements; tracked items use the existing serial/lot allocation model; discontinued/obsolete lifecycle policies remain unchanged.

Cost build-up does not mutate inventory valuation, FIFO layers or historical purchase/sales documents. It is a commercial calculation based on current effective master data.

## Supplier and Sales separation

Supplier part numbers, supplier assignment, preference, lead time and purchase price remain in `SupplierItems`. `ItemCostProfile` references that commercial meaning without copying the purchase price. Customer price lists and transactional pricing remain in the Sales model.

Bulk Price generation consumes Calculated Item Cost and writes only through the existing scoped PriceList model. Customer → Region → Global resolution therefore remains unchanged.

## Testing

Existing item-master, GTIN, lifecycle and traceability regression coverage remains in force. Item-cost regression tests additionally cover Base Cost only, absolute/percentage components, BaseCost/RunningTotal semantics, deterministic ordering, activity/validity, invalid values, currency mismatch, cancellation and optimistic evidence checks used by Bulk Pricing. Optional live-provider fixtures verify the item-cost schema on SQL Server and MySQL/MariaDB.
