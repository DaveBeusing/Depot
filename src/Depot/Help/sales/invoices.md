# Sales Invoices and Credit Notes

Sales invoices are created from posted shipments, so invoiced quantities remain tied directly to delivered goods.

## Invoice workflow

Select a posted shipment and create an invoice under **Sales > Invoices**. Depot uses the Sales Order billing-address snapshot together with shipment quantities and the order's pricing, discount, and tax snapshots.

The Invoice workspace shows **Due Status**, **Due Date**, and **Days Until Due**. Posted invoices are classified as Not Due, Due Today, or Overdue; draft and cancelled invoices retain their own states.

A Draft invoice can be reviewed, exported to PDF, opened as an **Email invoice** draft with the PDF attached, or cancelled before posting. Posting increases invoiced quantity on the related order lines. When all ordered quantities are shipped and invoiced, the Sales Order becomes **Completed**.

Posting is also the finalization boundary for the invoice identity. Depot only completes the posting transaction when the seller identity, buyer identity and XRechnung representation can all be captured successfully.

## Accounts Receivable integration

Finance F2 connects posted Sales Invoices and Sales Credit Notes to the customer subledger when an active Accounts Receivable configuration exists.

For a Sales Invoice, the same database transaction then includes:

- the Sales status/quantity transition;
- seller/buyer/XRechnung finalization;
- the configured F1 General Ledger posting;
- a debit Accounts Receivable open item for the invoice gross amount;
- Finance number allocation and Audit Log evidence.

If the AR/GL configuration, posting profile, period, exchange rate, account/dimension validation, number allocation, persistence, or audit write fails, the entire invoice posting rolls back. Depot does not leave a posted Sales Invoice without its required configured AR/GL evidence.

If Accounts Receivable is not configured and active, Sales Invoice/Credit Note posting continues with its existing Sales/e-invoice behavior and does **not** invent accounts or Finance defaults.

A posted Sales Credit Note creates a configured GL entry and credit open item. When the original invoice still has an outstanding debit open item for the same customer/currency/book/legal entity, Depot automatically applies as much of the credit as possible. Any remaining credit stays available under **Finance > Receivables** for later allocation.

See [Accounts Receivable](topic:finance.receivables) for payments, overpayments, write-offs, dunning, aging, and customer statements.

## Historical seller and buyer identity

Draft invoices use the current company and customer master data. When an invoice is posted, Depot freezes both sides needed to reproduce the issued invoice:

- the publishable company/issuer identity from **Administration > Company**;
- customer number and invoice customer name;
- Buyer Reference (BT-10) and electronic address (BT-49) including its scheme;
- buyer tax ID and VAT ID;
- the invoice's existing free-form billing-address snapshot;
- structured billing street, address line 2, postal code, city and country;
- buyer contact data used for the electronic invoice.

The issuer snapshot and invoice finalization are written in the same database transaction as the invoice status change and audit event. If any mandatory identity data or XRechnung generation fails, the whole posting transaction is rolled back.

Later changes to company or customer master data therefore do not alter a previously finalized invoice. Posted invoice PDFs continue to require their historical issuer snapshot, while the structured buyer identity and XRechnung XML are read from the finalization record rather than regenerated from current customer data.

Legacy posted invoices that predate these snapshots are not silently reconstructed from today's master data. Such records require a controlled remediation using independently verified historical information.

## Customer E-Invoice Identity

The customer workspace contains an **E-Invoice Identity** tab. Before an invoice can be posted, maintain:

- Buyer Reference (BT-10);
- electronic invoice address (BT-49) and endpoint scheme;
- structured billing street, postal code, city and ISO alpha-2 country code;
- at least one tax identifier: Tax ID or VAT ID.

The ordinary multiline Billing Address remains the commercial address snapshot used on the Sales Order and PDF. The structured fields exist in addition to it so XRechnung party data is not guessed by parsing display text.

## Persisted XRechnung

At posting, Depot builds the XRechnung-oriented UN/CEFACT CII document from the frozen invoice, issuer and buyer data and stores the exact generated XML in `SalesInvoiceFinalizations`. A SHA-256 fingerprint is stored alongside the XML and is verified whenever the finalization is loaded or exported. Export therefore returns the persisted issued XML instead of generating a potentially different document from current master data.

For a posted invoice, use **Export XRechnung** in the Invoice action bar. The action is disabled for Draft invoices because no issued XML exists before successful posting. The export path loads the finalized record, verifies its SHA-256 fingerprint, and writes those stored XML bytes as UTF-8 without regenerating the invoice.

The runtime generator performs Depot's EN 16931/XRechnung model validation. Representative generated XML remains checked in CI with the pinned KoSIT validator/configuration. Runtime posting does not currently invoke the external KoSIT validator executable itself.

Depot currently finalizes only invoice lines with a positive standard VAT rate. Zero-rated, exempt and reverse-charge scenarios are deliberately blocked until the invoice model carries the explicit EN 16931 tax category and exemption/reason semantics needed to issue them without guessing.

ZUGFeRD/Factur-X is not currently claimed. A true implementation requires a conforming PDF/A-3 container with embedded structured XML and end-to-end validation; a normal PDF with an XML attachment is not sufficient.

## Credit notes

Posted invoices are immutable. Corrections are recorded as separate Credit Notes. Depot tracks cumulative credited quantities and prevents the total from exceeding the originally invoiced quantity.

Each credit note captures its own issuer snapshot when it is posted. This preserves the legal identity that applied to the correction document even if company master data changes later. Buyer/XRechnung finalization for credit notes remains a separate follow-up because the current electronic-invoice flow finalizes sales invoices only.

When Accounts Receivable is configured, the credit note's Finance posting and AR credit open item are part of the same posting transaction. The original posted invoice remains unchanged; settlement is represented by controlled AR allocations and the GL correction journal rather than by rewriting the invoice.

The correction record remains linked to the original invoice/Sales Order. If goods physically return as well, process the inventory side independently with a Customer Return under **Warehouse > Shipping**.

## Related topics

- [Accounts Receivable](topic:finance.receivables)
- [General Ledger and Posting](topic:finance.general-ledger)
- [Sales Orders](topic:sales.orders)
- [Shipping, Packing and Customer Returns](topic:sales.shipping)
- [Company](topic:administration.company)
- [Audit Log](topic:administration.audit-log)
