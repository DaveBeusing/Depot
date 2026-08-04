# Purchase Orders

## Summary
Purchase orders progress through Draft, approval, ordering, receipt, closure, or cancellation states.

## Prerequisites
- The supplier and all ordered items are active.
- The order has at least one unique item line with a positive quantity and non-negative price.

## Steps
1. Open **Purchasing > Purchase Orders** and create a Draft.
2. Complete supplier, dates, notes, and lines.
3. Save the Draft and submit it for approval.
4. After approval, select **Place Order**.
5. Receive deliveries through Goods Receipts.
6. Close an Ordered or Partially Received order when no further receipt is expected, providing a reason.

## Result
Every transition validates status, permission, version, and required data and is audited atomically.

## Common problems
- Draft orders cannot move directly to Ordered.
- The creator cannot approve their own order.
- Cancel is only available when no receipt has been posted.

## Required permissions
`PurchaseOrders.View`; each workflow transition has a dedicated permission.

## Related topics
- [Purchase Order Approvals](topic:approvals.queue)
- [Goods Receipts](topic:purchasing.goods-receipts)
