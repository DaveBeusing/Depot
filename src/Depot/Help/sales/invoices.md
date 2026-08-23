# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so invoiced quantities remain tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice under **Sales > Invoices**. Depot uses the Sales Order billing-address snapshot together with shipment quantities and the order's pricing, discount, and tax snapshots.

The Invoice workspace shows **Due Status**, **Due Date**, and **Days Until Due**. Posted invoices are classified as Not Due, Due Today, or Overdue; draft and cancelled invoices retain their own states.

A Draft invoice can be reviewed, exported to PDF, opened as an **Email invoice** draft with the PDF attached, or cancelled before posting. Posting increases invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the Sales Order becomes **Completed**.

## Credit notes

Posted invoices are immutable. Corrections are recorded as separate Credit Notes. Depot tracks cumulative credited quantities and prevents the total from exceeding the originally invoiced quantity.

The correction record remains linked to the original invoice/Sales Order. If goods physically return as well, process the inventory side independently with a Customer Return under **Warehouse > Shipping**.

## Electronic invoicing technical baseline

Depot also contains an EN 16931-oriented electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Representative generated XML is checked in CI with a pinned KoSIT validator/configuration.

This technical foundation is not yet the same as production electronic-invoice delivery from this workspace. Before electronic-invoice support is advertised, Depot must integrate the generated structured XML into the persisted invoice workflow, retain issued XML immutably, validate each supported tax/profile/channel scenario, and configure organization/recipient-specific identifiers.

ZUGFeRD/Factur-X is not currently claimed. A true implementation requires a conforming PDF/A-3 container with embedded structured XML and end-to-end validation; a normal PDF with an XML attachment is not sufficient.

## Related topics
- [Sales Orders](topic:sales.orders)
- [Shipping, Packing and Customer Returns](topic:sales.shipping)
- [Audit Log](topic:administration.audit-log)
