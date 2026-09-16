# Track C acceptance and product decision

Reviewed: 2026-09-16

## F2 validation follow-up

PR #36 (`electronic-invoice-completion`) introduced the bounded F2 production path. PR #37 repaired the provider/migration and packaged-E2E workflow regressions identified after that merge. PR #38 (`packaged-e2e-acceptance-repair`) removed stale packaged-version assumptions and hardened executable corruption detection. PR #39 (`electronic-invoice-conformance-closure`) is now merged into `master` and closes the remaining external-coverage breadth in the repository implementation.

The conformance closure binds retained fixtures to the real `ElectronicInvoiceService` generator and validates the complete bounded issuance matrix externally:

- Standard-rated Invoice (`S`);
- Zero-rated Invoice (`Z`);
- Exempt Invoice (`E`) with exemption evidence;
- Reverse-charge Invoice (`AE`) with exemption evidence;
- Standard-rated Credit Note (`381`).

The generator/fixture equality tests run before KoSIT. The conformance script validates every `xrechnung-cii-*.xml` fixture against the pinned KoSIT Validator `1.6.2` and XRechnung `3.0.2` configuration release `2026-01-31`. BuyerReference and electronic endpoint data remain present in the fixtures; routing persistence is covered by Sales finalization regressions because F2 does not implement an external transport platform.

Persisted contracts remain Core schema `30` and Sales schema `13`; DepotManager remains `0.1.23-preview`. PR #39 changed conformance evidence/workflow assets, not the production persistence contract.

F2 remains acceptance-evidence dependent. At the status review immediately after the merge, GitHub still reported the final PR-head CI, quality, security, provider, release, packaged-E2E and electronic-invoice conformance workflows as queued rather than completed. Merge status therefore must not be represented as final acceptance. F2 becomes accepted only when the required candidate evidence is green.

## F3 blocked: explicit product commitment is missing

The Track C definition permits F3 (`zugferd-facturx`) only when ZUGFeRD/Factur-X is intended to become part of the product promise. No explicit approval of that commitment was found in the reviewed project and repository material.

Evidence reviewed:

- [Electronic Invoice Completion](compliance/ElectronicInvoiceCompletion.md) explicitly excludes a ZUGFeRD/Factur-X claim from F2.
- [Electronic Invoicing](compliance/ElectronicInvoicing.md) and [Phase 6 Status](compliance/Phase6Status.md) describe a future PDF/A-3 path.
- [Release 1.0](Release1.0.md) and [Security Roadmap](SecurityRoadmap.md) require PDF/A-3 before a claim; these are technical conditions, not a product decision to offer the feature.
- [Licensing Architecture](LicensingArchitecture.md) lists XRechnung/ZUGFeRD as an example entitlement, without committing to delivery or supported profiles.

F3 implementation and its branch have not been started. To unblock it, the product owner must record whether ZUGFeRD/Factur-X is part of the product promise and, if yes, the intended supported profile/version scope. Implementation must then satisfy PDF/A-3, embedded XML, metadata, deterministic integrity/archive evidence and independent validator acceptance using immutable finalized invoice evidence.

If the product decision is to defer ZUGFeRD/Factur-X, F3 must be explicitly marked deferred rather than silently skipped before Track C proceeds to F4.

## Track status and next work

F1 is merged. F2 repository implementation and conformance breadth are merged; final acceptance remains evidence-dependent. F3 is blocked by the explicit product decision. F4 and F5 have not been started.

The repository-wide status/version drift hardening package runs outside the Track C feature sequence and removes stale canonical baseline values while adding automated consistency checks. After that package, the next Track C action is the F3 product decision gate; only the recorded decision determines whether the next feature branch is F3 or F4.
