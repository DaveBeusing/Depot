# Sales Orders

Sales orders define the customer demand, prices and quantities that drive reservation, shipping and invoicing.

## Workflow

A new order starts as **Draft**. Add items, quantities, unit prices, discounts and tax rates, then save and submit it. Submitted orders move to **Pending Approval** and cannot be edited until rejected and reopened.

After approval, reserve inventory against one or more order lines. Depot shows **Ordered**, **Reserved**, **Backorder**, **Shipped** and **Invoiced** quantities so the fulfillment state is visible per line.

## Partial release and backorders

An approved order can be released once at least one quantity is reserved. A full reservation is not required. Unreserved quantities remain as backorders and can be reserved later while the order is Released or Partially Shipped.

Physical stock is not reduced when an order is approved, reserved or released. Stock changes only when a shipment is posted.

Unsaved draft changes are protected when you navigate away or close the workspace tab.
