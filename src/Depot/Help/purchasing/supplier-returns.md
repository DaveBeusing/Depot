# Supplier Returns

## Summary
Supplier returns send received goods back to one supplier and create negative stock movements.

## Prerequisites
- Select the supplier and preferably the original goods receipt.
- Return quantities cannot exceed the net received quantity still available for return.
- The selected inventory has sufficient stock.

## Steps
1. Open **Purchasing > Supplier Returns** and create a draft.
2. Select a supplier and goods receipt.
3. Review returnable and already returned quantities.
4. Add quantities, reason codes, and references.
5. Save and post the return.

## Result
The posted return remains a separate document. Historical received quantities are not rewritten; reporting evaluates supplier returns separately.

## Common problems
- Stock moved away after receipt may make the return unavailable from the original inventory.
- Posted returns are corrected through counter-movements.

## Required permissions
`SupplierReturns.View`; actions require create, edit, post, or reverse permissions.

## Related topics
- [Goods Receipts](topic:purchasing.goods-receipts)
- [Stock Movements](topic:inventory.movements)
