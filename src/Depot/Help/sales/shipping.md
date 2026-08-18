# Shipping and Customer Returns

Shipping converts reserved sales-order quantities into physical goods issue.

## Shipment workflow

Create a shipment from an active reservation. While the shipment is **Draft**, carrier, tracking number and notes can be edited and saved. Posting the shipment creates negative `SalesShipment` stock movements and consumes the corresponding reservation.

Partial shipments are supported. The sales order remains **Partially Shipped** until all ordered quantities are shipped.

## Shipment reversal

If a posted shipment has not been invoiced, use **Reverse shipment** with a reason. Depot creates positive `SalesShipmentReversal` counter-movements, restores reservations and moves the order back to Released or Partially Shipped. The original stock movements remain immutable.

## Customer return

If the shipment has already entered the invoicing process, use **Create return** instead of reversing the shipment. Posting a customer return creates positive `CustomerReturn` stock movements. Only quantities not already returned can be processed.

A commercial correction for a posted invoice is handled separately with a Credit Note under Sales Invoices.
