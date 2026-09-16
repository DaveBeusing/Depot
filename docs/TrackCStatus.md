# Track C acceptance and product decision

Reviewed: 2026-09-16

## F2 validation follow-up

PR #36 (`electronic-invoice-completion`) was merged into `master` as
`823cdcec693afd42660ffbca060669e0c2e0646c`. Merge status is not acceptance:
the provider and packaged E2E gates failed, while CI, quality and security
had not all completed successfully. F2 therefore remains pending acceptance.

The validation follow-up on `electronic-invoice-validation` addresses:

- a provider test that incorrectly relabelled the current Sales schema as
  version 10, causing forward-only migrations to replay against existing tables;
- an incomplete version 8 migration fixture and a stale version 12 assertion;
- a Finance version 3 fixture that lacked the Core and historical Finance
  structures required by the transitive Sales migration;
- the finalization regression's outdated error assertion after explicit VAT
  semantics were introduced;
- unattended packaged E2E signing hanging while importing a root certificate
  into the interactive CurrentUser trust store. The disposable hosted runner
  uses LocalMachine trust and removes the exact ephemeral certificate afterward.

Historical upgrade coverage uses actual version 8 and version 10 structures.
The version 10 regression checks retained cost-profile data, reservation
uniqueness, pricing and electronic-invoice evidence, and repeated migration.
Explicit zero-rated finalization is checked alongside rejection of zero-rated
lines labelled as standard VAT.

This follow-up changes tests and workflow execution, not persisted contracts:
Depot `0.15.190-preview`; Sales schema `13`; Core schema `30`;
DepotManager `0.1.23-preview` unchanged. Final acceptance requires successful
CI, quality, security, Release, packaged E2E, provider and electronic-invoice
conformance results for the final head. The existing KoSIT workflow validates
its representative fixture; a green result alone does not prove every F2
tax case and credit-note output has external validator coverage.

## F3 blocked: explicit product commitment is missing

The Track C definition permits F3 (`zugferd-facturx`) only when ZUGFeRD/Factur-X
is intended to become part of the product promise. No explicit approval of
that commitment was found in the reviewed project and repository material.

Evidence reviewed:

- [Electronic Invoice Completion](compliance/ElectronicInvoiceCompletion.md)
  explicitly excludes a ZUGFeRD/Factur-X claim from F2.
- [Electronic Invoicing](compliance/ElectronicInvoicing.md) and
  [Phase 6 Status](compliance/Phase6Status.md) describe a future PDF/A-3 path.
- [Release 1.0](Release1.0.md) and [Security Roadmap](SecurityRoadmap.md)
  require PDF/A-3 before a claim; these are technical conditions, not a product
  decision to offer the feature.
- [Licensing Architecture](LicensingArchitecture.md) lists XRechnung/ZUGFeRD
  as an example entitlement, without committing to delivery or supported profiles.

F3 implementation and its branch have not been started. To unblock it, the
product owner must record the decision to offer ZUGFeRD/Factur-X and the intended
supported profile/version scope. Implementation must then satisfy PDF/A-3,
embedded XML, metadata, deterministic integrity/archive evidence and independent
validator acceptance using immutable finalized invoice evidence.

## Track status and next work

F1 is merged (PR #35). F2 is merged but acceptance remains open; F3 is blocked
by the product decision. F4 and F5 have not been started in this follow-up.
The next immediate work is completing F2 gates; the next feature package is
F3 only after the documented product decision. No later package is silently
substituted for this decision.
