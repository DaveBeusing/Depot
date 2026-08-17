# Items

## Summary
Items define reusable article data such as part number, description, manufacturer, category, unit of measure, and packaging.

## Prerequisites
- Reference data required by the item is active.
- You have permission to view items; create or edit operations require the matching item permissions.

## Steps
1. Open **Inventory > Items**, or use **Ctrl+Shift+P** and run **New Item** when you want to create a record directly.
2. Search by part number or description. **Ctrl+P** can also locate items globally through Quick Open.
3. Select an item to inspect it, or choose **New item** when permitted.
4. Complete required fields and save.

## Result
The item is available to inventory and purchasing workflows. Existing items are deactivated instead of deleted. Opening an item from Quick Open also adds it to the session's recent records.

## Unsaved changes
Depot detects changes in the active item editor. If you switch workspace or section, close the tab, sign out, or close Depot before saving, you are asked whether to discard the changes.

> [!NOTE] Choosing **Discard changes** restores the last loaded or saved item state before navigation continues.

## Common problems
- An inactive reference value cannot be assigned to a new item.
- A part number must remain unique.
- Quick Open record search starts after at least two entered characters; recent records are available with an empty search field.

## Required permissions
`Items.View`; changes additionally require `Items.Create` or `Items.Edit`.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Inventory Overview](topic:inventory.overview)
- [Purchase Orders](topic:purchasing.purchase-orders)
