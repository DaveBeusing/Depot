# Finance Compliance and Control Boundaries

Updated: 2026-09-08

## Status and intent

This document describes technical controls implemented in Depot Finance. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with a jurisdiction-specific accounting framework.

Current Finance feature schema: **10**.

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
- Fixed-asset masters retain stable identity while capitalization, depreciation, impairment, transfer and disposal evidence is retained separately. Accounting mutations reference immutable General Ledger journal entries and operation IDs; historical posted evidence is not rewritten.

These controls support evidentiary integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival procedures, access controls and export procedures remain required.

### Double-entry and posting controls

`FinanceGeneralLedgerService` is the authoritative posting boundary. Subledgers, Inventory Accounting and Fixed Assets use this boundary rather than maintaining parallel ledger truth. Financial Reporting is read/reporting apart from explicit snapshot persistence. Localization never posts accounting entries.

The General Ledger validates balanced debit/credit totals, transaction/reporting currency, period/date/legal-entity/account/dimension requirements, number sequences, idempotency and configured posting profiles.

### Accounting-period control

The Finance Period Control workspace reads and changes accounting-period availability through `FinanceGeneralLedgerService`. Viewing requires `FinancePeriods.View`; close/reopen transitions require `FinancePeriods.Manage`. Closed periods remain fail-closed for new postings.

This is an operational control, not an accounting-policy decision. Depot does not decide whether reconciliation, accruals, reporting review, tax procedures or other deployment-specific period-end activities are complete, and it does not authorize reopening outside the organization's approved procedure.

### Audit, idempotency and retry safety

Finance mutations persist Audit evidence where required. Retry-sensitive operations use operation IDs, immutable source identities, request/content hashes or uniqueness constraints. Reusing an operation ID with incompatible content is rejected. Localization configuration writes use optimistic concurrency; active assignments cannot overlap for one Legal Entity; built-in pack/registry rows reject mutation.

Provider write retries are restricted to known transient lock/deadlock/write-conflict failures. Retried operations recreate the connection transaction and use bounded exponential backoff with jitter; non-transient constraint/business failures are not retried.

### Subledgers, inventory, banking and reporting

Accounts Receivable provides customer open items, allocations, payments, write-offs, aging/statements and dunning. Accounts Payable provides supplier documents/open items, payments/allocations/reversal, aging/statements and fail-closed PO/goods-receipt/invoice matching with separately authorized exceptions.

Inventory Accounting implements configured FIFO only, prevents negative valued issues, retains exact consumption/reversal evidence and supports adjustments, purchase-price variance, landed cost and historical Inventory ↔ GL reconciliation. Generic Finance does not determine whether FIFO or a particular landed-cost component is permitted/capitalizable for a deployment.

Banking provides configured bank accounts, immutable statement imports, deterministic normalization, payment proposals, creator/approver separation, execution through Accounts Payable, AR/AP/GL reconciliation, explicit reconciliation reversal and cash-position comparison. It does not claim direct bank/payment-services certification.

Financial Reporting provides Trial Balance, GL detail, Balance Sheet, P&L, Cash Flow, AR/AP Aging, Tax Summary, historical Inventory Valuation and COGS. GL-derived reports use persisted reporting-currency evidence; accounting meaning is driven by explicit mappings rather than names/numbers. Report snapshots bind parameters/content with SHA-256.

### Fixed Assets

Fixed Assets is a jurisdiction-neutral subledger. Asset classes configure fiscal calendars plus explicit posting profiles for capitalization, depreciation, impairment and disposal. The initial automated depreciation methods are **Straight Line** and **No Depreciation** only.

Closed-period depreciation fails closed unless the caller explicitly selects the documented next-open-period policy. Period depreciation runs use deterministic operation identities so retries cannot silently double-post completed schedule rows. Transfers update current location/custodian information without rewriting retained transactions, and disposal preserves historical cost/depreciation evidence.

The implementation does not decide statutory useful lives, tax depreciation, HGB/IFRS/US-GAAP treatment, component accounting, revaluation, pooled-asset treatment, IFRS 16 lease accounting or tax-book policy. Those remain deployment-specific accounting/tax decisions.

