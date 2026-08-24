# Current project status

Updated: 2026-08-23

Depot is on the `0.14.x-preview` line. Security/compliance roadmap phases 1 through 7 have their technically implementable repository/application controls in place. Remaining items are explicitly tracked as production, environment, legal, accessibility, provider, signing, tax-profile/routing, or enterprise acceptance gates.

Key current capabilities include first-run administrator bootstrap, hardened authentication and RBAC, privacy discovery/export, immutable business-record evidence, CRA technical evidence, Company legal-identity master data, immutable seller snapshots for posted invoices/credit notes, atomic Sales Invoice Buyer/XRechnung finalization with exact XML retention and SHA-256 integrity verification, posted-invoice XRechnung export, pinned KoSIT representative conformance validation, software-quality gates, accessibility static checks, SBOM/dependency audit, and release-integrity automation.

Current electronic-invoice boundaries are explicit: zero-rated/exempt/reverse-charge commercial scenarios require persisted EN 16931 tax semantics before issuance, electronic credit-note Buyer/XML finalization remains follow-up work, and production recipient/channel validation remains a release/deployment gate.

Phase 8 enterprise readiness remains planned.
