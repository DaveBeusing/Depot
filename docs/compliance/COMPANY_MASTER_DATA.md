# Company master data scope

Depot's company profile is the authoritative issuer/legal-entity master-data source for business documents generated from one Depot database.

## Design principles

1. **Jurisdiction-aware, not Germany-hardcoded.** German GmbH/UG/AG disclosure controls are validated where applicable, while foreign or unregistered entities are not forced into German register/management semantics.
2. **Structured identifiers before free text.** Legal, tax, customs, banking, electronic-invoice and common regulatory identifiers have dedicated fields. Country-specific registrations that cannot reasonably be modeled globally use the structured line format `CC | SCHEME | IDENTIFIER`.
3. **Conditional obligations.** Branch, liquidation, supervisory-board, capital-disclosure and fiscal-representative data is required only when the corresponding business condition is enabled.
4. **Transaction data stays outside company master data.** Invoice number/date, customer VAT ID, supply date, tax treatment, product origin, HS/commodity code, customs value, export-control classification, sanctions screening and shipment declarations belong to the transaction/product domain.
5. **Sensitive identifiers are not automatically printable.** In particular IOSS and internal customs-account references must be consumed only by workflows that explicitly require them.
6. **One authoritative issuer, explicit projections.** Human-readable PDFs and structured electronic invoices consume a sanitized `DocumentIssuerProfile` derived from Company master data. This prevents document generators from each inventing their own sender data or accidentally exposing unrelated regulatory identifiers.

## Implemented data groups

- Legal name, legal form, trading name, establishment and tax residence
- Registered office, registration authority/type/number
- Management/legal representatives, supervisory board, capital and liquidation
- Branch registration details
- Domestic tax number, VAT/GST ID, W-IdNr./business ID and multiple foreign tax registrations
- Fiscal representative identity, VAT ID and address
- Electronic-invoice endpoint/scheme and Leitweg-ID
- EORI, REX, AEO and customs-account references
- Default Incoterms 2020 rule and named place/port
- LEI, GLN and optional D-U-N-S identifier
- Packaging/EPR, WEEE, battery and additional country-specific regulatory registrations
- Regulatory authority and regulated-profession disclosure data
- Contact, banking, IBAN/BIC and SEPA creditor identifier
- Currency, language, payment-term and legal-footer defaults

## Business-document integration

`CompanyDocumentIdentityService` reads the current profile immediately before document generation and re-runs the company validation rules. If the company profile has never been configured or contains blocking errors, the document is not generated.

The resulting `DocumentIssuerProfile` contains only normal publication-safe issuer fields. Sales PDFs use it for the issuer name, address, document metadata, registration/management disclosure, tax identifiers, bank details and contact footer. The same projection maps into `ElectronicInvoiceParty` for EN 16931/XRechnung seller data.

Restricted or scenario-specific values such as IOSS and internal customs-account references are deliberately absent from `DocumentIssuerProfile`. Their existence in Company master data can therefore never make them appear accidentally on an ordinary quote, invoice, credit note or delivery document.

## Legal references used for the baseline

The technical baseline was designed against current German business-letter and invoice requirements, EU VAT invoice requirements and EU customs identification concepts. These sources establish minimum patterns; they do not make Depot a substitute for jurisdiction-specific legal/tax review.

Production document templates must be acceptance-tested for each supported legal form, country, tax treatment and international-trade scenario before relying on the generated disclosures.
