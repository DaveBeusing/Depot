# Company master data legal mapping

This mapping explains why Depot stores each company-level data group. It is a technical implementation aid, not legal advice and not an exhaustive list of every jurisdiction worldwide.

| Data group | Typical use | Depot treatment |
|---|---|---|
| Legal name, legal form, address, country | Business correspondence, contracts, VAT invoices | Core structured fields; projected into document identity and frozen for posted financial documents |
| Registered office, register authority/type/number | Registered-company correspondence | Required when `IsRegisteredEntity`; German seat additionally validated; included in publishable issuer projection |
| Legal representatives / management | Corporate correspondence where local company law requires organ disclosure | Required for German GmbH/UG/AG baseline; included where configured in issuer disclosure |
| Supervisory-board chair | German GmbH/AG and other entities where applicable | Conditional on `HasSupervisoryBoard` |
| Capital / outstanding contributions | Company-law disclosure if capital is voluntarily/statutorily shown | Conditional on `PublishesShareCapital` |
| Liquidation and liquidators | Winding-up correspondence | Conditional on `IsInLiquidation` |
| Branch name and registration | Registered branch correspondence, especially foreign branches | Conditional on `IsBranch` |
| Tax number, VAT/GST registrations | VAT/GST invoices and tax reporting | At least one tax registration required; additional registrations supported by country; publication-safe identifiers can enter issuer snapshot |
| Fiscal representative | VAT regimes requiring representative disclosure | Conditional on `HasFiscalRepresentative` |
| E-invoice endpoint/scheme, Leitweg-ID | EN 16931/XRechnung/Peppol-style routing and seller identification | Dedicated structured fields; publication-safe seller values enter the electronic-invoice projection |
| EORI | EU customs import/export/transit | Optional generally; operationally required before applicable EU customs use |
| REX | Preferential-origin declaration where Registered Exporter rules apply | Optional conditional registration |
| AEO | Authorized Economic Operator status | Optional conditional authorization |
| IOSS/OSS | EU e-commerce VAT schemes | Stored separately; IOSS treated as restricted/not routinely printable |
| LEI | Regulated financial-market identification | Optional |
| GLN | Supply-chain/e-business identification | Optional |
| D-U-N-S | Commercial/procurement counterparty identification | Optional |
| Packaging/EPR, WEEE, battery IDs | Producer-responsibility / product-market obligations | Dedicated common fields plus generic per-country registrations |
| Regulatory authority / professional title | Regulated professions or supervised businesses | Optional conditional disclosure |
| IBAN/BIC | Bank-transfer instructions | Optional generally; structurally validated and required by the current finalized XRechnung bank-transfer path |
| SEPA creditor identifier | SEPA Direct Debit | Optional conditional identifier |
| Incoterm + named place | International commercial terms | Default only; transaction overrides are mandatory where different |

## Historical-document treatment

Company master data is mutable because legal, tax, contact and banking details can change. Posted financial documents are not. Depot therefore separates the current Company record from the `DocumentIssuerProfile` captured for an issued document.

Posted Sales Invoices and Sales Credit Notes store an immutable issuer snapshot. Sales Invoice posting additionally stores the Buyer snapshot and exact issued XRechnung XML in `SalesInvoiceFinalizations`. A later change to Company master data applies to future documents and must not silently alter the historical seller identity of an already posted record.

## Not company master data

The following remain product, customer, or transaction data and must not be inferred from Company master data: invoice number/date, customer tax identifier, Buyer Reference, buyer electronic endpoint, buyer billing address, supply date, VAT rate/exemption/reverse-charge reason, HS/commodity code, country of origin, customs value, export-control/ECCN classification, sanctions screening result, dangerous-goods data, shipment weight/packages and transaction-specific Incoterm.

The current sales-invoice finalization path deliberately fails closed for zero-rated, exempt, and reverse-charge lines because explicit EN 16931 tax-category and exemption/reason semantics are not yet persisted in the commercial line model.
