# Electronic invoicing technical baseline

## Scope

Depot provides an EN 16931-oriented semantic invoice model and a deterministic UN/CEFACT Cross Industry Invoice (CII) XML generator targeted at the German XRechnung profile. This is a technical implementation baseline, not a statement that every generated document is legally or profile-conformant for every recipient or future XRechnung release.

## Authoritative data

The authoritative invoice data is the structured `ElectronicInvoice` semantic model. Human-readable PDF or other renderings are derived representations and must never replace the structured invoice data when electronic invoicing is in scope.

The model covers invoice identity/type/date, currency, buyer reference, purchase-order reference, seller and buyer identification/address/tax data, payment data, line quantities/prices/discounts/tax categories and free-text notes. Amounts are calculated deterministically with explicit decimal rounding.

## XRechnung

`ElectronicInvoiceService` creates UN/CEFACT CII XML and identifies the XRechnung 3.0 guideline in the document context. The service performs an application-level pre-validation of mandatory semantic terms and numeric invariants before XML generation.

Application-level validation is intentionally not described as complete KoSIT validation. Production exchange must additionally run the generated XML through the currently applicable official XRechnung validation configuration and business rules. The applicable profile/version can change independently of Depot.

## ZUGFeRD / Factur-X

ZUGFeRD/Factur-X is a hybrid PDF/A-3 plus embedded structured XML format. Depot's semantic model and CII generator provide the structured-data foundation. Creation of a conforming PDF/A-3 container, embedded XML metadata and profile-specific validation is deferred until Depot has a production invoice/PDF pipeline that can preserve PDF/A conformance and validate the resulting hybrid document end-to-end. A normal PDF with an XML attachment must not be labelled ZUGFeRD/Factur-X.

## Corrections and credits

Electronic invoices are business records. Final invoice data must not be silently overwritten. Corrections use explicit credit/correction transactions and retain the original record/reference, consistent with Depot's Phase 4 business-record integrity rules. The semantic model supports invoice and credit-note type codes; persistence/workflow integration must preserve the existing immutable-record model when invoicing becomes an operational workflow.

## Validation and release acceptance

Automated tests cover mandatory business-term rejection, deterministic CII generation, profile identification and amount calculation. Before production electronic exchange, release acceptance must additionally verify representative documents with the current external XRechnung validator and recipient/channel requirements (for example Leitweg-ID/buyer reference, electronic address schemes and Peppol requirements where applicable).

## Security and privacy

Electronic invoice XML can contain personal/contact and financial data. Existing Depot privacy, access-control, audit, retention, export and backup requirements apply to the structured source and every generated representation.
