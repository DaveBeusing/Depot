# Items

## Summary
Items are Depot's reusable article master records. In addition to part number, description and reference data, the item master can hold identification, lifecycle, trade/compliance, logistics and cost-build-up attributes used across purchasing, inventory, warehouse and sales workflows.

## Prerequisites
- Reference data required by the item is active.
- You have permission to view items; create or edit operations require the matching item permissions.

## Core master data
The item editor supports immutable part number and description; manufacturer, category, unit and packaging; GTIN; item type and lifecycle; model, revision and product family; tracking mode; origin, tariff number and ECCN; RoHS/REACH; dangerous-goods/battery data; weights and dimensions; lifecycle dates; replacement item and notes.

Tracking mode describes the intended traceability requirement. Serial/lot capture and enforcement in movements remains a separate operational capability.

## Standard units and packaging
New Depot databases include a standard reference-data baseline. Units of Measure are `EA`, `SET`, `PAIR`, `M`, `M2`, `M3`, `KG`, `G`, `L`, `ML`, `H` and `DAY`. `EA` means **Each** and is Depot's canonical built-in piece unit; `PCS` is intentionally not created as a second equivalent default.

Standard Packaging Types are `UNIT`, `BAG`, `BOX`, `CARTON`, `CASE`, `PACK`, `BUNDLE`, `TRAY`, `REEL`, `ROLL`, `CRATE` and `PALLET`.

Unit of Measure and Packaging are not interchangeable. UoM says how quantity is measured; Packaging says how an item is physically/logistically packaged. For example, cable may use `M` + `REEL`, while screws may use `EA` + `BOX`.

Packaging does not imply a quantity. A `BOX` does not automatically mean 100 each and a `REEL` does not automatically mean 305 metres. Item-specific packaging quantities and unit conversions are separate future functionality.

The Item editor loads these values from persisted reference data. Existing matching/custom values are preserved by initialization, and no hardcoded UI list is used. Packaging is never automatically assigned to new items.

## Cost build-up
For an existing item, **Cost build-up** provides the purchasing-cost calculation used by Bulk Pricing.

The current Base Cost source is the active preferred supplier purchase price. Supplier prices created before this feature do not contain currency metadata, so set the item's three-letter ISO **Cost currency** explicitly. Depot does not silently assume EUR or an FX rate.

Add Cost Components with:

- **Name** — for example Freight, Customs, Handling or Overhead.
- **Sequence** — determines calculation order.
- **Type** — `Absolute` or `Percentage`.
- **Percentage base** — `BaseCost` or `RunningTotal`; this option only applies to Percentage components.
- **Value** — monetary amount for Absolute, percentage points for Percentage.
- optional **Valid from / Valid until** dates and **Active** state.

`BaseCost` percentage always uses the original Base Cost. `RunningTotal` percentage uses the subtotal produced by earlier components. Equal Sequence values remain deterministic using the persisted component identity as a secondary order key.

The editor shows Base Cost and Calculated Cost. If Base Cost is missing/ambiguous or currency cannot be validated, Depot reports the reason instead of using zero.

## Validation rules
Depot rejects inconsistent master data before saving. Examples include invalid GTIN checksums, duplicate GTINs, malformed country codes or UN numbers, negative dimensions/weights, gross weight below net weight, dangerous goods without a UN number, invalid lifecycle date ordering, self-replacement, or replacement by an inactive/missing item.

Cost Components reject negative values, invalid validity ranges and invalid calculation types/bases. Item cost profile currency must be a three-letter code.

## Steps
1. Open **Inventory > Items**, or use **Ctrl+Shift+P** and run **New Item** when you want to create a record directly.
2. Search by part number, description, GTIN, model, revision, product family, origin, tariff number, ECCN, UN number, manufacturer, category or other indexed master-data text.
3. Select an item to inspect it, or choose **New item** when permitted.
4. Complete and save the core master-data fields, selecting the appropriate persisted Unit of Measure and Packaging where applicable.
5. For an existing item that participates in calculated pricing, set Cost currency and manage Cost Components in **Cost build-up**.

## Result
The item is available to workflows that reference item records. Existing items are deactivated instead of deleted. Calculated cost remains a derived value; changing Cost Components does not rewrite historical business documents.

## Unsaved changes
Depot detects changes in the active item editor. If you switch workspace or section, close the tab, sign out, or close Depot before saving, you are asked whether to discard the changes.

## Common problems
- An inactive reference value cannot be assigned to a new item.
- A part number must remain unique.
- GTIN must be valid and unique across items.
- Dangerous-goods records require a UN number in `UN1234` form.
- A replacement item must exist, be active, and cannot reference the item itself.
- Calculated Cost requires exactly one active preferred supplier purchase price and an explicit cost currency.
- A Cost currency different from the target PriceList currency cannot be bulk-priced until controlled FX conversion exists.
- Do not use Packaging Types as quantity conversions; Packaging only describes the container/grouping.

## Required permissions
`Items.View`; core item changes additionally require `Items.Create` or `Items.Edit`. Cost Component maintenance uses existing item edit/manage permissions. Activation/deactivation uses the existing item-management permission.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Inventory Overview](topic:inventory.overview)
- [Sales Pricing](topic:sales.pricing)
- [Purchase Orders](topic:purchasing.purchase-orders)
