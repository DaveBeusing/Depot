# Sales Overview

The Sales workspace covers Depot's commercial and order-to-cash process from customer master data and quotations through order release and invoicing.

Physical fulfillment is deliberately separated from the commercial workspace: once a Sales Order is released, picking, packing, shipment posting, shipment reversal, and Customer Returns are handled under **Warehouse > Shipping**.

## Sales sections

Depending on permissions, the Sales workspace exposes these sections directly below the workspace tabs:

- **Overview** — operational Sales workload and order-to-cash status.
- **Quotes** — customer quotations, PDF/email output, pricing snapshots, and conversion to Sales Orders.
- **Pricing** — Global, Regional and optional Customer price lists, item prices, discounts, validity windows, Sales Regions, and customer assignments.
- **Customers** — customer master data, addresses, contacts, payment terms, currency, optional Sales Region, and optional customer pricing.
- **Sales Orders** — customer demand, approval state, reservations, backorders, and lifecycle timeline.
- **Invoices** — shipment-based invoices, due status, Credit Notes, PDF output, and email drafts.

Sales approvals are intentionally not a Sales subsection. Submitted orders are reviewed in the global **Approvals > Sales Approvals** queue alongside Purchase Approvals.

## Order-to-cash process

1. Create or select a customer and maintain addresses and contacts.
2. Optionally prepare a Quote using per-item Customer → Region → Global pricing and convert it to a Sales Order.
3. Create or edit the Sales Order and submit it for approval.
4. Review the order under **Approvals > Sales Approvals** and approve or reject it.
5. Reserve available inventory and release the approved order. Unreserved demand remains visible as backorder.
6. Continue the physical workflow under **Warehouse > Shipping**: create a shipment, pick, pack, and post the goods issue.
7. Return to **Sales > Invoices** to create and post the invoice from the shipment.

## Corrections

Posted operational records are not edited in place:

- Incorrect shipment posting → **Warehouse > Shipping > Reverse posting**.
- Physical goods returned → **Warehouse > Shipping > Customer Return**.
- Posted invoice correction → **Sales > Invoices > Credit Note**.

The Sales Order Timeline links the key lifecycle events across approval, reservation, fulfillment, invoicing, returns, and credits.
