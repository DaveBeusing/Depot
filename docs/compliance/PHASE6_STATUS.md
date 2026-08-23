# Phase 6 Technical Status — Electronic Invoicing Readiness

Date: 2026-08-23

## Status

**TECHNICAL IMPLEMENTATION COMPLETE — 2026-08-23**

Phase 6 now includes the operational Sales Invoice finalization path. Remaining work is limited to additional tax/profile semantics, electronic credit-note finalization, production recipient/channel configuration and validation, PDF/A-3, and deployment-specific tax/legal acceptance.

## Implemented

- [x] EN 16931-oriented semantic invoice model with seller/buyer, payment, line, tax and reference business terms.
- [x] Invoice and credit-note semantic type codes in the semantic/generator layer.
- [x] Deterministic decimal amount calculation and invariant-culture serialization.
- [x] UN/CEFACT Cross Industry Invoice XML generation targeted at XRechnung 3.0.
- [x] Application-level validation of mandatory XRechnung business terms and numeric invariants before generation.
- [x] Administration > Company as authoritative seller/document identity.
- [x] Structured Customer Buyer identity with Buyer Reference (BT-10), electronic address (BT-49) and scheme, tax identity, and structured billing address.
- [x] Atomic Sales Invoice posting/finalization: business posting, issuer snapshot, Buyer snapshot, XML generation and audit share one transaction.
- [x] Exact issued Sales Invoice XML retained in `SalesInvoiceFinalizations` with SHA-256 integrity verification.
- [x] Posted Invoice workspace exports the verified persisted XRechnung XML without regeneration from mutable master data.
- [x] Legacy posted invoices without finalization fail closed rather than being reconstructed silently.
- [x] Unsupported ambiguous tax scenarios fail closed; zero-rated, exempt and reverse-charge lines are not guessed from a numeric 0% rate.
- [x] Correction/credit behavior remains aligned with immutable business-record rules; posted credit notes capture immutable issuer identity.
- [x] ZUGFeRD/Factur-X architecture is evaluated and the CII semantic/XML foundation is reusable for a future hybrid PDF/A-3 implementation.
- [x] Representative generated CII is bound to a committed conformance fixture to detect generator drift.
- [x] A dedicated CI workflow runs the representative fixture through pinned KoSIT validator and XRechnung configuration releases.
- [x] Automated tests cover mandatory-field validation, deterministic generation, profile identification, representative tax/totals, credit-note semantic type, immutable Buyer finalization, exact XML export, hash tamper detection, incomplete Buyer rejection, country-code syntax and unsupported 0% tax rejection.

## Pinned conformance assets

The repository conformance workflow currently pins:

- KoSIT Validator `1.6.2`
- XRechnung Validator Configuration `3.0.2 / 2026-01-31`
- Java 17 runtime in GitHub Actions

These versions are deliberate release inputs and must be updated through review rather than following moving `latest` references.

## Remaining production/integration gates

1. Persist explicit EN 16931 VAT-category and exemption/reason semantics for every commercial tax scenario that Depot intends to issue, including zero-rated, exempt and reverse-charge cases.
2. Extend equivalent Buyer snapshot, exact XML retention and integrity verification to electronic credit notes before advertising that issuance channel.
3. Configure organization/recipient-specific routing and delivery channels, including electronic-address scheme rules and Peppol requirements where applicable.
4. Extend conformance fixtures and validation for every advertised tax/business/profile/channel scenario, including allowances/charges, multiple VAT categories, reverse charge, intra-EU/export and correction cases.
5. Validate production releases against the then-applicable KoSIT/XRechnung assets rather than relying only on the pinned development baseline.
6. Build and validate a PDF/A-3 pipeline before claiming ZUGFeRD/Factur-X support.
7. Define and approve a controlled remediation procedure for legacy posted invoices that predate historical issuer/Buyer/XML finalization.
8. Obtain organization-specific tax/legal review of supported invoice scenarios before production use.

## Evidence

- `src/Depot/Models/ElectronicInvoice.cs`
- `src/Depot/Models/DocumentBuyerProfile.cs`
- `src/Depot/Services/ElectronicInvoiceService.cs`
- `src/Depot/Services/SalesInvoiceFinalizationService.cs`
- `src/Depot/Services/SalesInvoiceService.cs`
- `src/Depot/Services/SalesDocumentService.cs`
- `tests/Depot.Tests/ElectronicInvoiceTests.cs`
- `tests/Depot.Tests/SalesInvoiceFinalizationTests.cs`
- `tests/Depot.Tests/Fixtures/ElectronicInvoice/xrechnung-cii-basic.xml`
- `scripts/einvoice/validate-xrechnung.ps1`
- `.github/workflows/electronic-invoice-conformance.yml`
- `docs/compliance/ELECTRONIC_INVOICING.md`
- `docs/compliance/INVOICE_FINALIZATION.md`
