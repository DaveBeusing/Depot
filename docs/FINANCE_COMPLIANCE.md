# Finance Compliance and Control Boundaries

Updated: 2026-08-28

## Status and intent

This document describes technical controls implemented in Depot Finance **F0-F4**. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with a jurisdiction-specific accounting framework.

Current Finance feature schema: **6**.

## Core principle

Finance core is jurisdiction-neutral. Country-, tax-, filing-, invoice-format-, accounting-standard- and organization-policy-specific behavior must be configured or supplied by localization/compliance extensions. F0-F4 do not hard-code Germany, EUR, VAT rates, SKR03/SKR04, HGB, IFRS, US-GAAP, XRechnung, inventory-account numbering or statutory valuation choices into generic accounting behavior.

## Implemented technical controls

### Record integrity

- Posted General Ledger entries are immutable through Finance workflows.
- Corrections use linked reversal entries.
- Customer/supplier subledger records preserve source and journal linkage.
- Inventory valuation effects preserve immutable movement/source identities.
- FIFO consumptions are recorded separately from layers rather than rewriting historical source evidence.
- Purchase variances and landed-cost operations retain their original records after reversal.
- Period-end inventory reconciliations are immutable snapshots with retained per-item lines.

These controls support evidentiary integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival procedures and export controls remain required.

### Double-entry and posting controls

F1 validates balanced debit/credit totals in transaction and reporting currency and uses configured posting profiles. F2, F3 and F4 invoke this boundary rather than creating parallel ledger logic.

Posting rejects invalid/inactive configuration, closed/wrong periods, invalid accounts/dimensions, missing FX snapshots and incompatible source/profile configuration.

### Period and number controls

Finance uses explicit fiscal calendars/accounting periods and Finance number sequences. Number allocation occurs inside the accounting transaction. A failed posting must not leave a partially committed journal/subledger/valuation result.

Whether an organization requires gapless, chronological or legally prescribed numbering must be assessed in its jurisdiction and operating procedures.

### Audit trail

Finance mutations persist Audit Log evidence in the same transactional unit where required. Audit evidence includes user/action/time and before/after or action snapshots according to the shared audit framework.

Production audit retention, access review, monitoring, export and evidentiary procedures remain organizational acceptance items.

### Idempotency and retry safety

Retry-sensitive GL, AR, AP and Inventory Accounting operations use operation IDs, immutable source identities and request hashes where appropriate. Reusing an operation ID with incompatible content is rejected.

### Accounts Receivable controls

F2 includes customer open items, allocations, overpayments, payment reversals, write-offs, aging, statements and dunning. Sensitive write-off rights are not granted to the default Finance role.

### Accounts Payable controls

F3 includes supplier invoices/credit notes, open items, supplier payments, allocations, reversal, aging/statements and PO/goods-receipt/invoice matching. Generic three-way matching remains fail-closed with no implicit tolerance. Explicit match exceptions require separate authorization and retained reason.

### Inventory Accounting controls

F4 implements the following technical controls:

- only explicitly configured Inventory Accounting is activated; no accounting defaults are invented;
- FIFO is the only implemented valuation method and unsupported costing methods are rejected;
- valued stock issues cannot drive the valuation layer balance negative;
- receipt reversal is blocked after downstream valuation consumption until dependent issues are reversed;
- shipment reversal restores the exact recorded FIFO consumptions;
- inventory-count adjustment valuation fails closed if a positive correction has no defensible valued basis;
- purchase-price variance uses explicit posted supplier-document values and referenced PO values, not an implicit tolerance;
- landed cost can only be allocated/reversed while selected layers satisfy the required unconsumed state;
- cross-currency landed-cost allocation is rejected rather than silently converted;
- reconciliation reconstructs valuation at the requested historical cutoff instead of comparing a historical GL balance to today’s inventory state;
- reconciliation runs and item lines are retained as AuditEvidence.

### GRNI / COGS boundary

Goods receipts and sales shipments post through configured F1 profiles. The posting profile determines the actual debit/credit accounts. F4 supplies valuation amount keys and does not assume a chart of accounts or statutory account mapping.

The generic core therefore supports the technical mechanics commonly used for inventory/GRNI and inventory/COGS posting while leaving chart design and accounting-policy approval to the deployment.

### Valuation-policy boundary

The availability of FIFO does not mean FIFO is appropriate or permitted for every entity, inventory category, reporting standard or tax regime. Deployment accounting stakeholders must approve the chosen policy and consistency of application. LIFO, weighted average, standard cost, lower-of-cost/NRV impairment and jurisdiction-specific revaluation are not claimed by F4.

### Purchase variance boundary

F4’s purchase-price variance represents the difference between expected PO net value for referenced invoiced quantities and the posted supplier-document net value. It is a technical accounting consequence after F3 matching/approval. It is not a substitute for procurement tolerance policy, tax validation, fraud review or statutory invoice verification.

### Landed-cost boundary

F4 allocates an explicit landed-cost amount across selected valuation layers by quantity or current layer value. The user/deployment remains responsible for deciding which freight, duty, insurance, handling or other components are capitalizable under the applicable accounting policy. Depot does not infer capitalization eligibility.

### Reconciliation boundary

F4 compares the reconstructed inventory valuation with one configured inventory-control account in the same accounting book/reporting currency. A zero difference is technical reconciliation evidence, not proof that inventory valuation or financial statements comply with a particular accounting standard.

## Approval and segregation of duties

F4 adds `FinanceInventoryAccounting.View` and `FinanceInventoryAccounting.Manage`. The default Finance role receives these operational rights. Deployments requiring stronger segregation should separate configuration/policy maintenance, landed-cost/variance operations and period-end review using custom roles and procedures.

Existing AP document/match-exception approval separation remains unchanged.

## Tax and electronic-invoice boundary

F4 does not determine VAT/GST/sales-tax, customs-duty deductibility, import VAT, reverse charge, withholding tax, place of supply or tax capitalization. Existing Sales XRechnung/EN 16931 functionality remains separate from generic Finance.

## Provider and operational acceptance

Finance schema 6 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Before production claims, each supported deployment matrix still requires live acceptance for:

- fresh install and upgrades through Finance schemas 1→6;
- concurrent FIFO consumption and adjustment behavior;
- locking/deadlock/transient retry behavior;
- landed-cost allocation/reversal concurrency;
- period-end reconciliation at representative data volumes;
- backup, restore and recovery;
- identity/sequence behavior;
- date/decimal semantics;
- rollback under audit/GL/valuation failure.

## Standards and regulatory relevance

Depending on deployment, these controls may contribute evidence toward ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

No repository feature should be described externally as certified or legally compliant solely because these controls exist.

## Current gaps / future packages

F4 does not close:

- bank account/statement/payment-file workflows — F5;
- statutory financial statements and management reporting — F6;
- country-specific tax/localization packs — F7;
- costing methods other than FIFO;
- impairment/lower-of-cost-and-NRV policy;
- manufacturing/WIP/production costing;
- production provider certification;
- organization-specific retention and accounting-procedure documentation;
- production signing/deployment acceptance.

## Required evidence for F4 release acceptance

At minimum retain:

- Release build/publish result;
- F4 regression-test results;
- Finance feature schema 6 migration evidence;
- RBAC evidence for Inventory Accounting permissions;
- representative receipt/shipment valuation and reversal evidence;
- historical as-of valuation test evidence;
- landed-cost and variance operation/reversal evidence where used;
- inventory↔GL reconciliation snapshot evidence;
- provider-matrix acceptance results when production providers are certified;
- updated Help/documentation identifying generic/accounting-policy/localization boundaries.
