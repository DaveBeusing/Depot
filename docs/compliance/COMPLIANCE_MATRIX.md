# Depot Compliance Matrix

Updated: 2026-08-28

## Purpose

This matrix tracks regulatory, standards, and assurance areas relevant or potentially relevant to Depot. It is a planning/evidence tool, not a declaration of compliance, certification, or legal opinion.

| Area | Type | Relevance to Depot | Current direction | Priority |
| --- | --- | --- | --- | --- |
| EU Cyber Resilience Act | Regulation | Commercial software/product with digital elements may be in scope depending on distribution/use | Build CRA-ready security lifecycle, risk assessment, SBOM, vulnerability/update process and conformity evidence | Critical |
| GDPR / DSGVO | Regulation | Depot can process user, customer, supplier/contact, audit and financial reference data | Privacy by design, minimization, access control, retention, export/deletion/anonymization procedures | Critical |
| GoBD | German administrative requirements | Relevant when Depot records/processes tax-relevant business transactions/data in Germany | Immutable posted records, traceable corrections, auditability, deterministic processing, retention/export and procedural documentation | High |
| EN 16931 / German e-invoicing profiles | Standard/regulatory ecosystem | Relevant to structured invoice exchange | Maintain semantic invoice model, XRechnung validation/finalization and explicit tax/profile boundaries | High when invoicing is used accordingly |
| Accounting standards / statutory bookkeeping | Jurisdiction/deployment-specific | General Ledger functionality can become accounting evidence when configured/integrated for a deployment | Jurisdiction-neutral core, explicit books/charts/periods/currencies; localization and operator acceptance remain separate | High when Finance is used |
| ISO/IEC 27001 | Management-system standard | Customers may expect controls/evidence compatible with an ISMS | Align product controls and development evidence; certification applies to an organizational ISMS rather than Depot alone | High enterprise value |
| ISO/IEC 27034 | Application-security standard | Directly relevant to secure application development | Use as Secure SDLC guidance | High |
| ISO/IEC 25010 | Software quality model | Relevant to release quality | Map release gates to quality characteristics | High |
| OWASP ASVS / SAMM | Industry guidance | Practical secure-development verification | Maintain control mapping and security tests | High |
| WCAG 2.2 / EN 301 549 | Accessibility standards | Useful for enterprise/public-sector readiness and accessible operation | Integrate keyboard, focus, contrast, screen-reader and scaling acceptance | Medium/High |
| NIS2 | Regulation affecting covered organizations | Depot is not automatically 'NIS2 certified'; customer supply-chain requirements may flow down | Provide security controls/evidence expected by regulated customers | Context-dependent |
| ISO 9001 | Quality management system | Organizational/process value if Depot becomes a managed commercial product | Use disciplined requirements/change/release/customer-feedback processes; certify organization only if justified | Optional |
| SOC 2 | Assurance framework | Primarily relevant if Depot evolves into a hosted/service offering | Reassess if SaaS/cloud service is introduced | Low currently |
| BSI C5 | Cloud security criteria | Primarily relevant to cloud services | Reassess if Depot is offered as cloud service | Low currently |

## Evidence mapping

| Control/evidence | CRA | GDPR | GoBD | ISO 27001/27034 | Enterprise/accounting |
| --- | :---: | :---: | :---: | :---: | :---: |
| Threat model | X |  |  | X | X |
| SBOM | X |  |  | X | X |
| Vulnerability management | X |  |  | X | X |
| Security update/support policy | X |  |  | X | X |
| RBAC / least privilege | X | X | X | X | X |
| Audit trail | X | X | X | X | X |
| Immutable/correction transactions |  |  | X | X | X |
| Finance balanced double entry |  |  | X |  | X |
| Finance source/operation idempotency |  |  | X | X | X |
| Finance period/date/legal-entity validation |  |  | X |  | X |
| Finance historical currency/FX snapshots |  |  | X |  | X |
| Finance atomic number allocation + audit rollback |  |  | X | X | X |
| Finance segregation of manual-journal permission |  |  | X | X | X |
| Data inventory |  | X | X | X | X |
| Retention model | X | X | X | X | X |
| Backup/recovery tests | X | X | X | X | X |
| Release signing | X |  |  | X | X |
| Secure configuration | X | X |  | X | X |
| Accessibility evidence |  |  |  |  | X |
| E-invoice validation |  |  | X |  | X |

## Finance F1 evidence boundary

Finance F1 implements technical controls for immutable General Ledger postings, balancing, source/operation replay safety, period enforcement, explicit reversals, transactional audit evidence, and controlled posting permissions. These are relevant engineering controls for accounting-record integrity and auditability.

They do **not** by themselves establish GoBD, HGB, IFRS, US-GAAP, VAT/GST/sales-tax, statutory retention, filing, export, or audit conformity. Production accounting use additionally requires approved charts/posting profiles, source/subledger integrations and reconciliation, localization, retention/export procedures, provider acceptance, segregation-of-duties design, period-close/reopen procedures, and qualified legal/accounting review.

## Status vocabulary

Future reviews should classify individual requirements as:

- Not assessed
- Not applicable
- Planned
- Partially implemented
- Implemented
- Verified
- Risk accepted

## Governance

Before a production release, each Critical/High applicable area should have:

1. an owner,
2. applicability decision,
3. mapped implementation controls,
4. verification evidence,
5. known gaps/risks,
6. next review date.

For production Finance use, the governance package should additionally identify the legal entity, functional/reporting currency, chart/book, posting-profile owner, exchange-rate source, accounting calendar/period-close procedure, localization package, retention/export rules, and reconciliation responsibilities.

## Disclaimer

Regulatory applicability depends on how and where Depot is distributed, sold, operated, configured, and used. This matrix must be reviewed with qualified legal/compliance/accounting expertise before it is used as evidence of formal conformity or compliance.
