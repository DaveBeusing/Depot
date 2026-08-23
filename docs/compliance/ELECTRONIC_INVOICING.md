# Electronic invoicing technical baseline

## Scope

Depot provides an EN 16931-oriented semantic invoice model and a deterministic UN/CEFACT Cross Industry Invoice (CII) XML generator targeted at the German XRechnung profile. The operational Sales Invoice workflow now uses that foundation during posting. This remains a technical implementation baseline, not a statement that every possible document, tax treatment, recipient channel, or future XRechnung release is legally/profile-conformant.

## Authoritative and historical data

For a finalized Sales Invoice, the authoritative electronic representation is the exact structured XML generated at posting and stored in `SalesInvoiceFinalizations`. Human-readable PDF is a related representation; neither PDF regeneration nor mutable current Company/Customer master data replaces the stored issued XML.

Posting combines three historical data groups:

- the immutable seller `DocumentIssuerProfile` captured from Administration > Company;
- the immutable `DocumentBuyerProfile` containing the Buyer identity actually used for issuance;
- the posted invoice transaction data and lines.

The Buyer snapshot includes Customer Number/name, Buyer Reference (BT-10), electronic endpoint (BT-49) and scheme, tax identifiers, free-form billing-address snapshot, structured billing address, country, and contact data. The exact XML is stored with a SHA-256 fingerprint. Loading/export verifies that fingerprint before the document is returned.

The SHA-256 value is an application integrity/tamper-detection control. It is not a digital signature and must not be described as independent authenticity or non-repudiation evidence.

## XRechnung generation and posting

`ElectronicInvoiceService` creates UN/CEFACT CII XML and identifies the XRechnung 3.0 guideline in the document context. The service performs application-level pre-validation of mandatory semantic terms and numeric invariants before XML generation.

`SalesInvoiceService.PostAsync` is the operational finalization boundary. Invoice posting, Sales Order quantity effects, seller snapshot, Buyer/XML finalization, order completion where applicable, and audit persistence share one database transaction. If mandatory identity is missing or XML generation fails, the transaction rolls back and the invoice remains unposted.

The current finalization path requires a configured seller IBAN because it issues bank-transfer payment information. It also requires Buyer Reference, electronic endpoint/scheme, structured Buyer billing address, ISO alpha-2 country-code syntax, and at least one Buyer tax identifier.

## Export and immutability

The Invoice workspace exposes **Export XRechnung** only for a posted invoice. The command delegates through `SalesDocumentService` to `SalesInvoiceFinalizationService`, which loads the finalized record, checks its SHA-256 digest and writes the persisted XML as UTF-8 without regeneration.

A posted legacy invoice with no historical finalization record fails closed. Depot does not silently manufacture an electronic invoice later from today's Company or Customer data.

## Validation boundary

Application-level validation is intentionally not described as complete production KoSIT validation. Representative generated XML is validated in CI against pinned KoSIT/XRechnung assets. Runtime posting does not execute the external KoSIT validator.

Before production electronic exchange, every advertised tax/profile/channel scenario must additionally be validated against the then-applicable XRechnung configuration and recipient/channel requirements. Routing requirements such as Leitweg-ID, electronic-address schemes, and Peppol rules remain organization/recipient-specific.

## Tax scenarios

The current commercial Sales Invoice line model stores a numeric tax rate but does not yet persist an explicit EN 16931 VAT category plus exemption/reason semantics. Finalization therefore accepts positive taxable rates under category `S` and fails closed for zero-rated, exempt, or reverse-charge lines rather than guessing `Z`, `E`, or `AE` from `0%`.

Support for those scenarios requires explicit commercial tax semantics and corresponding conformance fixtures before they are advertised.

## Corrections and credit notes

Posted invoices are immutable and corrections use explicit Credit Notes. The electronic semantic/generator layer supports a credit-note type code, and posted Sales Credit Notes capture their own immutable seller snapshot.

Equivalent Buyer snapshot, exact issued XML retention, hash verification, and production tax/profile handling for electronic credit-note issuance are not yet implemented. Depot must not advertise fully finalized electronic credit-note issuance until that path reaches the same historical-evidence standard as Sales Invoices.

## ZUGFeRD / Factur-X

ZUGFeRD/Factur-X is a hybrid PDF/A-3 plus embedded structured XML format. Depot's semantic model and CII generator provide the structured-data foundation. A conforming PDF/A-3 container, embedded XML metadata, and profile-specific end-to-end validation are still required before any ZUGFeRD/Factur-X claim. A normal PDF with an XML attachment must not be labelled ZUGFeRD/Factur-X.

## Security, privacy and retention

Electronic invoice XML can contain personal/contact, tax, and financial data. Existing Depot privacy, access-control, audit, backup, retention, and export requirements apply to the finalization record and every exported representation.

Historical invoice identity is deliberately separated from mutable Company/Customer master data. Legacy remediation or migration must use independently verified historical source information and a controlled process; it must not backfill proof from current values alone.

## Related evidence

- `docs/compliance/INVOICE_FINALIZATION.md`
- `docs/compliance/ISSUER_SNAPSHOTS.md`
- `docs/compliance/COMPANY_MASTER_DATA.md`
- `src/Depot/Services/SalesInvoiceFinalizationService.cs`
- `tests/Depot.Tests/SalesInvoiceFinalizationTests.cs`
- `.github/workflows/electronic-invoice-conformance.yml`
