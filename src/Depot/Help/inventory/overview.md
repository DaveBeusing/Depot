# Inventory Overview

## Summary
The inventory overview shows stock by item and storage location, including its warehouse and recent movement activity.

## Prerequisites
- Items, warehouses, and storage locations exist.
- Inventory records have been created by an authorized workflow or import.

## Steps
1. Open **Inventory > Overview**.
2. Search or filter the server-side list.
3. Select a row to load its details and recent movements.

## Result
You see the current aggregated quantity for the selected inventory without loading the complete movement ledger.

## Common problems
> [!NOTE] Stock is derived from immutable movements. Depot does not directly overwrite inventory quantities.

- A zero balance can still have movement history.
- If a recent transaction is missing, refresh the affected row.

## Required permissions
`Inventory.View`.

## Related topics
- [Stock Movements](topic:inventory.movements)
- [Inventory Counts](topic:warehouse.inventory-counts)
