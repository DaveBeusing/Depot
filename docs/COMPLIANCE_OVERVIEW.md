# Compliance overview

Updated: 2026-08-27

Depot's compliance/security roadmap separates implemented technical controls from legal or deployment-specific claims. Phases 1-7 have technically implementable controls in place; remaining acceptance gates are tracked in `docs/SECURITY_ROADMAP.md`, `docs/RELEASE_1_0.md`, and `docs/compliance`.

Technical baselines currently cover software supply-chain security, authentication/RBAC hardening, privacy discovery/export, business-record integrity, CRA evidence/update/vulnerability processes, Company/document identity, immutable issuer/buyer snapshots, Sales Invoice XRechnung finalization and integrity verification, representative KoSIT validation, software-quality/accessibility gates, and the Finance F0 jurisdiction-neutral accounting foundation.

Finance F0 adds explicit legal entities, currencies, sourced exchange rates, periods, accounting books, charts/accounts, tax registrations, dimensions, number sequences, localization/tax-determination interfaces, provider-neutral Finance schema versioning, and dedicated Finance RBAC. It deliberately seeds no jurisdiction, currency, tax rate, chart of accounts, accounting standard, or statutory rule.

See `docs/FINANCE_COMPLIANCE.md` for the Finance-specific boundary. F0 is technical infrastructure and does not establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, SAF-T, DATEV, statutory-reporting, audit, or tax-filing conformity. General Ledger evidence controls are part of F1 and later packages.

The electronic-invoice technical boundary remains separate: special tax semantics, electronic credit-note finalization, recipient/channel configuration, and every advertised production scenario still require applicable implementation/acceptance.

This documentation is engineering evidence and does not itself certify Depot against ISO, CRA, GDPR/DSGVO, GoBD, EN 16931, XRechnung, WCAG, accounting standards, tax law, or another legal/standards framework. A stored SHA-256 fingerprint is an application integrity control, not a digital signature or independent authenticity proof.
