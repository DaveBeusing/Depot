# Approvals

## Summary
The **Approvals** workspace is the central four-eyes worklist for both procurement and sales decisions.

Depending on permissions, it exposes:

- **Purchase Approvals** — Purchase Orders in Pending Approval status.
- **Sales Approvals** — Sales Orders in Pending Approval status.

## Purchase Approvals
1. Open **Approvals > Purchase Approvals**.
2. Search or filter the pending queue.
3. Select an order and review supplier, header, lines, totals, dates, notes, and history.
4. Add an optional approval comment or rejection reason.
5. Select **Approve** or **Reject** and confirm.

The decided Purchase Order is removed from the pending worklist and continues in the Purchasing workflow.

## Sales Approvals
1. Open **Approvals > Sales Approvals**.
2. Select a submitted Sales Order.
3. Review customer, order lines, pricing, requested dates, totals, and notes.
4. Approve or reject the order.
5. Return to **Sales > Sales Orders** for reservation and release after approval.

## Four-eyes rule
Depot enforces creator/approver separation in the business services, not only in the UI. Non-administrators cannot approve or reject their own Purchase Orders or Sales Orders. Protected Administrator users may decide their own orders when their role carries the corresponding approval permission.

If another user decides the same record first, reload the current status after the concurrency message.

## Required permissions
- Purchase queue: `PurchaseOrders.Approve`.
- Sales queue: `SalesOrders.Approve`.

A queue is hidden when the signed-in user does not have its corresponding permission.

## Related topics
- [Purchase Orders](topic:purchasing.purchase-orders)
- [Sales Orders](topic:sales.orders)
- [Sales Approvals](topic:sales.approvals)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
