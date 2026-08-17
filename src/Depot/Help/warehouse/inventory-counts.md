# Inventory Counts

## Summary
An inventory count captures a warehouse snapshot, records physical quantities, reviews differences, and posts correction movements.

## Prerequisites
- The warehouse and its inventories are active.
- Counters have access to the relevant physical locations.

## Steps
1. Open **Warehouse > Inventory Counts**. You can also use **Ctrl+Shift+P** and choose **Start Inventory Count** to open the workflow and create a new Draft.
2. Select a warehouse for the Draft.
3. Select **Start Count** to snapshot all active inventories.
4. Enter every counted quantity. Zero is valid; blank means not counted.
5. Filter uncounted lines or differences as needed.
6. Move the complete count to Review.
7. Review and post the count.

## Result
Differences are booked against the current stock at posting time. The original expected snapshot remains unchanged for traceability.

## Common problems
> [!NOTE] Movements between count start and posting are considered when Depot calculates the final correction.

- Review requires all mandatory positions to be counted.
- A concurrency conflict requires reloading the count before continuing.
- Direct workflow commands are shown only when the corresponding workspace is available.

## Required permissions
`InventoryCounts.View`; workflow actions use create, edit, post, and reverse permissions.

## Related topics
- [Workspace Navigation](topic:getting-started.workspace-navigation)
- [Stock Movements](topic:inventory.movements)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
