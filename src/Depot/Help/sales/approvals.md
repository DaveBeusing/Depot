# Sales Approvals

Sales approvals provide a controlled four-eyes step before inventory is allocated and an order is released.

Submitted Sales Orders are reviewed in the global **Approvals** workspace, not inside the Sales workspace. Open **Approvals > Sales Approvals** to work through the pending queue.

## Workflow

1. Create and submit the order under **Sales > Sales Orders**.
2. Open **Approvals > Sales Approvals**.
3. Review customer, lines, totals, requested dates, notes, and current status.
4. Approve or reject the order.
5. Return to **Sales > Sales Orders** for reservation and release after approval.

The creator cannot approve or reject their own order unless the current user is an Administrator. The service enforces this four-eyes rule independently of the UI.

**Approve** moves the order to **Approved**, where inventory can be reserved. **Reject** records the decision and optional comment; a rejected order can be reopened as a Draft by a user with edit permission.

## Required permission
`SalesOrders.Approve`.

## Related topics
- [Sales Orders](topic:sales.orders)
- [Approvals](topic:approvals.queue)
- [Concurrency Conflicts](topic:troubleshooting.concurrency-conflict)
