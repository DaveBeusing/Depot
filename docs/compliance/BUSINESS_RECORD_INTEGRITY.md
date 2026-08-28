# Depot Business Record Integrity Baseline

Updated: 2026-08-28

## Purpose

This document defines the technical integrity model used by Depot for business records relevant to operational, accounting and audit processes. It is engineering evidence, not a legal determination that a deployment satisfies GoBD or another statutory regime.

## Core integrity rules

1. Draft records may be edited only while their workflow explicitly permits it.
2. Finalized or posted records are not corrected by overwriting historical business content.
3. Corrections use explicit reversals, returns, cancellations, credit notes or compensating transactions.
4. Original and correcting records remain identifiable.
5. Actor, UTC timestamp, action, entity type/id and before/after snapshots are retained where auditable.
6. Authoritative business/accounting mutation and required Audit evidence commit in the same transaction where applicable.
7. Permanent document/accounting numbers remain bound to their records.
8. Backups/restores preserve identities, links, status, Audit history and schema state as a unit.

## Record classification

The executable classification is defined in `BusinessRecordCatalog` and is included in business-record evidence exports. Accounting-relevant Finance records, imported bank statements, report snapshots and localization assignment/registry evidence use explicit retained classifications and correction mechanisms.

Concrete statutory retention periods are intentionally not hard-coded. Deployment/jurisdiction policy determines them.

## Numbering and General Ledger integrity

Finance uses configurable `FinanceNumberSequence` records for General Ledger entries. The selected sequence belongs to the relevant Legal Entity/document type. Allocation occurs inside the journal transaction and advances with an expected-value/concurrency guard. Failure of line persistence, reversal linking or required Audit persistence rolls sequence state back. Idempotent retries return existing entries instead of consuming new numbers.

Once a `FinanceJournalEntry` is posted, its header/lines/dimension snapshots, currencies/rate snapshot, operation/source identity and number are retained. Correction uses a new linked reversal entry; the original is never rewritten by the normal workflow.

## Audit consistency

Workflow code uses `IDatabaseTransactionRunner` / transaction-session infrastructure so retained mutation and required `AuditEntries` inserts commit or roll back together. A code-review finding exists whenever an accounting-relevant/finalized mutation is committed first and required Audit evidence is written later in an independent transaction.

Notifications, UI refreshes and email delivery remain outside the authoritative database transaction and are not accounting evidence.

## Idempotency and source traceability

Finance uses operation IDs, deterministic request fingerprints and unique source identities to protect replay-sensitive accounting workflows. Reusing an identity for different content is rejected. AR/AP/Inventory/Banking integrations preserve these boundaries so retries cannot duplicate General Ledger truth.

## Machine-readable evidence export

`AuditLogService.ExportBusinessRecordEvidenceAsync` produces administrator-authorized JSON evidence for classified records, including classification, chronological Audit events, actors/timestamps and sanitized snapshots. The export is a technical reconstruction format, not a statutory/tax-authority export specification.

## Backup and restore

A backup is a point-in-time copy of business records and Audit/accounting evidence. Restore procedures must preserve relationships and must not selectively restore a document/journal without dependent state required to explain it. After restore, verify schema migration, integrity, record identities, journal/reversal/source links and Audit history before production accounting resumes.

## Change review triggers

Compliance/security/accounting review is required when changing permanent numbering, final-state mutability, balancing/currency snapshots, correction/reversal behavior, idempotency semantics, period enforcement, Audit transaction boundaries, retained fields, backup/restore semantics, generated financial documents or machine-readable evidence export.
