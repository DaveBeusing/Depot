# Depot Business Record Integrity Baseline

Updated: 2026-08-28

## Purpose

This document defines the technical integrity model used by Depot for business records that may become relevant to operational, accounting, audit, or German GoBD-oriented processes. It is engineering evidence, not a legal determination that a specific deployment satisfies GoBD or another accounting/statutory regime.

## Core integrity rules

1. Draft records may be edited only while their workflow explicitly permits editing.
2. Finalized or posted records are not corrected by overwriting their historical business content.
3. Corrections use explicit status transitions, reversals, returns, cancellations, credit notes, or other compensating transactions.
4. The original record and the correcting transaction remain identifiable.
5. Actor, UTC timestamp, action, entity type, entity id, and before/after snapshots are retained in audit history where the workflow is auditable.
6. Business/accounting data and its required audit event commit in the same database transaction whenever a workflow changes retained authoritative state.
7. Assigned permanent document/accounting numbers remain bound to their record. Numbers are never silently reassigned to another record.
8. Database backups and restores preserve database identity, document numbers, accounting numbers, links, status, audit history, and schema/feature-schema state as a unit.

## Record classification

The executable classification is defined in `BusinessRecordCatalog` and is included in business-record evidence exports.

| Record | Category | Editable state | Finalized/retained state | Correction model |
| --- | --- | --- | --- | --- |
| Purchase order | Business transaction | Draft | Ordered/received/closed/cancelled history | Close, receipt/return or compensating workflow |
| Goods receipt | Accounting-relevant source record | Preparation | Posted/reversed | Explicit reversal with reason |
| Stock transfer | Business transaction | Draft | Posted/reversed | Explicit reversal with compensating movements |
| Inventory count | Business transaction | Draft/counting | Posted/reversed | Explicit correction/reversal |
| Material issue | Business transaction | Draft | Posted/reversed | Material return/reversal |
| Material return | Business transaction | Draft | Posted/reversed | Explicit reversal |
| Supplier return | Business transaction | Draft | Posted/reversed | Explicit reversal |
| Sales order | Business transaction | Draft | Released/shipped/completed/cancelled | Cancellation before fulfilment; downstream return/credit afterwards |
| Shipment | Business transaction | Draft | Posted/reversed | Reversal before invoicing; customer return afterwards |
| Customer return | Business transaction | Draft | Posted | Separate corrective transaction |
| Sales invoice | Accounting-relevant | Draft | Posted | Credit note; posted invoice is retained |
| Sales credit note | Accounting-relevant | Draft | Posted | Additional correcting transaction if required |
| Finance journal entry | Accounting-relevant | None after posting | Posted | Explicit linked reversal journal entry; original remains immutable |
| Stock movement | Audit evidence | None after creation | Immediately retained | Linked reversal movement |

Concrete statutory retention periods are intentionally not hard-coded. Deployment/jurisdiction policy must determine them.

## Numbering

Operational documents currently use stable record-linked numbering families such as PO, GR, transfer/count/material/supplier-return, sales order/shipment/customer-return, invoice and credit-note numbers.

Finance F1 introduces a separate configurable `FinanceNumberSequence` boundary for General Ledger entries. The selected sequence must belong to the same legal entity and appropriate Finance General Ledger document type.

Finance numbering rules:

- number allocation occurs inside the same write transaction as the journal entry;
- the sequence row is advanced with an expected-value/concurrency guard;
- failure of line persistence, reversal linking, or required Audit Log persistence rolls the sequence update back;
- an idempotent retry returns the already-created entry and does not consume another number;
- permanent General Ledger numbers are not reassigned to another journal entry;
- localization may define numbering policy, but must not silently rewrite historical numbers.

Gaps can still arise from external/migration/manual policy or database/operator actions; Depot must not back-fill/reassign historical identifiers merely for cosmetic continuity.

## Finance F1 immutability and correction

`FinanceJournalEntry` is explicitly classified by `BusinessRecordCatalog` as `AccountingRelevant`.

Once posted:

- its original header/lines/dimension snapshots are retained;
- transaction/reporting currencies and exchange-rate snapshot remain historical evidence;
- operation/source identity remains attached to the accounting event;
- normal F1 workflows do not edit/delete the original entry;
- correction uses a new linked reversal entry;
- the reversal swaps the original transaction and reporting debit/credit amounts exactly;
- a second reversal of the same original is rejected.

The reversal entry and the reversal action on the original are auditable events. Future subledger/source integrations must preserve this model rather than mutating GL truth after posting.

## Audit consistency

Workflow code uses `IDatabaseTransactionRunner` / transaction-session infrastructure so retained business/accounting mutation and required `AuditEntries` inserts commit or roll back together.

For Finance F1, journal creation, number allocation, lines/dimensions, reversal linking, and Audit Log evidence participate in the same accounting transaction. If the required audit write fails, the journal transaction rolls back. An unaudited F1 posting is therefore not considered successfully committed by the application workflow.

A code review finding exists whenever an accounting-relevant/finalized mutation is committed first and its required audit evidence is written later in an independent transaction.

Notifications, UI refreshes, and email delivery remain outside the authoritative database transaction. Notification success/failure is not accounting evidence.

## Idempotency and source traceability

F1 adds two complementary replay-safety boundaries:

- unique operation ID + deterministic request fingerprint;
- unique accounting-book/source-type/source-id/source-event identity for source postings.

An identical retry can return the existing accounting record. Reusing either identity for different accounting content is rejected. Later AR/AP/inventory/banking integrations must use these boundaries so source retries cannot duplicate General Ledger truth.

## Machine-readable evidence export

`AuditLogService.ExportBusinessRecordEvidenceAsync` produces administrator-authorized JSON evidence for classified records, including classification, chronological audit events, actors/timestamps, sanitized structured snapshots, and retained latest evidence where applicable.

`FinanceJournalEntry` participates in the executable business-record classification. The evidence export remains a technical reconstruction format, not a tax-authority/statutory export specification.

## Readability and reconstruction

Structured database records and audit snapshots are authoritative technical sources. Generated PDFs/spreadsheets are derived representations unless a specific workflow designates otherwise.

Finance reconstruction must preserve enough structured data to understand the book/journal, posting date, source identity, currencies/rate snapshot, account/line amounts, dimensions, entry number, reversal relationship, and audit trail independently of later mutable master/reference changes.

## Backup and restore

A backup is a point-in-time copy of business records and audit/accounting evidence. Restore procedures must preserve relationships and must not selectively restore a document/journal without dependent state required to explain it.

After restoring an older database:

1. verify core and feature-schema migration completes successfully;
2. verify database integrity;
3. verify document/Finance entry identities and audit history remain present;
4. verify Finance feature schema and representative journal/reversal/source links when Finance is in use;
5. reapply any post-backup operational/privacy actions still applicable;
6. record the recovery operation operationally.

## Change review triggers

A compliance/security/accounting review is required when changing:

- permanent document/General Ledger number assignment;
- final-state mutability;
- Finance balancing/currency snapshot behavior;
- correction/reversal behavior;
- operation/source idempotency semantics;
- period-close/reopen enforcement;
- audit-event creation or transaction boundaries;
- deletion/migration of retained fields;
- backup/restore semantics;
- generated financial documents;
- machine-readable business-record/accounting export.
