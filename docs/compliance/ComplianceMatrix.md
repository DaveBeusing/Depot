# Depot Compliance Matrix

Updated: 2026-09-08

## Purpose

This matrix tracks regulatory, standards and assurance areas potentially relevant to Depot. It is a planning/evidence tool, not a declaration of compliance, certification or legal opinion.

| Area | Type | Current technical direction | Priority |
| --- | --- | --- | --- |
| EU Cyber Resilience Act | Regulation | Secure lifecycle, SBOM, vulnerability/update process, release evidence | Critical |
| GDPR / DSGVO | Regulation | Privacy by design, minimization, RBAC, audit, retention/export procedures | Critical |
| GoBD | German administrative requirements | Immutable/correction-oriented records, traceable subledger/GL linkage, auditability, reproducible processing | High when Finance is used |
| EN 16931 / German e-invoicing | Standard/regulatory ecosystem | Sales XRechnung finalization/integrity with explicit scope boundaries | High when applicable |
| Accounting standards / statutory bookkeeping | Deployment/jurisdiction-specific | Jurisdiction-neutral Finance core with explicit localization and operator acceptance | High when Finance is used |
| Receivables / dunning / collections | Deployment/jurisdiction-specific | Controlled AR/dunning evidence; legal wording/fees/escalation remain external | High when AR is used |
| Payables / invoice approval / matching | Deployment/jurisdiction-specific | Controlled AP lifecycle, fail-closed matching, explicit exception approval, retained settlement/reversal evidence | High when AP is used |
| Database provider production acceptance | Engineering/runtime assurance | Real SQLite, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 migration/concurrency/recovery/business-flow acceptance | High |
| ISO/IEC 27001 / 27034 | Management/application security | Product/development controls and evidence mapping | High enterprise value |
| ISO/IEC 25010 | Software quality | Release/quality gates and real-provider regression evidence | High |
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
| AR/AP source/open-item/journal traceability |  | X | X | X | X |
| Payment/allocation/reversal evidence |  | X | X | X | X |
| Fail-closed PO/receipt/invoice matching |  |  | X | X | X |
| Explicit match-exception approval/reason |  | X | X | X | X |
| Segregated approval permissions |  |  | X | X | X |
| Real-provider migration/concurrency/retry acceptance | X |  | X | X | X |
| Provider-native backup/restore boundary tests | X | X | X | X | X |
| SBOM/vulnerability management | X |  |  | X |  |
| Release signing/evidence | X |  |  | X | X |
| E-invoice validation/integrity |  |  | X |  | X |
| Immutable report snapshots |  |  | X | X | X |
| Effective-dated localization evidence |  |  | X | X | X |

## Database provider evidence boundary

The production database-provider matrix is complete for the exact baselines documented in [Database Provider Production Support Matrix](../DatabaseProviderSupportMatrix.md): the Depot-bundled SQLite runtime, SQL Server 2022 / engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS.

The matrix validates provisioning/migration, provider SQL/types/constraints, rollback, concurrency/deadlock/write-conflict retry, representative Sales/Procurement/session/Finance flows, Banking/reconciliation, Financial Reporting/snapshots, server restart, native remote backup/restore and representative 100,000-row indexed access.

This evidence demonstrates the technical database/runtime boundary only. It does not convert a database provider, Finance module, localization pack, report, tax treatment or deployment into a legal/regulatory certification.

## Finance evidence boundary

The immutable General Ledger and subledger model preserves source, journal, settlement, matching and correction evidence. Supplier-document posting creates AP and GL evidence in controlled transactions. Matching uses explicit source facts and requires separately authorized exceptions where generic Finance cannot determine tolerance.

Inventory Accounting, Banking, Reporting and Localization add retained valuation, statement/reconciliation, report-snapshot and effective configuration evidence without replacing the General Ledger as accounting truth.

These controls strengthen technical evidence for traceability, authorization, reconciliation and correction history. They do **not** determine tax deductibility, VAT/GST treatment, statutory inbound e-invoice compliance, payment-law requirements, retention periods or conformity with HGB/IFRS/US-GAAP/GoBD.

## Governance

Before production release, each applicable Critical/High area should have an owner, applicability decision, mapped controls, verification evidence, known gaps/risks and review date. Finance governance should additionally identify Legal Entity, currency, chart/book/calendar/posting-profile owners, exchange-rate source, AR/AP configuration owners, reconciliation procedures, approvers, payment evidence and retention/localization responsibilities.

Remote database operators remain responsible for production backup scheduling, retention, off-host copies, encryption where required and restore drills even though the automated matrix proves the provider-native restore/re-entry boundary.

## Disclaimer

Regulatory applicability depends on how and where Depot is distributed, configured and used. This matrix must be reviewed with qualified legal/compliance/accounting expertise before use as evidence of formal conformity.
