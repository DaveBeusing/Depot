# Shipping, Packing and Customer Returns

Shipping is part of the **Warehouse** workspace because it represents the physical fulfillment and stock-posting side of a Sales Order.

Open **Warehouse > Shipping** to create shipments, pick and pack goods, post the goods issue, reverse incorrect shipment postings, and process Customer Returns.

## Pick and pack workflow

Create a shipment from an active reservation on a released Sales Order. The shipment inherits the Sales Order shipping-address snapshot.

A draft shipment moves through these packing states:

1. **Not Started** — shipment prepared but picking has not begun.
2. **Picking** — warehouse work is in progress.
3. **Packed** — picked quantities are confirmed and the shipment is ready to post.

A shipment must be **Packed** before **Post shipment** is allowed by the business service. Posting creates negative `SalesShipment` stock movements and consumes the corresponding reservations.

Use **Pick list PDF** for warehouse picking and **Packing slip PDF** for the packed parcel. The Delivery Note remains the customer-facing shipment document.

Partial shipments are supported. The Sales Order remains **Partially Shipped** until all ordered quantities are shipped.

## Shipment reversal

Use **Reverse posting** only when the original shipment was posted incorrectly and has not entered an immutable invoicing path. Depot creates positive `SalesShipmentReversal` counter-movements and restores reservations instead of changing the original stock movement.

## Customer return

Use **Create customer return** when goods were genuinely delivered and physically come back. Posting creates positive `CustomerReturn` stock movements and Depot prevents returned quantities from exceeding the shipment quantity already delivered and not previously returned.

Use **Return receipt PDF** for the return document. If the shipment was invoiced, correct the commercial side separately with a Credit Note under **Sales > Invoices**.

## Navigation from Sales

Shipment and Customer Return results opened through Quick Open or notifications route directly to **Warehouse > Shipping**. The originating Sales Order remains available under **Sales > Sales Orders**, where its Timeline shows shipment, reversal, return, invoice, and credit events.

## Required permissions
Shipping visibility and actions remain permission-based. Typical Warehouse Operator permissions include shipment view/create/edit/post/reverse, packing, and Customer Return posting.

## Related topics
- [Sales Orders](topic:sales.orders)
- [Sales Invoices and Credit Notes](topic:sales.invoices)
- [Stock Movements](topic:inventory.movements)
- [Insufficient Stock](topic:troubleshooting.insufficient-stock)
