# Phase 6 Technical Status — Electronic Invoicing Readiness

Date: 2026-08-22

## Status

**TECHNICAL FOUNDATION IMPLEMENTED — 2026-08-22**

Phase 6 is implemented to the extent possible without an operational invoice persistence/workflow, production PDF/A-3 pipeline, recipient-specific routing configuration, or bundling/versioning the external official XRechnung validation assets.

## Implemented

- [x] EN 16931-oriented semantic invoice model with seller/buyer, payment, line, tax and reference business terms.
- [x] Invoice and credit-note semantic type codes.
- [x] Deterministic decimal amount calculation and invariant-culture serialization.
- [x] UN/CEFACT Cross Industry Invoice XML generation targeted at XRechnung 3.0.
- [x] Application-level validation of mandatory XRechnung business terms and numeric invariants before generation.
- [x] Structured invoice data is explicitly defined as authoritative; human-readable representations are derived.
- [x] Correction/credit behavior is aligned with the immutable business-record rules established in Phase 4.
- [x] ZUGFeRD/Factur-X architecture is evaluated and the CII semantic/XML foundation is reusable for a future hybrid PDF/A-3 implementation.
- [x] Automated tests cover mandatory-field validation, deterministic generation, profile identification and representative tax/totals.

## Remaining production/integration gates

1. Integrate the semantic invoice model into an actual persisted invoice workflow once Depot operationally creates invoices.
2. Preserve issued invoice XML and references as immutable authoritative business evidence in that workflow.
3. Run generated documents through the current official KoSIT/XRechnung validation configuration and business rules in CI/release acceptance; profile assets must be version-pinned and updated deliberately.
4. Add recipient/channel-specific requirements such as Peppol identifiers/routing when a supported delivery channel is selected.
5. Build and validate a PDF/A-3 pipeline before claiming ZUGFeRD/Factur-X support.
6. Add representative conformance fixtures from the actual supported invoice scenarios, including exemptions, allowances/charges, multiple VAT categories, reverse charge, intra-EU/export and credit/correction cases as those business cases enter product scope.
7. Obtain tax/legal review of the supported invoice scenarios and mandatory organization-specific seller/payment data before production use.

## Evidence

- `src/Depot/Models/ElectronicInvoice.cs`
- `src/Depot/Services/ElectronicInvoiceService.cs`
- `tests/Depot.Tests/ElectronicInvoiceTests.cs`
- `docs/compliance/ELECTRONIC_INVOICING.md`
- Phase 4 immutable business-record and evidence controls
