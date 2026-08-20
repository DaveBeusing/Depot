# Shipping and Customer Returns

Shipping converts reserved sales-order quantities into physical goods issue.

## Pick and pack workflow

Create a shipment from an active reservation. The Shipping workspace shows the sales order, reservation/pick source, shipment quantity and the resulting pick/pack lines together. The shipment inherits the shipping-address snapshot stored on the Sales Order.

While the shipment is **Draft**, carrier, tracking number and notes can be edited and saved. Posting creates negative `SalesShipment` stock movements and consumes the corresponding reservation.

Partial shipments are supported. The sales order remains **Partially Shipped** until all ordered quantities are shipped.

## Shipment reversal

Use **Reverse posting** only when the original shipment was posted incorrectly and has not entered an immutable invoicing path. Depot creates positive `SalesShipmentReversal` counter-movements, restores reservations and moves the order back to Released or Partially Shipped. The original movements remain unchanged.

## Customer return

Use **Create customer return** when goods were genuinely delivered and physically come back from the customer. Posting a return creates positive `CustomerReturn` stock movements. Depot prevents returned quantities from exceeding the shipment quantity and sends workflow notifications to the relevant Sales and Finance users.

Use **Return receipt PDF** to create an auditable return document referencing the original shipment.

If the shipment was invoiced, handle the commercial correction separately with a Credit Note under **Sales > Invoices**.