### Localization Framework

Localization requires an explicit effective-dated root-pack assignment. Country packs are validated against Legal Entity country; active assignment ranges cannot overlap; parent/child layer rules and cycle/depth controls protect pack composition. Built-in `GENERIC → EU → DE` references are immutable. Custom packs can extend the hierarchy without another schema change when metadata/configuration is sufficient.

Registry support levels are `SoftwareCapability`, `ConfigurationRequired`, `ExternalProcedureRequired` and `ReferenceOnly`. They are **not** compliance status flags and do not mean compliant, certified, approved or legally sufficient.

The framework does not invent VAT/GST/sales-tax rates, reverse-charge/place-of-supply decisions, withholding/customs rules, statutory charts of accounts, HGB/IFRS/US-GAAP policy choices, tax-return box mappings, filing eligibility, legal-retention periods, audit/signature requirements or organization-specific control ownership.

## Approval and segregation of duties

UI visibility is not an authorization boundary. Finance operations are enforced at service level. Deployments may require stronger custom-role segregation for localization/configuration, posting, reconciliation, report preparation and review. Supplier-document/match-exception approval and payment-proposal approval separation remain independent controls.

## Provider and operational acceptance

Finance schema 10 follows the same provider-neutral migration and acceptance path on the database baselines listed in [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md): Depot's bundled SQLite runtime, SQL Server 2022 engine 16.x, MariaDB 11.8.9 LTS and MySQL 8.4.11 LTS.

The real-provider suite covers provisioning/migration, decimal/date/timestamp behavior, rollback, constraints, concurrency/deadlock/retry, GL/AR/AP/FIFO flows, Banking/reconciliation, Financial Reporting/snapshots, restart/re-entry, provider-native remote backup/restore and representative 100k indexed lookup performance. This closes the database-provider technical acceptance gate for those exact baselines.

It does **not** close deployment-specific accounting/reporting policy approval, period-end procedures, reconciliation ownership, segregation-of-duties review, retention/export/restore procedures, direct-bank integration, statutory filing, tax, localization or legal acceptance.

Depending on deployment, these controls may contribute evidence toward ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

## Current gaps and extensions

Remaining work is legal/organizational/deployment acceptance and demand-driven jurisdiction extensions, not generic provider compatibility for the certified database baselines. Other potential extensions include costing methods beyond FIFO, impairment/NRV, manufacturing/WIP costing, direct bank connectivity and jurisdiction-specific statutory filing implementations.

No repository feature, provider baseline or localization pack should be described externally as legally certified or jurisdiction-compliant solely because these engineering controls and provider tests pass.

## Budget planning boundary

Finance schema **11** adds controlled budgeting without changing the accounting system of record. Budgets are planning evidence only: they do not post journals, alter posted history or replace General Ledger/Financial Reporting authority.

Approval policies retain immutable submitted-stage evidence. Approved and Locked budget versions cannot be silently edited; changes require a new version/amendment. The real-provider acceptance path includes budget persistence and nine-decimal aggregate behavior.


## SEPA SCT export boundary

Depot implements a bounded SEPA SCT customer-to-PSP export profile using `pain.001.001.09`, pinned to the EPC SCT 2025 rulebook v1.1 / Customer-to-PSP implementation guidelines 2025 v1.0. The implementation requires explicit structured payment addresses and does not emit unstructured-only address lines.

The EPC announced on 9 September 2026 that the previously planned 15 November 2026 end-date for unstructured addresses would be postponed. Depot's structured-address-only rule is therefore a stricter supported-product profile, not a claim that the postponed date remains binding.

Schema/profile validation, deterministic artifact tests and live database-provider persistence evidence do not constitute EPC or bank certification. EBICS, PSD2/Open Banking submission, bank-specific host-to-host connectivity, automatic status polling and sanctions/AML/KYC decisioning remain outside the implemented boundary. External submission and acceptance/rejection are retained as manual evidence only.
