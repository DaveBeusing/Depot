# Invoice finalization boundary

Depot treats sales-invoice posting as the point at which the business-document identity becomes immutable.

## Atomic finalization

The following changes occur inside one database transaction:

1. invoice quantities are applied to the Sales Order;
2. the Sales Invoice changes from Draft to Posted;
3. the current publishable Company identity is captured as the immutable issuer snapshot;
4. the Buyer identity used by the invoice is projected and stored;
5. the XRechnung-oriented UN/CEFACT CII XML is generated from the frozen invoice/issuer/buyer values;
6. the exact XML and its SHA-256 fingerprint are persisted;
7. Sales Order completion and audit entries are committed.

If any mandatory identity value is missing or XML generation fails, the transaction is rolled back and the invoice remains unposted.

## Buyer snapshot

`SalesInvoiceFinalizations.BuyerPayload` stores a serialized `DocumentBuyerProfile` containing the buyer values used when the invoice was issued:

- Customer ID and Customer Number;
- invoice customer name;
- Buyer Reference (BT-10);
- electronic address (BT-49) and endpoint scheme;
- Tax ID and VAT ID;
- the invoice's immutable free-form billing-address snapshot;
- structured street, address line 2, postal code, city and ISO country code;
- buyer contact name, email and phone.

Structured billing fields are maintained separately from the display-oriented multiline Billing Address. Depot does not parse legal structured address data out of free-form text at posting time.

## Persisted XRechnung

`SalesInvoiceFinalizations.XRechnungXml` contains the exact XML generated during posting. It is not recreated from current master data for later export. `XRechnungSha256` contains the lower-case hexadecimal SHA-256 digest of the UTF-8 XML bytes.

`SalesInvoiceFinalizationService.LoadRequired` verifies the digest before returning the record. `ExportXRechnung` exports the verified persisted XML without regenerating it.

The generated document uses the existing deterministic EN 16931-oriented UN/CEFACT CII generator targeted at XRechnung 3.0. The runtime generator performs Depot's application-level validation. Representative XML remains checked by the CI validation baseline; the external KoSIT validator executable is not invoked as part of the runtime posting transaction.

## Tax-scenario boundary

The current invoice model does not yet persist an explicit EN 16931 VAT category/exemption reason per sales line. Therefore finalization currently accepts only lines with a positive standard VAT rate and emits category `S`.

Zero-rated, exempt and reverse-charge lines fail closed. They must not be guessed as category `Z`, `E` or `AE` based solely on a numeric 0% tax rate. Supporting those scenarios requires explicit tax-category and exemption/reason semantics in the commercial invoice model.

## Immutability and tamper detection

`SalesInvoiceFinalizations` has exactly one row per Sales Invoice through its primary key. Application finalization rejects a second record. There is no application update path for BuyerPayload, XML or hash.

Changing Customer or Company master data after posting cannot change the saved finalization. If persisted XML is modified outside the normal application path, the SHA-256 check fails when the finalization is loaded.

## Legacy invoices

Invoices posted before invoice-finalization support do not contain a provable Buyer snapshot or issued XML. Depot does not reconstruct these silently from current master data. Historical remediation requires independently verified source information and a controlled migration process.

## Database schema

Sales schema version 8 adds the structured Customer electronic-invoice fields and `SalesInvoiceFinalizations` for SQLite, SQL Server and MySQL/MariaDB.
