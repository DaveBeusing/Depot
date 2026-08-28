# Compliance overview

Updated: 2026-08-28

Depot's compliance/security roadmap separates implemented technical controls from legal or deployment-specific claims. Phases 1-7 have technically implementable controls in place; remaining acceptance gates are tracked in `docs/SECURITY_ROADMAP.md`, `docs/RELEASE_1_0.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/compliance`.

Technical baselines currently cover software supply-chain security, authentication/RBAC hardening, privacy discovery/export, business-record integrity, CRA evidence/update/vulnerability processes, Company/document identity, immutable issuer/buyer snapshots, Sales Invoice XRechnung finalization/integrity verification, representative KoSIT validation, software-quality/accessibility gates, Finance F0, and Finance F1 General Ledger controls.

## Finance F0

Finance F0 adds explicit legal entities, currencies, sourced exchange rates, periods, accounting books, charts/accounts, tax registrations, dimensions, number sequences, localization/tax-determination interfaces, provider-neutral Finance schema versioning, and dedicated Finance RBAC. It deliberately seeds no jurisdiction, currency, tax rate, chart of accounts, accounting standard, or statutory rule.

## Finance F1

Finance F1 raises the Finance feature schema to **2** and adds technical accounting-control building blocks:

- balanced double-entry validation in transaction and reporting currency;
- immutable posted journal entries/lines;
- explicit linked reversal instead of destructive correction;
- persisted transaction/reporting currency and exchange-rate snapshots;
- open accounting-period/date/legal-entity checks;
- account/chart/direct-posting validation;
- required-dimension validation;
- deterministic operation/source idempotency;
- transactional Finance number allocation;
- atomic central Audit Log persistence with rollback on audit failure;
- optimistic posting-profile concurrency;
- service-layer GL/posting-profile authorization;
- separate sensitive authorization for free manual journals.

These controls improve traceability, reproducibility, segregation of duties, historical integrity, and retry safety. They are engineering controls, not a declaration that Depot or a deployment conforms to a specific accounting, tax, audit, or records regime.

See `docs/FINANCE_COMPLIANCE.md` for the detailed Finance-specific boundary.

## No certification claim

F0/F1 do not establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, SAF-T, DATEV, XBRL, statutory-reporting, statutory-retention, audit, or tax-filing conformity. Those outcomes depend on localization, configuration, organizational procedures, deployment controls, legal/accounting review, source/subledger integration, and jurisdiction-specific acceptance.

The electronic-invoice technical boundary remains separate: special tax semantics, electronic credit-note finalization, recipient/channel configuration, and every advertised production scenario still require applicable implementation/acceptance.

This documentation is engineering evidence and does not itself certify Depot against ISO, CRA, GDPR/DSGVO, GoBD, EN 16931, XRechnung, WCAG, accounting standards, tax law, or another legal/standards framework. A stored SHA-256 fingerprint is an application integrity control, not a digital signature or independent authenticity proof.
