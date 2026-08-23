# Compliance overview

Updated: 2026-08-23

Depot's compliance/security roadmap separates implemented technical controls from legal or deployment-specific claims. Phases 1-7 have technically implementable controls in place; remaining acceptance gates are tracked in `docs/SECURITY_ROADMAP.md`, `docs/RELEASE_1_0.md`, and the phase/status files under `docs/compliance`.

Technical baselines currently cover software supply-chain security, authentication/RBAC hardening, privacy discovery/export, business-record integrity, CRA evidence/update/vulnerability processes, controlled Company/document identity, immutable issuer snapshots, atomic Sales Invoice Buyer/XRechnung finalization with exact issued XML retention and SHA-256 integrity verification, representative KoSIT XRechnung conformance validation, and software-quality/accessibility gates.

The electronic-invoice technical boundary is documented explicitly: special tax scenarios that require explicit EN 16931 category/exemption semantics, Buyer/XML finalization for electronic credit notes, recipient/channel configuration, and validation of every advertised production scenario remain acceptance or implementation work as applicable.

This documentation is engineering evidence and does not itself certify Depot against ISO, CRA, GDPR/DSGVO, GoBD, EN 16931, XRechnung, WCAG, or any other legal/standards framework. A stored SHA-256 fingerprint is an application integrity control, not a digital signature or independent authenticity proof.
