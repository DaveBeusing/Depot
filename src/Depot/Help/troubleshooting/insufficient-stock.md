# Insufficient Stock

## Summary
Depot rejects a booking that would make an inventory balance negative.

## Prerequisites
- Identify the item, warehouse, storage location, requested quantity, and document number.

## Steps
1. Open **Inventory > Overview** and locate the exact inventory.
2. Compare available stock with the requested quantity.
3. Check recent movements and other open work.
4. Correct the draft quantity or choose a valid source inventory.
5. Retry the booking after refreshing the document.

## Result
The workflow posts only when every affected source inventory has sufficient stock inside the same transaction.

## Common problems
- Stock may have been consumed by another user after the draft was prepared.
- A quantity in another warehouse or storage location is not available to the selected source inventory.

## Required permissions
View access to inventory plus the permission for the attempted workflow.

## Related topics
- [Inventory Overview](topic:inventory.overview)
- [Stock Transfers](topic:warehouse.transfers)
- [Material Issues](topic:warehouse.material-issues)
