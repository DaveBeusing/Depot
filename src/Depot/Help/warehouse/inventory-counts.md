# Inventory Counts

## Summary
An inventory count captures a warehouse snapshot, records physical quantities, reviews differences, and posts correction movements.

## Prerequisites
- The warehouse and its inventories are active.
- Counters have access to the relevant physical locations.

## Steps
1. Create a Draft count for one warehouse.
2. Select **Start Count** to snapshot all active inventories.
3. Enter every counted quantity. Zero is valid; blank means not counted.
4. Filter uncounted lines or differences as needed.
5. Move the complete count to Review.
6. Review and post the count.

## Result
Differences are booked against the current stock at posting time. The original expected snapshot remains unchanged for traceability.

## Common problems
> [!NOTE] Movements between count start and posting are considered when Depot calculates the final correction.

- Review requires all mandatory positions to be counted.
- A concurrency conflict requires reloading the count before continuing.

## Required permissions
`InventoryCounts.View`; workflow actions use create, edit, post, and reverse permissions.

## Related topics
- [Stock Movements](topic:inventory.movements)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
