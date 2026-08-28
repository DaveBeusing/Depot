# Compliance overview

Updated: 2026-08-28

Depot's compliance/security roadmap separates implemented technical controls from legal or deployment-specific claims. Phases 1-7 have technically implementable controls in place; remaining acceptance gates are tracked in `docs/SECURITY_ROADMAP.md`, `docs/RELEASE_1_0.md`, `docs/FINANCE_COMPLIANCE.md`, and `docs/compliance`.

Technical baselines currently cover software supply-chain security, authentication/RBAC hardening, privacy discovery/export, business-record integrity, CRA evidence/update/vulnerability processes, Company/document identity, immutable issuer/buyer snapshots, Sales Invoice XRechnung finalization/integrity verification, representative KoSIT validation, software-quality/accessibility gates, Finance F0, Finance F1 General Ledger controls, and Finance F2 Accounts Receivable controls.

## Finance F0/F1

F0 adds explicit legal entities, currencies, sourced exchange rates, periods, accounting books, charts/accounts, tax registrations, dimensions, number sequences, localization/tax-determination interfaces, provider-neutral Finance schema versioning, and dedicated Finance RBAC. It deliberately seeds no jurisdiction/accounting defaults.

F1 adds balanced immutable double entry, historical FX evidence, open-period/date/legal-entity checks, account/chart/dimension validation, deterministic operation/source idempotency, transactional number allocation, explicit reversal, atomic Audit Log persistence, posting-profile concurrency, and separated manual-journal authorization.

## Finance F2

Finance F2 raises the Finance feature schema to **3** and adds technical customer-subledger controls:

- retained receivable debit/credit open items linked to source document and GL journal;
- configured Sales Invoice/Credit Note → AR → GL atomic processing;
- operation-idempotent customer payment records;
- explicit allocation records for partial/full settlement and unapplied overpayments;
- later credit allocation without rewriting source invoices;
- payment reversal that restores every active allocation and uses a linked GL reversal;
- write-off records with sensitive dedicated authorization and linked reversal;
- aging/customer-statement projections from retained AR evidence;
- configurable dunning policies and retained idempotent dunning-run evidence;
- F2 Sales-schema dependency made explicit in migration;
- service-layer segregation between normal Finance AR operations and sensitive write-off authority.

These controls improve traceability, subledger/GL reconciliation capability, reproducibility, segregation of duties, historical integrity, and retry/correction safety. They are engineering controls, not a declaration that Depot or a deployment conforms to a specific accounting, tax, audit, collections, or records regime.

See `docs/FINANCE_COMPLIANCE.md` for the detailed Finance-specific boundary.

## No certification claim

F0/F1/F2 do not establish HGB, GoBD, IFRS, US-GAAP, VAT/GST/sales-tax, SAF-T, DATEV, XBRL, statutory-reporting, statutory-retention, receivable-valuation/impairment, legal dunning/collections, banking/payment-services, audit, or tax-filing conformity. Those outcomes depend on localization, configuration, organizational procedures, deployment controls, legal/accounting review, reconciliation, retention/export processes, and jurisdiction-specific acceptance.

The electronic-invoice technical boundary remains separate: special tax semantics, electronic credit-note finalization, recipient/channel configuration, and every advertised production scenario still require applicable implementation/acceptance.

This documentation is engineering evidence and does not itself certify Depot against ISO, CRA, GDPR/DSGVO, GoBD, EN 16931, XRechnung, WCAG, accounting standards, tax law, collections law, or another legal/standards framework. A stored SHA-256 fingerprint is an application integrity control, not a digital signature or independent authenticity proof.
