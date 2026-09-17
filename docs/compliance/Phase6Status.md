# Phase 6 Technical Status — Electronic Invoicing Readiness

Date: 2026-09-17

## Status

**BOUNDED REPOSITORY IMPLEMENTATION AND CONFORMANCE COMPLETE — 2026-09-17**

Depot's advertised repository scope now includes bounded XRechnung 3.0 CII issuance plus ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile PDF/A-3B hybrid artifacts. The implementation and independent repository conformance paths are present; deployment-specific routing, legal/tax acceptance and any scenarios outside the advertised matrix remain separate production decisions.

## Implemented bounded scope

- [x] EN 16931-oriented semantic invoice model with seller/buyer, payment, line, tax and reference business terms.
- [x] Deterministic XRechnung 3.0 CII generation with application-level validation before issuance.
- [x] Authoritative Company seller data and structured Customer Buyer identity with Buyer Reference, electronic address/scheme, tax identity and billing address.
- [x] Atomic Sales Invoice finalization with immutable seller/buyer snapshots, exact issued XML, SHA-256 evidence and transactional Audit/business state.
- [x] Explicit advertised VAT semantics for Standard-rated (`S`), Zero-rated (`Z`), Exempt (`E`) and Reverse-charge (`AE`) issuance instead of inferring special semantics from a numeric 0% rate.
- [x] Electronic Sales Credit Note finalization for the bounded advertised scope, including immutable Buyer/routing evidence, exact XML retention and integrity verification.
- [x] Persisted recipient/routing evidence kept separately from immutable XML meaning so later transport handling cannot silently rewrite the issued document.
- [x] Posted Invoice and Credit Note export uses retained finalized evidence rather than mutable current master data.
- [x] Legacy posted records without historical finalization fail closed instead of being reconstructed silently.
- [x] ZUGFeRD 2.5.2 / Factur-X 1.09.2 XRECHNUNG-profile hybrid generation uses the same finalized `xrechnung.xml` payload and retains exact PDF/XML SHA-256 evidence.
- [x] Hybrid documents are generated as PDF/A-3B artifacts with embedded `xrechnung.xml`, Factur-X XMP metadata and `AFRelationship=Alternative`.
- [x] The bounded five-document matrix covers `S`, `Z`, `E`, `AE` invoices and a Standard-rated Credit Note (`381`).
- [x] KoSIT validates the bounded XRechnung matrix independently of PDF/A validation.
- [x] Pinned veraPDF validates the advertised hybrid PDF/A-3B matrix independently of KoSIT.
- [x] Provider-neutral persistence/migration coverage exists for the electronic-invoice evidence boundary.

## Conformance assets

The repository intentionally pins validation tool/configuration inputs rather than following moving `latest` references. The electronic-invoice workflow currently uses the repository-pinned KoSIT/XRechnung assets and veraPDF 1.30.2 for the advertised matrix. Changes to those inputs require review because they are part of the acceptance evidence chain.

## Remaining production/integration gates

These are not missing implementation for the bounded repository scope:

1. Configure and approve organization/recipient-specific transport channels, including Peppol or other delivery-network rules where a deployment advertises them.
2. Add separate implementation and conformance evidence before advertising tax/business/profile scenarios outside the current bounded matrix, such as additional allowance/charge combinations, multi-VAT cases, intra-EU/export variants or jurisdiction-specific extensions.
3. Revalidate a concrete production release against the then-applicable regulatory/conformance inputs where required by the target deployment or market.
4. Define and approve operational handling for historical records that predate immutable electronic-invoice finalization.
5. Obtain organization-specific accounting, tax and legal acceptance for the actual marketed/deployed scenarios.

## Evidence

- `src/Depot/Models/ElectronicInvoice.cs`
- `src/Depot/Models/DocumentBuyerProfile.cs`
- `src/Depot/Services/ElectronicInvoiceService.cs`
- `src/Depot/Services/SalesInvoiceFinalizationService.cs`
- `src/Depot/Services/SalesCreditNoteFinalizationService.cs`
- `src/Depot/Services/ZugferdFacturXService.cs`
- `tests/Depot.Tests/ElectronicInvoiceTests.cs`
- `tests/Depot.Tests/SalesInvoiceFinalizationTests.cs`
- electronic-invoice conformance fixtures under `tests/Depot.Tests/Fixtures/ElectronicInvoice/`
- `scripts/einvoice/validate-xrechnung.ps1`
- `.github/workflows/electronic-invoice-conformance.yml`
- `docs/compliance/ElectronicInvoicing.md`
- `docs/compliance/ElectronicInvoiceCompletion.md`
- `docs/TrackCStatus.md`
