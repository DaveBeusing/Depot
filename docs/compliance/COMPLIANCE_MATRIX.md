# Depot Compliance Matrix

Updated: 2026-08-28

## Purpose

This matrix tracks regulatory, standards and assurance areas potentially relevant to Depot. It is a planning/evidence tool, not a declaration of compliance, certification or legal opinion.

| Area | Type | Current technical direction | Priority |
| --- | --- | --- | --- |
| EU Cyber Resilience Act | Regulation | Secure lifecycle, SBOM, vulnerability/update process, release evidence | Critical |
| GDPR / DSGVO | Regulation | Privacy by design, minimization, RBAC, audit, retention/export procedures | Critical |
| GoBD | German administrative requirements | Immutable/correction-oriented records, traceable subledger/GL linkage, auditability, reproducible processing | High when Finance is used |
| EN 16931 / German e-invoicing | Standard/regulatory ecosystem | Sales XRechnung finalization/integrity with explicit scope boundaries | High when applicable |
| Accounting standards / statutory bookkeeping | Deployment/jurisdiction-specific | Jurisdiction-neutral Finance core F0-F3; localization and operator acceptance remain separate | High when Finance is used |
| Receivables / dunning / collections | Deployment/jurisdiction-specific | Controlled AR/dunning evidence; legal wording/fees/escalation remain external | High when AR is used |
| Payables / invoice approval / matching | Deployment/jurisdiction-specific | Controlled AP lifecycle, fail-closed matching, explicit exception approval, retained settlement/reversal evidence | High when AP is used |
| ISO/IEC 27001 / 27034 | Management/application security | Product/development controls and evidence mapping | High enterprise value |
| ISO/IEC 25010 | Software quality | Release/quality gates | High |
| OWASP ASVS / SAMM | Industry guidance | Secure-development verification | High |
| WCAG 2.2 / EN 301 549 | Accessibility | Keyboard/focus/contrast/screen-reader/scaling acceptance | Medium/High |
| NIS2 | Customer/supply-chain context | Security evidence for regulated customers | Context-dependent |

## Evidence mapping

| Control/evidence | CRA | GDPR | GoBD | Security/enterprise | Accounting |
| --- | :---: | :---: | :---: | :---: | :---: |
| RBAC / least privilege | X | X | X | X | X |
| Audit trail | X | X | X | X | X |
| Immutable/correction transactions |  |  | X | X | X |
| Balanced double entry |  |  | X |  | X |
| Source/operation idempotency |  |  | X | X | X |
| Period/date/legal-entity validation |  |  | X |  | X |
| Historical currency/FX snapshots |  |  | X |  | X |
| Atomic number allocation + audit rollback |  |  | X | X | X |
| AR source/open-item/journal traceability |  | X | X | X | X |
| AR payment/allocation/reversal evidence |  | X | X | X | X |
| AP supplier-document/open-item/journal traceability |  | X | X | X | X |
| AP supplier payment/allocation/reversal evidence |  | X | X | X | X |
| AP fail-closed PO/receipt/invoice matching |  |  | X | X | X |
| AP explicit match-exception approval/reason |  | X | X | X | X |
| Segregated AP approval permissions |  |  | X | X | X |
| Backup/recovery tests | X | X | X | X | X |
| SBOM/vulnerability management | X |  |  | X |  |
| Release signing/evidence | X |  |  | X | X |
| E-invoice validation/integrity |  |  | X |  | X |

## Finance F3 evidence boundary

Finance F3 extends the immutable F1 General Ledger and F2 subledger model with a supplier subledger. Supplier-document posting creates AP and GL evidence in one controlled transaction. Supplier payments, allocations and reversals preserve original evidence rather than rewriting source documents.

For PO-linked supplier invoices, matching uses supplier identity, PO price, non-reversed received quantity and previously invoiced quantity. Generic Finance does not apply an implicit tolerance. A mismatch requires explicit match-exception approval with a dedicated permission and retained reason.

These controls strengthen technical evidence for AP traceability, authorization, reconciliation and correction history. They do **not** determine tax deductibility, VAT/GST treatment, statutory inbound e-invoice compliance, payment-law requirements, statutory retention or conformity with HGB/IFRS/US-GAAP/GoBD.

Production Finance/AP use additionally requires approved charts/books/profiles, provider acceptance, AP-to-GL reconciliation, payment evidence/reconciliation, organization-specific approval/SoD rules, retention/export procedures, localization and qualified accounting/legal review.

## Governance

Before production release, each applicable Critical/High area should have an owner, applicability decision, mapped controls, verification evidence, known gaps/risks and review date. Finance governance should additionally identify legal entity, currency, chart/book, calendar, posting-profile owner, exchange-rate source, AR/AP configuration owners, reconciliation procedures, supplier-document approvers, match-exception authority, payment evidence and retention/localization responsibilities.

## Disclaimer

Regulatory applicability depends on how and where Depot is distributed, configured and used. This matrix must be reviewed with qualified legal/compliance/accounting expertise before use as evidence of formal conformity.
