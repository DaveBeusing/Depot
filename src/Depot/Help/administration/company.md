# Company master data

## Summary
The **Administration > Company** page stores the authoritative legal identity of the organization represented by the current Depot database. The profile is shared by all clients connected to that database and is intended as the source for quotations, orders, invoices, credit notes, electronic invoices, customs/commercial documents and legal document footers.

Company-master-data requirements depend on jurisdiction, legal form, transaction type and market. Depot therefore separates required core identity from conditional disclosures and optional international registrations instead of treating every field as globally mandatory.

## Legal identity and establishment
Maintain the registered legal name, legal form, primary business address, country of establishment and tax-residence country. German entities should also maintain their registered office/seat and postal code.

Enable **registered entity** when the organization is entered in a company, commercial, professional or public register. In that case maintain the registration authority/court, register type where applicable and registration number. Optional global identifiers include:

- **LEI** — Legal Entity Identifier, commonly used in regulated financial markets.
- **GLN** — Global Location Number used in supply-chain and electronic-business scenarios.
- **D-U-N-S** — optional commercial organization identifier used by some international customers and procurement systems.

These identifiers are not substitutes for statutory company-registration or tax identifiers.

## Corporate representation
For German GmbH/UG/AG profiles Depot requires the legal representatives/management field because German business correspondence requires corporate representation information for these legal forms. Maintain the supervisory-board chair when such a board exists and the chair must be disclosed.

If capital is shown on business documents, enable the capital-disclosure option and maintain the complete share/registered-capital information, including outstanding contributions where relevant. If the entity is in liquidation/winding-up, enable the status and maintain the liquidators.

## Branches
Enable **registered branch** when the Depot installation represents a branch rather than only the head legal entity. Maintain the branch name, registration authority and branch registration number. Foreign branches can have disclosure requirements for both the branch and the parent company, so both sets of identifiers must remain available to document-generation logic.

## Tax registrations
Maintain a domestic tax number, primary VAT/GST ID or at least one additional tax registration. Multiple international tax registrations can be stored one per line using:

`CC | TYPE | IDENTIFIER`

Example: `FR | VAT | FR12345678901`

Use the two-letter ISO country code and a clear scheme name such as VAT, GST or SalesTax. OSS registration data can be stored separately. The **IOSS identification number is restricted data** and must not be printed on ordinary commercial invoices, packing slips or customer-facing documents merely because it is stored here.

## Fiscal representative
Where local tax law requires a fiscal representative, enable the corresponding option and maintain the representative's name, VAT identifier and address. Document generators should include these fields only for transactions/jurisdictions where the representative must be disclosed.

## Electronic invoicing
The electronic-invoicing section stores the seller electronic address/endpoint and its scheme plus an optional Leitweg-ID for German public-sector invoicing. Structured-invoice identifiers are kept separate from ordinary email addresses because EN 16931/XRechnung/Peppol-style documents use dedicated identifier schemes.

## Customs and international trade
Maintain customs identifiers actually assigned to the organization:

- **EORI** — required for EU customs import, export and transit operations when the business acts as the relevant economic operator.
- **REX** — registered-exporter identifier for applicable preferential-origin scenarios.
- **AEO authorization** — only when Authorized Economic Operator status has actually been granted.
- **Customs account/deferment reference** — optional internal/master-data reference for customs workflows.

A default Incoterm and named place/port can be stored for recurring trade flows, but every transaction must still confirm the correct Incoterm and named place. Product origin, HS/commodity code, customs value, export-control classification and shipment-specific declarations remain transaction/product data and must not be inferred solely from the company profile.

The exporter/origin statement is a reusable template only. Users remain responsible for ensuring the statement is legally permitted for the specific origin regime and shipment.

## Regulatory and market registrations
Depot provides dedicated fields for commonly relevant German/EU producer-responsibility identifiers such as packaging/EPR, WEEE and battery registrations. Other country-specific environmental, EPR, licensing or market registrations can be stored one per line as:

`CC | SCHEME | IDENTIFIER`

Example: `FR | EPR-PACKAGING | FR123456_01ABC`

Only identifiers that have actually been assigned to the legal entity should be entered. Their presence in Company master data does not by itself prove product-level compliance.

## Regulated professions and supervisory information
For regulated businesses, maintain the competent regulatory/supervisory authority, professional title, jurisdiction in which the title was granted, and a reference to applicable professional rules. These fields are optional for ordinary trading companies but provide a central source when specific website, correspondence or contract disclosures apply.

## Contact and payment data
General phone, email and website fields can be used on business documents. The invoice-contact email can differ from the general mailbox. Bank account holder, bank name, IBAN and BIC are available for payment instructions. The SEPA creditor identifier can be stored when the organization uses SEPA Direct Debit; it should not be displayed on documents unrelated to direct-debit collection.

Depot validates IBAN structure using the standard mod-97 check and checks the structural shape of BIC and SEPA creditor identifiers. Successful structural validation does not prove that an account or identifier is active or belongs to the company.

## Document defaults
Default currency, language and payment terms provide reusable starting values. A default **Incoterms 2020** rule and named place can support recurring international trade, but transaction-level terms always override these defaults. Additional legal-footer text is available only for disclosures that do not have a dedicated structured field.

## Validation and recommendations
Depot distinguishes blocking validation from recommendations:

- Blocking validation covers the core legal identity, conditional register/branch/liquidation/fiscal-representative requirements, tax identity and structural identifier checks.
- Recommendations highlight useful operational data such as business contact details, payment information, EORI for customs workflows and e-invoice routing identifiers.

The profile uses optimistic concurrency. If another administrator saves a newer version first, reload before saving again.

## Important legal scope
Company master data is an authoritative data source, not a universal legal-rule engine. Invoice-required transaction data such as invoice date/number, customer identity, supply date, line description, quantity, taxable amount, VAT treatment and tax amount belongs to the invoice transaction. International-trade data such as HS code, product origin, export-control classification, customs value, destination restrictions and sanctions screening belongs to products/shipments and must be evaluated per transaction.

Local rules can change and can depend on legal form, industry, customer type and destination. Production document templates should therefore be acceptance-tested for every supported jurisdiction and business scenario.

## Permissions
Viewing requires `Settings.View`; changing the company profile requires `Settings.Manage`.

## Related topics
- [Sales Quotes](topic:sales.quotes)
- [Sales Invoices and Credit Notes](topic:sales.invoices)
- [Database Configuration](topic:administration.database)
