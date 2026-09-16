# Track C acceptance and product decision

Reviewed: 2026-09-16

## F2 validation follow-up

PR #36 (`electronic-invoice-completion`) introduced the bounded F2 production path. PR #37 repaired the provider/migration and packaged-E2E workflow regressions identified after that merge. PR #38 (`packaged-e2e-acceptance-repair`) then removed stale packaged-version assumptions and hardened executable corruption detection; it is merged into `master`.

F2's remaining acceptance gap is external coverage breadth. The previous KoSIT workflow validated one representative Standard-rated invoice fixture, so a green result did not prove the other production VAT categories or the electronic Credit Note output.

The `electronic-invoice-conformance-closure` package closes that evidence gap by binding retained conformance fixtures to the real `ElectronicInvoiceService` generator and validating the complete bounded issuance matrix externally:

- Standard-rated Invoice (`S`);
- Zero-rated Invoice (`Z`);
- Exempt Invoice (`E`) with exemption evidence;
- Reverse-charge Invoice (`AE`) with exemption evidence;
- Standard-rated Credit Note (`381`).

The generator/fixture equality tests run before KoSIT. The conformance script validates every `xrechnung-cii-*.xml` fixture against the pinned KoSIT Validator `1.6.2` and XRechnung `3.0.2` configuration release `2026-01-31`. BuyerReference and electronic endpoint data remain present in the fixtures; routing persistence is covered by Sales finalization regressions because F2 does not implement an external transport platform.

This closure changes test/evidence assets and workflow execution only. Persisted contracts remain unchanged: Core schema `30`, Sales schema `13`, DepotManager `0.1.23-preview`. Depot moves from `0.15.191-preview` to `0.15.192-preview` for the repository commit.

F2 becomes accepted only when the final package head passes its generator-bound fixture tests, the complete KoSIT matrix and the repository CI/quality/security/provider/release/packaged-E2E gates. Until then F2 remains pending acceptance.

## F3 blocked: explicit product commitment is missing

The Track C definition permits F3 (`zugferd-facturx`) only when ZUGFeRD/Factur-X is intended to become part of the product promise. No explicit approval of that commitment was found in the reviewed project and repository material.

Evidence reviewed:

- [Electronic Invoice Completion](compliance/ElectronicInvoiceCompletion.md) explicitly excludes a ZUGFeRD/Factur-X claim from F2.
- [Electronic Invoicing](compliance/ElectronicInvoicing.md) and [Phase 6 Status](compliance/Phase6Status.md) describe a future PDF/A-3 path.
- [Release 1.0](Release1.0.md) and [Security Roadmap](SecurityRoadmap.md) require PDF/A-3 before a claim; these are technical conditions, not a product decision to offer the feature.
- [Licensing Architecture](LicensingArchitecture.md) lists XRechnung/ZUGFeRD as an example entitlement, without committing to delivery or supported profiles.

F3 implementation and its branch have not been started. To unblock it, the product owner must record the decision to offer ZUGFeRD/Factur-X and the intended supported profile/version scope. Implementation must then satisfy PDF/A-3, embedded XML, metadata, deterministic integrity/archive evidence and independent validator acceptance using immutable finalized invoice evidence.

## Track status and next work

F1 is merged. F2 is functionally merged and its conformance-closure package is in progress; acceptance remains gate-dependent. F3 remains blocked by the product decision. F4 and F5 have not been started.

After F2 acceptance, the next implementation package is the status/version drift hardening package unless the product owner first records the F3 product decision.
