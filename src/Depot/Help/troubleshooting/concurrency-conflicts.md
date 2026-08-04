# Concurrency Conflicts

## Summary
A concurrency conflict means another user changed the same document after you loaded it. Depot prevents the older version from overwriting newer data.

## Prerequisites
- Keep the document number and current operation available for diagnosis.

## Steps
1. Read the status message and do not repeat an irreversible action immediately.
2. Reload or reselect the affected document.
3. Confirm its current status and version.
4. Reapply only changes that are still needed.

## Result
You continue from the current database state without silently losing another user's work.

## Common problems
> [!NOTE] After an unclear network result, Depot reloads critical documents to detect whether the operation already committed.

- Duplicated posting is prevented by status, version, and operation identifiers in critical workflows.

## Required permissions
The same permission as the affected workflow operation.

## Related topics
- [Purchase Order Approvals](topic:approvals.queue)
- [Inventory Counts](topic:warehouse.inventory-counts)
