# Finance Compliance and Control Boundaries

Updated: 2026-08-28

## Status and intent

This document describes technical controls implemented in Depot Finance **F0-F7**. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with a jurisdiction-specific accounting framework.

Current Finance feature schema: **9**.

## Core principle

Finance core is jurisdiction-neutral. Country-, tax-, filing-, invoice-format-, accounting-standard- and organization-policy-specific behavior must be configured or supplied by localization/compliance extensions. Generic Finance does not hard-code Germany, EUR, VAT rates, SKR03/SKR04, HGB, IFRS, US-GAAP, statutory financial-statement layouts or tax-filing classifications into accounting behavior.

F7 makes this boundary operational by requiring explicit effective-dated localization assignment rather than inferring behavior from `LegalEntity.CountryCode`.

## Implemented technical controls

### Record integrity and correction

- F1 General Ledger entries are immutable; corrections use linked reversals.
- F2/F3 subledger records retain source/journal identity and settlement evidence.
- F4 valuation layers/consumptions, purchase variances, landed-cost operations and reconciliation runs retain historical evidence.
- F5 bank statements are immutable imports; reconciliations preserve original evidence and explicit reversal.
- F6 report snapshots are immutable AuditEvidence containing report parameters, canonical CSV and SHA-256 parameter/content hashes.
- F7 localization assignments and registry entries are retained AuditEvidence; built-in reference definitions are immutable.

These controls support evidentiary integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival procedures, access controls and export procedures remain required.

### Double-entry and posting controls

`FinanceGeneralLedgerService` remains the authoritative posting boundary. F2-F5 use this boundary instead of maintaining parallel ledger truth. F6 is read/reporting only apart from explicit snapshot persistence. F7 does not post accounting entries.

F1 validates balanced debit/credit totals, transaction/reporting currency, period/date/legal-entity/account/dimension requirements, number sequences, idempotency and configured posting profiles.

### Audit, idempotency and retry safety

Finance mutations persist Audit evidence where required. Retry-sensitive operations use operation IDs, immutable source identities, request/content hashes or uniqueness constraints. Reusing an operation ID with incompatible content is rejected.

F7 configuration writes use optimistic concurrency. Assignments prevent overlapping active effective ranges for the same legal entity. Built-in pack/registry rows reject mutation.

### Accounts Receivable / Payable

F2 provides customer open items, allocations, payments, write-offs, aging/statements and dunning. F3 provides supplier documents/open items, payments/allocations/reversal, aging/statements and fail-closed PO/goods-receipt/invoice matching with separately authorized exceptions.

### Inventory Accounting

F4 implements configured FIFO only, prevents negative valued issues, retains exact consumption/reversal evidence, supports inventory adjustments, PPV, landed cost and true historical as-of Inventory ↔ GL reconciliation. Generic Finance does not determine whether FIFO or a particular landed-cost component is permitted/capitalizable for a deployment.

### Banking and payments

F5 provides configured bank accounts, immutable statement imports, deterministic normalization, payment proposals, creator/approver separation, execution through F3 AP, AR/AP/GL reconciliation, explicit reconciliation reversal and cash-position comparison. It does not claim EBICS, PSD2/open-banking certification, payment initiation certification, sanctions/AML/KYC decisioning or bank-specific ISO 20022 profile certification.

### Financial Reporting

F6 provides Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS.

GL-derived reports use persisted F1 reporting-currency values rather than current FX. Cash Flow, tax and COGS meaning is driven by explicit mappings rather than account-name/number heuristics. Report snapshots bind parameters/content with SHA-256.

A balanced/reconciled report or retained snapshot is technical accounting evidence. It is not proof that a financial statement complies with HGB, IFRS, US-GAAP, tax law, GoBD or any statutory filing specification.

### Localization Framework

F7 introduces a controlled localization boundary:

- a Legal Entity country code does not activate any localization behavior automatically;
- localization requires an explicit effective-dated root-pack assignment;
- the built-in reference hierarchy is `GENERIC → EU → DE`;
- country packs are validated against the Legal Entity country;
- overlapping active assignments fail closed;
- parent/child layer rules, cycle detection and hierarchy depth limits protect pack composition;
- built-in pack and registry definitions are immutable;
- custom regional/country packs can be added without another schema change;
- effective registry entries separate `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`.

These support levels are **not** compliance status flags. They do not mean “compliant”, “certified”, “approved”, or “legally sufficient”. The framework intentionally has no automatic compliant/non-compliant outcome.

The German reference pack may identify that Depot contains a technical capability relevant to XRechnung and may identify GoBD-related organizational/procedural boundaries. It does not claim that assigning `DE` makes a deployment XRechnung/GoBD/HGB/tax compliant. Qualified review of configuration, process, retention, signing, filing and operating procedures remains required.

F7 does not invent or prescribe:

- VAT/GST/sales-tax rates;
- reverse-charge/place-of-supply decisions;
- withholding/customs rules;
- SKR03/SKR04 or another statutory chart of accounts;
- HGB/IFRS/US-GAAP accounting-policy choices;
- tax-return box mappings;
- statutory filing eligibility;
- legal-retention periods;
- audit/signature requirements;
- organization-specific control ownership.

## Approval and segregation of duties

UI visibility is not an authorization boundary. Finance operations are enforced at service level. The default Finance role receives F7 `FinanceLocalization.View` and `FinanceLocalization.Manage` rights. Deployments may require stronger custom-role segregation so one role maintains packs/assignments while another qualified owner approves the deployment configuration.

AP document/match-exception approval and F5 payment-proposal approval separation remain independent controls.

## Tax/localization boundary

F6 Tax Summary is a configurable technical report over explicitly mapped GL accounts. F7 can describe jurisdiction reference requirements and configuration/procedure ownership, but it does not determine tax treatment, tax return boxes, VAT/GST/sales-tax rules, reverse charge, withholding, place of supply, customs treatment or filing correctness.

Jurisdiction-specific executable statutory behavior must be separately implemented when metadata/configuration alone is insufficient.

## Provider and operational acceptance

Finance schema 9 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Before production support/certification claims, each deployment matrix still requires live acceptance for:

- fresh install and upgrades through Finance schemas 1→9;
- transaction/locking/deadlock/retry behavior for AR/AP/FIFO/Banking/reporting/localization configuration mutations;
- backup/restore/recovery and identity/sequence behavior;
- date/decimal semantics;
- representative statement, reconciliation and financial-reporting volumes;
- localization assignment/registry concurrency and migration behavior;
- qualified review of every enabled country pack and its external/configuration requirements;
- rollback under Audit/GL/valuation failure;
- report/export/retention procedures.

## Standards and regulatory relevance

Depending on deployment, these controls may contribute evidence toward ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

No repository feature or localization pack should be described externally as certified or legally compliant solely because these controls exist.

## Current gaps / future extensions

F0-F7 are implemented. Remaining Finance work is production/provider/legal/organizational acceptance and demand-driven jurisdiction extensions. Other gaps include costing methods beyond FIFO, impairment/NRV, manufacturing/WIP costing, direct bank connectivity, jurisdiction-specific filing implementations, live-provider certification, organization-specific accounting procedures and production signing/deployment acceptance.

## Required F7 release evidence

Retain at minimum:

- Release build/publish results;
- F7 regression evidence;
- Finance schema 9 migration evidence;
- F7 RBAC evidence;
- explicit-country-non-activation evidence;
- Germany hierarchy/country-guard/overlap evidence;
- custom-country-pack extensibility evidence;
- built-in immutability evidence;
- Help/documentation synchronization;
- qualified deployment localization review and live provider-matrix acceptance results when production providers are certified.
