# Finance Compliance and Control Boundaries

Updated: 2026-08-28

## Status and intent

This document describes technical controls implemented in Depot Finance. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with a jurisdiction-specific accounting framework.

Current Finance feature schema: **9**.

## Core principle

Finance core is jurisdiction-neutral. Country-, tax-, filing-, invoice-format-, accounting-standard- and organization-policy-specific behavior must be configured or supplied by localization/compliance extensions. Generic Finance does not hard-code Germany, EUR, VAT rates, SKR03/SKR04, HGB, IFRS, US-GAAP, statutory financial-statement layouts or tax-filing classifications into accounting behavior.

Localization makes this boundary operational by requiring explicit effective-dated assignment rather than inferring behavior from `LegalEntity.CountryCode`.

## Implemented technical controls

### Record integrity and correction

- General Ledger entries are immutable; corrections use linked reversals.
- Receivable/Payable subledger records retain source/journal identity and settlement evidence.
- Inventory valuation layers/consumptions, purchase variances, landed-cost operations and reconciliation runs retain historical evidence.
- Bank statements are immutable imports; reconciliations preserve original evidence and explicit reversal.
- Report snapshots are immutable `AuditEvidence` containing report parameters, canonical CSV and SHA-256 parameter/content hashes.
- Localization assignments and registry entries are retained `AuditEvidence`; built-in reference definitions are immutable.

These controls support evidentiary integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival procedures, access controls and export procedures remain required.

### Double-entry and posting controls

`FinanceGeneralLedgerService` is the authoritative posting boundary. Subledgers and Inventory Accounting use this boundary rather than maintaining parallel ledger truth. Financial Reporting is read/reporting apart from explicit snapshot persistence. Localization never posts accounting entries.

The General Ledger validates balanced debit/credit totals, transaction/reporting currency, period/date/legal-entity/account/dimension requirements, number sequences, idempotency and configured posting profiles.

### Audit, idempotency and retry safety

Finance mutations persist Audit evidence where required. Retry-sensitive operations use operation IDs, immutable source identities, request/content hashes or uniqueness constraints. Reusing an operation ID with incompatible content is rejected. Localization configuration writes use optimistic concurrency; active assignments cannot overlap for one Legal Entity; built-in pack/registry rows reject mutation.

### Subledgers, inventory, banking and reporting

Accounts Receivable provides customer open items, allocations, payments, write-offs, aging/statements and dunning. Accounts Payable provides supplier documents/open items, payments/allocations/reversal, aging/statements and fail-closed PO/goods-receipt/invoice matching with separately authorized exceptions.

Inventory Accounting implements configured FIFO only, prevents negative valued issues, retains exact consumption/reversal evidence and supports adjustments, purchase-price variance, landed cost and historical Inventory ↔ GL reconciliation. Generic Finance does not determine whether FIFO or a particular landed-cost component is permitted/capitalizable for a deployment.

Banking provides configured bank accounts, immutable statement imports, deterministic normalization, payment proposals, creator/approver separation, execution through Accounts Payable, AR/AP/GL reconciliation, explicit reconciliation reversal and cash-position comparison. It does not claim direct bank/payment-services certification.

Financial Reporting provides Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS. GL-derived reports use persisted reporting-currency evidence; accounting meaning is driven by explicit mappings rather than names/numbers. Report snapshots bind parameters/content with SHA-256.

### Localization Framework

Localization requires an explicit effective-dated root-pack assignment. Country packs are validated against Legal Entity country; active assignment ranges cannot overlap; parent/child layer rules and cycle/depth controls protect pack composition. Built-in `GENERIC → EU → DE` references are immutable. Custom packs can extend the hierarchy without another schema change when metadata/configuration is sufficient.

Registry support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. They are **not** compliance status flags and do not mean compliant, certified, approved or legally sufficient.

The framework does not invent VAT/GST/sales-tax rates, reverse-charge/place-of-supply decisions, withholding/customs rules, statutory charts of accounts, HGB/IFRS/US-GAAP policy choices, tax-return box mappings, filing eligibility, legal-retention periods, audit/signature requirements or organization-specific control ownership.

## Approval and segregation of duties

UI visibility is not an authorization boundary. Finance operations are enforced at service level. Deployments may require stronger custom-role segregation for localization/configuration, posting, reconciliation, report preparation and review. Supplier-document/match-exception approval and payment-proposal approval separation remain independent controls.

## Provider and operational acceptance

Finance schema 9 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Production support requires live acceptance for fresh install/upgrades, transaction/locking/deadlock/retry behavior, backup/restore/recovery, date/decimal semantics, representative statement/reconciliation/reporting volumes, localization concurrency, rollback behavior and evidence-retention procedures.

Depending on deployment, these controls may contribute evidence toward ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

## Current gaps and extensions

Remaining work is production/provider/legal/organizational acceptance and demand-driven jurisdiction extensions. Other potential extensions include costing methods beyond FIFO, impairment/NRV, manufacturing/WIP costing, direct bank connectivity and jurisdiction-specific statutory filing implementations.

No repository feature or localization pack should be described externally as certified or legally compliant solely because these controls exist.
