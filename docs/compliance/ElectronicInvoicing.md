# Electronic invoicing technical baseline

Updated: 2026-09-19

## Scope

Depot implements a bounded electronic-invoicing path based on EN 16931 semantics, UN/CEFACT Cross Industry Invoice (CII), the German XRechnung 3.0 profile and a ZUGFeRD 2.5.2 / Factur-X 1.09.2 hybrid-document path using the `XRECHNUNG` reference profile.

The advertised technical issuance matrix currently covers Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`) and Reverse-charge (`AE`) invoices plus Standard-rated electronic Credit Notes (`381`). Invalid or incomplete tax/profile combinations fail closed instead of being inferred from a numeric tax rate alone.

These are engineering/product capabilities. Production acceptance remains tied to the exact externally validated candidate and does not by itself establish legal, tax, recipient-network or organization-specific compliance.

## Authoritative and historical data

For a finalized Sales Invoice or electronic Sales Credit Note, the authoritative structured representation is the exact CII XML generated during finalization and retained with its SHA-256 digest. Depot does not regenerate an issued XML document later from mutable Company or Customer master data.

Finalization uses immutable issuance evidence:

- the seller `DocumentIssuerProfile` captured for the issued document;
- the immutable `DocumentBuyerProfile` containing the Buyer identity actually used for issuance;
- the posted document transaction data and lines;
- explicit VAT category and exemption evidence where applicable.

The Buyer snapshot retains Buyer Reference, electronic endpoint/scheme, structured billing address, country, tax identifiers and contact data. Sales Credit Notes reuse the immutable Buyer evidence of their source invoice rather than rebuilding the Buyer from current Customer data.

SHA-256 is an application integrity/tamper-detection control. It is not a digital signature and must not be described as independent authenticity or non-repudiation evidence.

## XRechnung generation and posting

`ElectronicInvoiceService` is the single CII generator for the bounded production path. Invoice and Credit Note finalization both use this generator; the hybrid ZUGFeRD/Factur-X path embeds the same finalized output rather than maintaining a second XML implementation.

Posting/finalization is transactional. If required seller/buyer identity, tax semantics, XML generation, routing evidence or hybrid-artifact generation fails, the transaction fails rather than leaving a partially issued electronic document.

The current transfer-payment path requires the applicable payment and Buyer identity data. Recipient/routing evidence is retained separately from the immutable XML so later transport/channel handling cannot silently change what was issued.

## Tax scenarios

Depot explicitly persists the EN 16931 VAT category needed by the advertised bounded matrix.

- `S` — Standard Rated with a positive tax rate;
- `Z` — Zero Rated with a zero tax rate;
- `E` — Exempt with explicit exemption evidence;
- `AE` — Reverse Charge with explicit reverse-charge/exemption evidence.

Generator validation rejects contradictory combinations rather than deriving categories from `0%`. Conformance fixtures cover each advertised invoice tax case and bind retained XML to production generator output before external validation.

## Corrections and electronic Credit Notes

Posted invoices remain immutable and corrections use explicit Credit Notes. Electronic Sales Credit Note finalization is implemented with its own finalized CII XML, SHA-256 integrity evidence, routing evidence and ZUGFeRD/Factur-X hybrid artifact.

Credit Note Buyer identity is derived from the immutable source-invoice finalization. A source invoice without that historical evidence fails closed; Depot does not backfill proof from mutable current customer data.

## ZUGFeRD / Factur-X hybrid artifacts

Sales feature schema 14 adds immutable hybrid-artifact persistence. `ZugferdFacturXService` creates a new PDF/A-3B document during finalization, embeds exactly one structured payload named `xrechnung.xml`, uses `AFRelationship=Alternative`, writes the Factur-X/XRECHNUNG XMP contract and retains the exact PDF bytes.

The visible page layout uses the same validated template infrastructure as normal Sales PDFs. The active Sales Invoice or Credit Note template is resolved at finalization time. Once finalization succeeds, the retained hybrid PDF bytes are authoritative for electronic-invoice export: later template activation, Company changes, or designer edits do not regenerate or replace the stored artifact. Designer Preview is sample-data presentation only and is never accepted as PDF/A/XRechnung evidence.

The persisted hybrid evidence includes:

- ZUGFeRD/Factur-X standard/profile metadata;
- PDF/A conformance declaration;
- embedded XML filename;
- finalized XML SHA-256;
- PDF SHA-256;
- exact PDF bytes;
- creation timestamp.

Loading/export verifies the retained PDF digest and links the artifact back to the finalized XML digest. Legacy posted records without this evidence are not reconstructed from mutable master data.

## Independent validation boundary

Application-level checks and PDFsharp structural tests are not treated as independent conformance evidence.

The electronic-invoice conformance workflow uses two external validation boundaries:

1. the retained five-case XRechnung CII matrix is validated through the pinned KoSIT Validator/XRechnung configuration;
2. the corresponding five production-generated hybrid PDFs are validated independently as PDF/A-3B using pinned veraPDF `1.30.2`.

The veraPDF installer is downloaded from the official release location and checked against the pinned SHA-256 `6cc6341cb1af644044054b81f00a6590a7918abb18f762243de115258bcad838`. The CLI version is checked before use. Validation runs with an explicit PDF/A-3B flavour and the machine-readable report is parsed fail-closed: missing reports, non-compliant results, parse/encryption/validator exceptions or process failures reject the candidate.

The same test data binds both layers: each hybrid artifact is generated only after its CII output matches the corresponding retained KoSIT-bound XML fixture. PDF/A success therefore cannot hide XML/profile drift, and KoSIT success cannot substitute for PDF/A acceptance.

Generated PDFs, a generator manifest, per-document veraPDF reports/logs and the validator summary are retained as CI evidence. Runtime document posting does not invoke KoSIT or veraPDF; external validators are acceptance/build evidence rather than runtime dependencies.

## Export and immutability

Posted XRechnung export writes the verified retained XML. ZUGFeRD/Factur-X export writes the verified retained PDF bytes. Neither export path regenerates an issued representation from current Company or Customer master data.

Historical remediation must use independently verified historical evidence and a controlled process. It must not manufacture an issued-document record from present-day values.

## Security, privacy and retention

Electronic invoice XML and hybrid PDFs can contain personal/contact, tax, banking and commercial data. Existing Depot authorization, Audit, backup, retention, privacy-export and recovery requirements apply to finalization records and exported representations.

Integrity hashes detect application-level tampering but do not replace signing, qualified archival, recipient acknowledgement or jurisdiction-specific retention requirements where those are independently required.

## Related evidence

- `docs/TrackCStatus.md`
- `docs/compliance/ElectronicInvoiceCompletion.md`
- `docs/compliance/InvoiceFinalization.md`
- `src/Depot/Services/ElectronicInvoiceService.cs`
- `src/Depot/Services/SalesInvoiceFinalizationService.cs`
- `src/Depot/Services/SalesCreditNoteFinalizationService.cs`
- `src/Depot/Services/ZugferdFacturXService.cs`
- `tests/Depot.Tests/ElectronicInvoiceConformanceFixtureTests.cs`
- `tests/Depot.Tests/ZugferdFacturXConformanceFixtureTests.cs`
- `scripts/einvoice/validate-xrechnung.ps1`
- `scripts/einvoice/validate-zugferd-facturx.ps1`
- `.github/workflows/electronic-invoice-conformance.yml`
