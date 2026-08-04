# Material Returns

## Summary
A material return records physical material coming back into stock. It is a separate business document, not a reversal.

## Prerequisites
- A destination inventory exists and is active.
- Every line has a positive quantity and reason code.

## Steps
1. Open **Warehouse > Material Returns** and create a draft.
2. Enter the source or recipient, reference, date, and notes.
3. Add destination inventories, quantities, and reasons.
4. Save and post the return.

## Result
Depot creates positive movements and atomically records the posted document and audit entry.

## Common problems
- An original material issue may be referenced but is not mandatory.
- Corrections use explicit counter-movements; they do not alter the posted return.

## Required permissions
`MaterialReturns.View`; actions require create, edit, post, or reverse permissions.

## Related topics
- [Material Issues](topic:warehouse.material-issues)
- [Stock Movements](topic:inventory.movements)
