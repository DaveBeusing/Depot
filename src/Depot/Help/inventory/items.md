# Items

## Summary
Items are Depot's reusable article master records. In addition to part number, description and reference data, the item master can hold identification, lifecycle, trade/compliance and logistics attributes used to describe a product consistently across purchasing, inventory, warehouse and sales workflows.

## Prerequisites
- Reference data required by the item is active.
- You have permission to view items; create or edit operations require the matching item permissions.

## Core master data
The item editor supports:

- immutable part number (manufacturer part number / MPN) and description
- manufacturer, category, unit of measure and packaging
- GTIN with GTIN-8/12/13/14 check-digit validation; one GTIN can be assigned to only one item
- item type, lifecycle status, model, revision and product family
- intended tracking mode (`None`, `SerialNumber`, or `Lot`)
- country of origin using two-letter ISO alpha-2 syntax, customs tariff number and ECCN
- RoHS and REACH status
- dangerous-goods flag, UN number and battery indication
- net and gross weight in **kg** and dimensions in **mm**
- introduction, end-of-life, last-buy and end-of-support dates
- an optional active replacement item and internal notes

> [!NOTE] Tracking mode describes the intended traceability requirement in item master data. Serial-number and lot capture/enforcement in stock-movement workflows is a separate operational capability and must not be inferred from this field alone.

> [!NOTE] Item type and lifecycle status are master-data classifications. Workflows continue to enforce their existing transaction rules unless a workflow explicitly states that it evaluates these classifications.

## Validation rules
Depot rejects inconsistent master data before saving. Examples include invalid GTIN checksums, duplicate GTINs, malformed country codes or UN numbers, negative dimensions/weights, gross weight below net weight, dangerous goods without a UN number, invalid lifecycle date ordering, self-replacement, or replacement by an inactive/missing item.

The replacement selector displays active item part numbers; **Clear** removes an existing replacement link.

## Steps
1. Open **Inventory > Items**, or use **Ctrl+Shift+P** and run **New Item** when you want to create a record directly.
2. Search by part number, description, GTIN, model, revision, product family, origin, tariff number, ECCN, UN number, manufacturer, category or other indexed master-data text. **Ctrl+P** can also locate items globally through Quick Open.
3. Select an item to inspect it, or choose **New item** when permitted.
4. Complete the required and relevant master-data fields and save.

## Result
The item is available to workflows that reference item records. Existing items are deactivated instead of deleted. Opening an item from Quick Open also adds it to the session's recent records.

## Unsaved changes
Depot detects changes in the active item editor. If you switch workspace or section, close the tab, sign out, or close Depot before saving, you are asked whether to discard the changes.

> [!NOTE] Choosing **Discard changes** restores the last loaded or saved item state before navigation continues.

## Common problems
- An inactive reference value cannot be assigned to a new item.
- A part number must remain unique.
- GTIN must be valid and unique across items.
- Dangerous-goods records require a UN number in `UN1234` form.
- A replacement item must exist, be active, and cannot reference the item itself.
- Quick Open record search starts after at least two entered characters; recent records are available with an empty search field.

## Required permissions
`Items.View`; changes additionally require `Items.Create` or `Items.Edit`. Activation/deactivation requires the existing item-management permission.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Inventory Overview](topic:inventory.overview)
- [Purchase Orders](topic:purchasing.purchase-orders)
