# Depot Business Record Integrity Baseline

## Purpose

This document defines the technical integrity model used by Depot for business records that may become relevant to German GoBD-oriented operational or accounting processes. It is engineering evidence, not a legal determination that a specific deployment satisfies GoBD.

## Core integrity rules

1. Draft records may be edited only while their workflow explicitly permits editing.
2. Finalized or posted records are not corrected by overwriting their historical business content.
3. Corrections use explicit status transitions, reversals, returns, cancellations, credit notes, or other compensating transactions.
4. The original record and the correcting transaction remain identifiable.
5. Actor, UTC timestamp, action, entity type, entity id, and before/after snapshots are retained in the audit history where the workflow is auditable.
6. Business data and its audit event should commit in the same database transaction whenever a workflow changes retained business state.
7. Assigned document numbers are stable identifiers. Numbers are never silently reassigned to another record.
8. Database backups and restores preserve database identity, document numbers, links, status, audit history, and schema version as a unit.

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
| Stock movement | Audit evidence | None after creation | Immediately retained | Linked reversal movement |

Concrete statutory retention periods are intentionally not hard-coded. Deployment policy must determine them.

## Document numbering

Depot currently assigns document numbers from the database identity of the newly created record. A temporary `PENDING-...` value may exist only inside the creation transaction before the permanent number is written.

Supported numbering families include:

- `PO-######` purchase orders,
- `GR-######` goods receipts,
- `ST-######` stock transfers,
- `IC-######` inventory counts,
- `MI-######` material issues,
- `MR-######` material returns,
- `SR-######` supplier returns,
- `SO-######` sales orders,
- `SH-######` shipments,
- `CR-######` customer returns,
- `INV-######` sales invoices,
- `CN-######` sales credit notes.

Rules:

- a permanent number identifies one database record,
- finalized numbers are not edited by normal workflows,
- gaps are permitted and are not back-filled merely to produce cosmetic continuity,
- deleted or rolled-back creation attempts must not cause reuse logic to be added,
- imports/migrations must preserve historical numbers or record an explicit mapping,
- changing the numbering scheme is a compliance-impacting migration and requires review.

## Immutability and corrections

Repositories and services must enforce state predicates in update statements, not rely only on disabled UI controls. Typical examples are draft-only update predicates and expected-state/expected-version transition predicates.

Once a record has produced external or financial effect, correction must use an explicit workflow. Examples already implemented include shipment reversal, stock movement reversal, customer return, supplier return and invoice credit note. Correction reasons are mandatory where the operation would otherwise be ambiguous.

A correction must not erase the original record. The original and correction remain reconstructable through direct references, document relationships, stock-movement references and/or audit history.

## Audit consistency

Workflow code should use `IDatabaseTransactionRunner` or repository transaction helpers so the business mutation and the corresponding `AuditEntries` insert commit or roll back together. This is required for posted/finalized business-state changes.

A code review finding exists whenever a retained business mutation is committed first and its audit entry is written later in an independent transaction. Such findings are release blockers for workflows classified as accounting-relevant or finalized business transactions.

Notifications, UI refreshes and email delivery are deliberately outside the authoritative database transaction. Failure to notify must not roll back a correctly committed business transaction, and notification success must never be treated as accounting evidence.

## Machine-readable evidence export

`AuditLogService.ExportBusinessRecordEvidenceAsync` produces an administrator-authorized JSON evidence package for classified records. The package contains:

- export schema identifier,
- UTC export timestamp,
- entity type and database id,
- executable record classification,
- event count,
- chronological audit events,
- actor and UTC event timestamps,
- sanitized before/after structured JSON,
- the latest retained snapshot.

Exports require both `AuditLogView` and `AuditLogExport`. Secrets handled by the audit sanitizer remain excluded.

The evidence export is not a substitute for a tax-authority-specific export format. It provides a stable technical reconstruction format that later regulatory/export adapters can consume.

## Readability

Structured database records and audit snapshots are the authoritative technical source. Generated PDFs and spreadsheets are derived representations unless a future workflow explicitly designates a generated artifact as authoritative.

For retained records Depot must preserve enough structured values to regenerate or interpret the transaction after the original UI workflow has changed. Release/migration documentation must identify any migration that changes semantic meaning rather than only physical storage.

## Backup and restore

A backup is a point-in-time copy of business records and audit evidence. Restore procedures must preserve relationships and must not selectively restore a business document without the dependent state required to explain it.

After restoring an older database:

1. verify schema migration completes successfully,
2. verify database integrity,
3. verify document identities and audit history remain present,
4. reapply any post-backup operational/privacy actions that are still applicable,
5. record the recovery operation operationally.

## Change review triggers

A compliance/security review is required when changing:

- permanent document-number assignment,
- final-state mutability,
- correction/reversal behavior,
- audit-event creation or transaction boundaries,
- deletion of business records,
- migration of retained fields,
- backup/restore semantics,
- generated financial documents,
- machine-readable business-record export.
