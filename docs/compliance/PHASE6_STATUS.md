# Phase 6 Technical Status — Electronic Invoicing Readiness

Date: 2026-08-22

## Status

**TECHNICAL IMPLEMENTATION COMPLETE — 2026-08-22**

Phase 6 is implemented to the extent possible without an operational persisted invoice workflow, production PDF/A-3 pipeline, recipient-specific routing configuration, or deployment-specific tax/legal decisions.

## Implemented

- [x] EN 16931-oriented semantic invoice model with seller/buyer, payment, line, tax and reference business terms.
- [x] Invoice and credit-note semantic type codes.
- [x] Deterministic decimal amount calculation and invariant-culture serialization.
- [x] UN/CEFACT Cross Industry Invoice XML generation targeted at XRechnung 3.0.
- [x] Application-level validation of mandatory XRechnung business terms and numeric invariants before generation.
- [x] Structured invoice data is explicitly defined as authoritative; human-readable representations are derived.
- [x] Correction/credit behavior is aligned with the immutable business-record rules established in Phase 4.
- [x] ZUGFeRD/Factur-X architecture is evaluated and the CII semantic/XML foundation is reusable for a future hybrid PDF/A-3 implementation.
- [x] Representative generated CII is bound to a committed conformance fixture to detect generator drift.
- [x] A dedicated CI workflow runs the representative fixture through pinned KoSIT validator and XRechnung configuration releases.
- [x] Automated tests cover mandatory-field validation, deterministic generation, profile identification, representative tax/totals, credit-note type and fixture drift.

## Pinned conformance assets

The repository conformance workflow currently pins:

- KoSIT Validator `1.6.2`
- XRechnung Validator Configuration `3.0.2 / 2026-01-31`
- Java 17 runtime in GitHub Actions

These versions are deliberate release inputs and must be updated through review rather than following moving `latest` references.

## Remaining production/integration gates

1. Integrate the semantic invoice model into an actual persisted invoice workflow once Depot operationally creates invoices.
2. Preserve issued invoice XML and original/correction relationships as immutable authoritative business evidence in that workflow.
3. Add organization-specific seller/payment master data and recipient/channel configuration.
4. Extend the conformance fixture set for every supported tax/business scenario, including exemptions, allowances/charges, multiple VAT categories, reverse charge, intra-EU/export and credit/correction scenarios before those scenarios are declared supported.
5. Add Peppol/electronic-address/routing validation when a supported electronic delivery channel is selected.
6. Build and validate a PDF/A-3 pipeline before claiming ZUGFeRD/Factur-X support.
7. Obtain tax/legal review of supported invoice scenarios before production use.

## Evidence

- `src/Depot/Models/ElectronicInvoice.cs`
- `src/Depot/Services/ElectronicInvoiceService.cs`
- `tests/Depot.Tests/ElectronicInvoiceTests.cs`
- `tests/Depot.Tests/Fixtures/ElectronicInvoice/xrechnung-cii-basic.xml`
- `scripts/einvoice/validate-xrechnung.ps1`
- `.github/workflows/electronic-invoice-conformance.yml`
- `docs/compliance/ELECTRONIC_INVOICING.md`
