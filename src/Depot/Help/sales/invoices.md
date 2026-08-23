# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so invoiced quantities remain tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice under **Sales > Invoices**. Depot uses the Sales Order billing-address snapshot together with shipment quantities and the order's pricing, discount, and tax snapshots.

The Invoice workspace shows **Due Status**, **Due Date**, and **Days Until Due**. Posted invoices are classified as Not Due, Due Today, or Overdue; draft and cancelled invoices retain their own states.

A Draft invoice can be reviewed, exported to PDF, opened as an **Email invoice** draft with the PDF attached, or cancelled before posting. Posting increases invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the Sales Order becomes **Completed**.

## Historical company identity

Draft invoices use the current company details from **Administration > Company**. When an invoice is posted, Depot stores an immutable snapshot of the publishable legal issuer data in the same transaction as the posting and audit event. Later changes to company name, address, VAT/tax identifiers, register details, banking data or ordinary document contact data therefore do not change a previously posted invoice when its PDF is regenerated.

Posted invoices require their historical issuer snapshot. Depot deliberately does not fall back to today's company master data if a legacy posted invoice has no snapshot, because doing so would create a different historical document.

## Credit notes

Posted invoices are immutable. Corrections are recorded as separate Credit Notes. Depot tracks cumulative credited quantities and prevents the total from exceeding the originally invoiced quantity.

Each credit note captures its own issuer snapshot when it is posted. This preserves the legal identity that applied to the correction document even if company master data changes later.

The correction record remains linked to the original invoice/Sales Order. If goods physically return as well, process the inventory side independently with a Customer Return under **Warehouse > Shipping**.

## Electronic invoicing technical baseline

Depot also contains an EN 16931-oriented electronic-invoice model and deterministic UN/CEFACT CII generation targeted at XRechnung 3.0. Representative generated XML is checked in CI with a pinned KoSIT validator/configuration.

The Company master data and document-issuer projection provide the seller identity foundation for structured invoices. Production electronic-invoice delivery still requires immutable storage of the issued XML, validation of each supported tax/profile/channel scenario, and organization/recipient-specific routing configuration.

ZUGFeRD/Factur-X is not currently claimed. A true implementation requires a conforming PDF/A-3 container with embedded structured XML and end-to-end validation; a normal PDF with an XML attachment is not sufficient.

## Related topics
- [Sales Orders](topic:sales.orders)
- [Shipping, Packing and Customer Returns](topic:sales.shipping)
- [Company](topic:administration.company)
- [Audit Log](topic:administration.audit-log)
