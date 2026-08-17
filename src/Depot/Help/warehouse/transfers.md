# Stock Transfers

## Summary
Transfers move the same item between inventories in different warehouses through paired Transfer Out and Transfer In movements.

## Prerequisites
- Source and destination warehouses are different.
- Both inventories belong to the selected warehouses and reference the same item.
- The source has sufficient stock.

## Steps
1. Open **Warehouse > Transfers**, or use **Ctrl+Shift+P** and run **Transfer Stock** to open the workflow and create a new Draft.
2. Select source and destination warehouses.
3. Add each inventory pair once and enter a positive quantity.
4. Save the draft.
5. Review available stock and select **Post Transfer**.
6. Confirm the booking.

## Result
Depot atomically creates paired movements, links them with the transfer number, and marks the transfer as Posted.

## Common problems
- Identical source and destination warehouses are not allowed.
- Posting fails if another transaction consumes the source stock first.
- A posted transfer is corrected through counter-movements, not editing.
- Direct workflow commands are shown only when the corresponding workspace is available.

## Required permissions
`StockTransfers.View`; creating, editing, posting, and reversing use separate permissions.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Inventory Overview](topic:inventory.overview)
- [Insufficient Stock](topic:troubleshooting.insufficient-stock)
