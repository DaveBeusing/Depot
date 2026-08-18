# Sales Approvals

Sales approvals provide a controlled four-eyes step before inventory is allocated and an order is released.

Orders submitted from **Sales Orders** appear in **Sales > Approvals** for users with `SalesOrders.Approve` permission. The creator cannot approve or reject their own order unless the current user is an Administrator.

Approve moves the order to **Approved**, where inventory can be reserved. Reject records the decision and optional comment; a rejected order can be reopened as a Draft by a user with edit permission.
