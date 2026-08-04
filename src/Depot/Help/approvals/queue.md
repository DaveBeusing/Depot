# Purchase Order Approvals

## Summary
The approval queue is a focused worklist containing only purchase orders in Pending Approval status.

## Prerequisites
- You have the approval permission.
- The order was submitted by another user.

## Steps
1. Open **Approvals**.
2. Search or filter the pending queue.
3. Select an order and review its header, lines, totals, supplier, dates, notes, and status history.
4. Add an optional approval comment or a rejection reason.
5. Select **Approve** or **Reject** and confirm.

## Result
The decision, user, timestamp, order status, and audit entry are committed atomically. The decided order is removed from the worklist.

## Common problems
- Depot enforces the four-eyes rule in the service, not only in the UI.
- If another user decided first, reload the current status after the concurrency message.

## Required permissions
`PurchaseOrders.Approve`.

## Related topics
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
