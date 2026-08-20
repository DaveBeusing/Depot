# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so invoiced quantities remain tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice under **Sales > Invoices**. Depot uses the Sales Order billing-address snapshot together with shipment quantities and the order's pricing, discount and tax snapshots.

The Invoice workspace shows **Due Status**, **Due Date** and **Days Until Due**. Posted invoices are classified as Not Due, Due Today or Overdue; draft and cancelled invoices retain their own states.

A Draft invoice can be reviewed, exported to PDF, opened as an **Email invoice** draft with the PDF attached, or cancelled before posting. Posting increases invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the Sales Order becomes **Completed**.

## Credit notes

Posted invoices are immutable. Corrections are recorded as separate Credit Notes.

You can credit the complete invoice or select an invoice line and enter a partial credit quantity. Depot tracks cumulative credited quantities and prevents the total from exceeding the originally invoiced quantity.

The Invoice workspace shows original gross amount and effective value after posted credits. Use **Credit note PDF** to generate the correction document with references to the original invoice and Sales Order.

If goods physically return as well, process the inventory side independently with a Customer Return under **Warehouse > Shipping**.

## Related topics
- [Sales Orders](topic:sales.orders)
- [Shipping, Packing and Customer Returns](topic:sales.shipping)
