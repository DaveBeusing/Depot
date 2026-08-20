# Sales Orders

Sales orders define customer demand, prices, address snapshots and quantities that drive reservation, shipping and invoicing.

## Workflow

A new order starts as **Draft**. Select the customer and the billing/shipping addresses for the order. Depot stores those addresses as snapshots so later customer changes do not alter existing documents.

Add items, quantities, prices, discounts and tax rates. If the customer has an assigned price list, select an item and use **Apply customer price** to load the valid customer price and discount for the order date.

Save and submit the order. Submitted orders move to **Pending Approval**. After approval, reserve inventory against one or more lines and release the available quantity for fulfillment.

## Allocation and backorders

Depot shows **Ordered**, **Reserved**, **Backorder**, **Shipped** and **Invoiced** quantities per line. An approved order can be released with a partial reservation. Unreserved demand remains a backorder and can be allocated later.

Physical stock is not reduced by approval, reservation or release. Stock changes only when a packed shipment is posted.

## Timeline

The Order Timeline combines key lifecycle events in one view: creation, submission, approval, release, shipment posting/reversal, invoices, customer returns and credit notes. Use it to understand the operational history without manually joining separate documents.

Unsaved draft changes are protected when you navigate away or close the workspace tab.
