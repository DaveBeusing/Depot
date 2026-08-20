# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so invoiced quantities remain tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice. Depot uses the Sales Order billing-address snapshot together with the shipment quantities and the order's pricing, discount and tax snapshots. A Draft invoice can be reviewed, exported to PDF or cancelled before posting.

Posting increases the invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the Sales Order becomes **Completed**.

## Credit notes

Posted invoices are immutable. Corrections are recorded as separate Credit Notes.

You can credit the complete invoice or select an invoice line and enter a partial credit quantity. Depot tracks cumulative credited quantities and prevents the total credited quantity from exceeding the originally invoiced quantity.

The Invoice workspace shows the original gross amount and the effective value after posted credits. Use **Credit note PDF** to generate the correction document with references to the original invoice and Sales Order.

Posting a Credit Note sends workflow notifications to relevant Sales and Finance users without changing the original invoice.

If goods physically return as well, process the inventory side independently with a Customer Return under **Sales > Shipping**.
