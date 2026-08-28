# Finance Compliance and Control Boundaries

Updated: 2026-08-28

## Status and intent

This document describes technical controls implemented in Depot Finance **F0-F6**. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with a jurisdiction-specific accounting framework.

Current Finance feature schema: **8**.

## Core principle

Finance core is jurisdiction-neutral. Country-, tax-, filing-, invoice-format-, accounting-standard- and organization-policy-specific behavior must be configured or supplied by localization/compliance extensions. F0-F6 do not hard-code Germany, EUR, VAT rates, SKR03/SKR04, HGB, IFRS, US-GAAP, statutory financial-statement layouts or tax-filing classifications into generic accounting behavior.

## Implemented technical controls

### Record integrity and correction

- F1 General Ledger entries are immutable; corrections use linked reversals.
- F2/F3 subledger records retain source/journal identity and settlement evidence.
- F4 valuation layers/consumptions, purchase variances, landed-cost operations and reconciliation runs retain historical evidence.
- F5 bank statements are immutable imports; reconciliations preserve original evidence and explicit reversal.
- F6 report snapshots are immutable AuditEvidence containing report parameters, canonical CSV and SHA-256 parameter/content hashes.

These controls support evidentiary integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival procedures, access controls and export procedures remain required.

### Double-entry and posting controls

`FinanceGeneralLedgerService` remains the authoritative posting boundary. F2-F5 use this boundary instead of maintaining parallel ledger truth. F6 is read/reporting only apart from explicit snapshot persistence.

F1 validates balanced debit/credit totals, transaction/reporting currency, period/date/legal-entity/account/dimension requirements, number sequences, idempotency and configured posting profiles.

### Audit, idempotency and retry safety

Finance mutations persist Audit evidence where required. Retry-sensitive operations use operation IDs, immutable source identities, request/content hashes or uniqueness constraints. Reusing an operation ID with incompatible content is rejected.

F6 snapshot creation is idempotent only for the same parameters/content. It fails closed when an operation ID is reused for different report content.

### Accounts Receivable / Payable

F2 provides customer open items, allocations, payments, write-offs, aging/statements and dunning. F3 provides supplier documents/open items, payments/allocations/reversal, aging/statements and fail-closed PO/goods-receipt/invoice matching with separately authorized exceptions.

### Inventory Accounting

F4 implements configured FIFO only, prevents negative valued issues, retains exact consumption/reversal evidence, supports inventory adjustments, PPV, landed cost and true historical as-of Inventory ↔ GL reconciliation. Generic Finance does not determine whether FIFO or a particular landed-cost component is permitted/capitalizable for a deployment.

### Banking and payments

F5 provides configured bank accounts, immutable statement imports, deterministic normalization, payment proposals, creator/approver separation, execution through F3 AP, AR/AP/GL reconciliation, explicit reconciliation reversal and cash-position comparison. It does not claim EBICS, PSD2/open-banking certification, payment initiation certification, sanctions/AML/KYC decisioning or bank-specific ISO 20022 profile certification.

### Financial Reporting

F6 provides Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS.

Controls include:

- GL-derived reports use the persisted F1 reporting-currency values rather than recalculating historical FX from current rates;
- AR/AP aging remains in each open item's transaction currency and does not invent missing historical FX;
- Cash Flow, tax and COGS meaning is driven by explicit account mappings rather than account-name/number heuristics;
- financial-statement mapping is validated against account type;
- optional GL report dimension filters use persisted journal-line dimensions;
- deterministic culture-invariant CSV export is separately authorized;
- retained report snapshots bind parameters and content with SHA-256 and preserve immutable user/time evidence.

A balanced/reconciled report or retained snapshot is technical accounting evidence. It is not proof that a financial statement complies with HGB, IFRS, US-GAAP, tax law, GoBD or any statutory filing specification.

## Approval and segregation of duties

UI visibility is not an authorization boundary. Finance operations are enforced at service level. The default Finance role receives operational F6 view/manage/export/snapshot rights. Deployments may require stronger custom-role segregation for mapping maintenance, report preparation, period close and review/approval.

AP document/match-exception approval and F5 payment-proposal approval separation remain independent controls.

## Tax/localization boundary

F6 tax summary is a configurable technical report over explicitly mapped GL accounts. It does not determine tax treatment, tax return boxes, VAT/GST/sales-tax rules, reverse charge, withholding, place of supply, customs treatment or filing correctness.

Jurisdiction-specific statutory presentation/filing belongs to F7 localization/compliance packs or separately scoped modules.

## Provider and operational acceptance

Finance schema 8 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Before production support/certification claims, each deployment matrix still requires live acceptance for:

- fresh install and upgrades through Finance schemas 1→8;
- transaction/locking/deadlock/retry behavior for AR/AP/FIFO/Banking configuration mutations;
- backup/restore/recovery and identity/sequence behavior;
- date/decimal semantics;
- representative statement, reconciliation and financial-reporting volumes;
- rollback under Audit/GL/valuation failure;
- report performance and snapshot/export retention procedures.

## Standards and regulatory relevance

Depending on deployment, these controls may contribute evidence toward ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

No repository feature should be described externally as certified or legally compliant solely because these controls exist.

## Current gaps / future package

F0-F6 are implemented. Remaining Finance package **F7** covers localization/statutory extension infrastructure. Other gaps include costing methods beyond FIFO, impairment/NRV, manufacturing/WIP costing, direct bank connectivity, jurisdiction-specific filing packs, live-provider certification, organization-specific accounting procedures and production signing/deployment acceptance.

## Required F6 release evidence

Retain at minimum Release build/publish results, F6 regression evidence, Finance schema 8 migration evidence, F6 RBAC evidence, representative Trial Balance/GL/Cash Flow mapping evidence, snapshot idempotency/hash evidence, Help/documentation synchronization and provider-matrix acceptance results when production providers are certified.
