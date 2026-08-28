# Finance Compliance and Control Boundaries

Updated: 2026-08-28

## Status and intent

This document describes technical controls implemented in Depot Finance F0-F3. It is not a legal opinion, accounting-policy determination, tax determination, certification, audit opinion, or claim of compliance with any jurisdiction-specific standard.

Current Finance feature schema: **4**.

## Core principle

Finance core is jurisdiction-neutral. Country-, tax-, filing-, invoice-format- and accounting-standard-specific behavior must be configured or supplied by localization/compliance extensions. F0-F3 do not hard-code Germany, EUR, a VAT rate, SKR03/SKR04, HGB, IFRS, US-GAAP or XRechnung into generic accounting behavior.

## Implemented technical controls

### Record integrity

- Posted General Ledger entries are immutable through Finance workflows.
- Corrections use linked reversal entries.
- Supplier/customer subledger records preserve their source identity and journal linkage.
- Posted supplier documents and supplier payments are retained accounting-relevant records.
- Payment reversals preserve the original payment and allocation history.
- Supplier-document reversal preserves the original supplier document and journal.

These controls support evidence integrity but do not independently establish statutory retention compliance. Organization-specific retention periods, archival controls, export procedures and operating instructions remain required.

### Double-entry and posting controls

F1 validates balanced debit/credit totals in transaction and reporting currency and uses configured posting profiles. F2 and F3 invoke this boundary rather than creating parallel ledger logic.

Posting rejects invalid or inactive accounting configuration, closed/wrong periods, invalid accounts/dimensions, missing required FX snapshots and incompatible source/profile configuration.

### Period and number controls

Finance uses explicit fiscal calendars/accounting periods and Finance number sequences. Number allocation occurs inside the accounting transaction. A failed posting must not leave a partially committed journal/subledger result.

Whether a specific organization requires gapless, chronological or legally prescribed numbering must be assessed in its applicable jurisdiction and procedure documentation.

### Audit trail

Finance mutations persist Audit Log evidence in the same transactional unit where required. Audit evidence includes user/action/time and before/after or action snapshots according to the existing audit framework.

Audit logging is a technical control. Production audit retention, access review, monitoring, export and evidentiary procedures remain organizational acceptance items.

### Idempotency and retry safety

Retry-sensitive GL, AR and AP operations use operation IDs and, where appropriate, request hashes/source identity. Reusing an operation ID with different content is rejected. This reduces duplicate financial postings under retry/concurrency conditions.

### Accounts Receivable controls

F2 includes customer open items, allocations, overpayments, payment reversals, write-offs, aging, statements and dunning. Sensitive write-off rights are withheld from the default Finance role.

### Accounts Payable controls

F3 includes supplier invoices/credit notes, open items, supplier payments, allocations, reversal, aging/statements and PO/goods-receipt/invoice matching.

Three-way matching is fail-closed in generic core:

- supplier must match the referenced purchase order;
- invoiced quantity cannot exceed currently received and not-yet-invoiced quantity;
- invoiced unit price must equal purchase-order unit price;
- reversed goods receipts do not provide matching quantity;
- no implicit percentage/quantity/price tolerance exists.

A mismatch becomes an explicit match exception. Approval of that exception requires `FinanceSupplierMatchExceptions.Approve` and a retained reason. This avoids silently converting a mismatch into an accepted posting.

Non-PO invoices are supported; they do not receive invented purchase-order evidence.

### Approval and segregation of duties

F3 separates permissions for:

- supplier-document creation;
- submission;
- normal approval;
- match-exception approval;
- posting;
- reversal;
- supplier-payment posting/reversal.

The default Finance role receives operational AP rights but not supplier-document approval or match-exception approval. Deployment role design must ensure that incompatible permissions are not assigned to the same person where the organization's control framework requires four-eyes separation. Role configuration and periodic access review remain organizational controls.

## Tax boundary

Supplier-document tax amounts in F3 are explicit document inputs consumed by configured posting profiles. F3 does not infer VAT/GST/sales-tax rates, deductibility, reverse charge, exemptions, place of supply, withholding tax or statutory tax-code treatment.

A jurisdiction/localization package may later determine and validate such semantics, but it must not be smuggled into generic AP behavior as an implicit default.

## Electronic invoicing boundary

The existing Sales XRechnung/EN 16931 functionality is separate from generic Finance. F3 does not claim inbound e-invoice parsing, validation, routing or statutory supplier-invoice compliance. Those require separate implementation and acceptance.

## Provider and operational acceptance

Finance schema 4 has provider-specific DDL for SQLite, SQL Server and MySQL/MariaDB. Before production claims, each supported deployment matrix still requires live acceptance for:

- fresh install and upgrade from Finance schemas 1/2/3;
- locking and concurrent posting/allocation behavior;
- deadlock/transient-retry behavior;
- backup, restore and recovery;
- representative data volumes/performance;
- identity/sequence behavior;
- date/decimal semantics;
- failure rollback under audit/GL/subledger errors.

## Standards and regulatory relevance

Depending on deployment and customer context, technical controls may contribute evidence toward frameworks such as ISO 27001, SOC-style controls, OWASP ASVS, EU CRA security obligations, GDPR accountability and accounting-control expectations. Applicability and conformity must be assessed separately by qualified organizational/legal/accounting stakeholders.

No repository feature should be described externally as certified or legally compliant solely because these controls exist.

## Current gaps / future packages

F3 does not close:

- inventory accounting/valuation/COGS — planned F4;
- banking/payment-file workflows — later package;
- statutory financial statements and consolidation;
- country-specific tax determination/reporting;
- localization packs;
- production provider certification;
- organization-specific retention and procedure documentation;
- production signing/deployment acceptance.

## Required evidence for F3 release acceptance

At minimum retain:

- Release build/publish result;
- F3 regression-test result;
- Finance feature schema 4 migration evidence;
- RBAC test showing approval/match-exception permissions remain separate;
- representative AP→GL balance evidence;
- payment/reversal allocation evidence;
- provider-matrix acceptance results when production providers are certified;
- updated Help/documentation identifying the generic/localization boundary.
