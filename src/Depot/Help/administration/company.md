# Company master data

## Summary
The **Administration > Company** page stores the legal identity of the organization using the current Depot database. The profile is shared by all clients connected to that database and is the authoritative issuer source for quotations, order confirmations, invoices, credit notes, delivery documents, electronic invoices, and other business documents.

Depot validates the company profile before a business document is generated. If mandatory legal master data is incomplete, document creation is blocked with an actionable message instead of silently falling back to a generic `DEPOT` sender.

## Required legal identity
Maintain the registered legal company name and legal form, registered office where applicable, full business address, registration authority/type/number for registered entities, and the legally relevant management or representatives for the selected jurisdiction/legal form. German GmbH/UG/AG profiles receive the corresponding German corporate-disclosure checks without applying those rules blindly to foreign entities.

If a supervisory board exists, maintain its chair. If capital information is published, maintain share capital and outstanding contributions. If the company is in liquidation, enable the liquidation flag and record the liquidator or liquidators.

## Branches and international entities
A registered branch can carry its own branch name, address and registration information while retaining the underlying legal entity identity. Tax residence is stored separately from establishment country because the two concepts may differ.

## Tax registrations
Maintain the domestic tax number and/or VAT/GST registration. Additional registrations can be stored as `CC | TYPE | IDENTIFIER` so a company can hold multiple registrations in different jurisdictions. Fiscal-representative data is conditional and should only be completed where such representation actually exists.

OSS and IOSS identifiers are stored separately. Sensitive identifiers such as IOSS are **not part of the normal printable document identity** and are not automatically emitted on quotes, invoices or shipping documents.

## Electronic invoicing
The electronic invoicing section stores the seller electronic address/endpoint and scheme plus an optional Leitweg-ID. Depot maps the publishable Company identity into the EN 16931/XRechnung seller party so the structured invoice and human-readable documents use the same issuer source.

## International trade and customs
EORI, REX, AEO and customs references can be maintained for applicable international-trade workflows. Default Incoterms 2020 and named place/port are reusable defaults only; the actual transaction or shipment must override them whenever the commercial agreement differs.

Customs account references and other restricted identifiers are deliberately excluded from the normal document issuer snapshot. HS/commodity code, country of origin, customs value, export-control classification and sanctions decisions belong to product/transaction/shipment data rather than Company master data.

## Regulatory and market identifiers
Optional fields cover LEI, GLN, D-U-N-S, Packaging/EPR, WEEE, battery registrations and additional country-specific registrations. These values are stored because they may be required in specific markets, not because every company must have them.

## Contact and payment data
General phone, email and website fields can be used on generated documents. The invoice contact email can differ from the general contact address. Bank account holder, bank name, IBAN and BIC are available for payment instructions. A SEPA creditor identifier can be stored where Direct Debit is used.

## Document output
Normal generated PDFs now use the Company profile for:

- issuer/display name and PDF metadata;
- registered postal address;
- register/management disclosure where configured;
- tax number and/or VAT ID;
- bank-transfer details;
- general company contact details.

The printable issuer model intentionally contains only publication-safe fields. IOSS, internal customs-account references and unrelated regulatory identifiers cannot leak into a normal document simply because they exist in Company master data.

## Document defaults
Default currency, language, payment terms and Incoterm/location provide reusable defaults for future transaction workflows. Additional legal footer text is available for disclosures that are applicable but not represented by a dedicated field.

## Validation and concurrency
Depot displays blocking validation errors and non-blocking recommendations while the profile is edited. Saving is blocked while required structural/legal fields are invalid. Business-document generation performs the same validation again at point of use. The company profile uses optimistic concurrency; if another user saves a newer version first, reload the current data before saving again.

## Permissions
Viewing the administration page requires `Settings.View`; changing the company profile requires `Settings.Manage`. Sales users do not need settings-administration permission merely to create an authorized sales document: the document pipeline reads only the sanitized issuer snapshot required for that document.

## Related topics
- [Sales Quotes](topic:sales.quotes)
- [Sales Invoices and Credit Notes](topic:sales.invoices)
- [Database Configuration](topic:administration.database)
