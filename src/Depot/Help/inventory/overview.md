# Inventory Overview

## Summary
The inventory overview shows stock by item and storage location, including its warehouse and recent movement activity. It also supports user-specific **Saved Views** so recurring filters and grid layouts can be restored without rebuilding the workspace after every sign-in.

## Prerequisites
- Items, warehouses, and storage locations exist.
- Inventory records have been created by an authorized workflow or import.
- Saved Views require an active signed-in user; saved presentation state never grants access to inventory data.

## Steps
1. Open **Inventory > Overview**.
2. Search or filter the server-side list.
3. Adjust supported column order, width, visibility, sorting or grid density as needed.
4. Use **Saved views** to save the current supported presentation state for this workspace.
5. Optionally mark one saved view as the default for your user/workspace.
6. Select a saved view to reapply its filter and grid presentation state, or use **Reset** to restore the canonical layout.
7. Select a row to load its details and recent movements.

## Saved Views
Saved Views are scoped to the signed-in user and the semantic `inventory.overview` workspace identifier. They can persist supported filter state, column order/width/visibility, sorting and grid density. Another user's views are never loaded into your workspace.

Saved definitions are deliberately fail-safe. If a later Depot version removes or renames a column or filter, unknown state is ignored instead of preventing the workspace from opening. Invalid or unsupported stored definitions are also ignored.

A saved view changes presentation state only. Inventory authorization, server-side filtering and business data access continue through the normal service/repository path.

## Result
You see the current aggregated quantity for the selected inventory without loading the complete movement ledger, and recurring presentation preferences can be restored from your saved views.

## Common problems
> [!NOTE] Stock is derived from immutable movements. Depot does not directly overwrite inventory quantities.

- A zero balance can still have movement history.
- If a recent transaction is missing, refresh the affected row.
- If an older saved view references presentation elements that no longer exist, Depot ignores that stale part and keeps the workspace usable.
- **Reset** restores the canonical workspace presentation; it does not change business data or another user's saved views.

## Required permissions
`Inventory.View`. Saved Views do not add or replace inventory permissions.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Serial and Lot Traceability](topic:inventory.traceability)
- [Stock Movements](topic:inventory.movements)
- [Inventory Counts](topic:warehouse.inventory-counts)
