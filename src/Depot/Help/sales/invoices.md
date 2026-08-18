# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so the invoiced quantity is tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice. Depot snapshots the shipment quantities and the sales-order pricing, discount and tax values. A Draft invoice can be reviewed, exported to PDF or cancelled before posting.

Posting the invoice increases the invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the sales order becomes **Completed**.

## Credit notes

A posted invoice is immutable. To correct it, enter a reason and create a Credit Note. The credit note snapshots the original invoice quantities and pricing. Posting the credit note records the commercial correction without modifying the original invoice.

If goods are physically returned as well, process the stock side independently with a Customer Return under Shipping.
