# Depot Technical Procedural Documentation Baseline

## Purpose

This document provides the technical input for a deployment-specific GoBD-oriented procedural documentation package. It describes how Depot creates, processes, stores, changes, corrects, exports, backs up, restores and migrates business data. Organizational procedures, legal retention periods, tax-process responsibilities and operator controls must be completed for the actual deployment.

## System scope

Depot is a Windows desktop inventory and business-operations application with support for SQLite, SQL Server and MySQL/MariaDB database providers. Business workflows include master data, purchasing, inventory movements, warehouse operations, sales orders, shipments, returns, invoicing, credit notes, administration and audit evidence.

The application database is the authoritative structured data store. Generated PDFs, CSV files, spreadsheets, emails and backups are derived or copied representations unless a specific future workflow explicitly designates otherwise.

## Data creation

Business records are created through permission-checked service boundaries. New numbered documents are initially inserted with a temporary value where necessary, receive a permanent identity-derived document number inside the creation transaction, and are then returned to the caller.

Draft records may be edited only in explicitly permitted draft states. Optimistic concurrency versions prevent stale clients from silently overwriting newer data.

## Processing and workflow

State transitions are enforced by services and repository update predicates. Important transitions verify both expected state and expected version. Actor identifiers and UTC timestamps are written when the workflow requires attribution.

Examples include:

- purchase-order submission, approval, ordering and closure,
- sales-order submission, approval, release and cancellation,
- posting and reversing inventory-affecting transactions,
- posting shipments,
- posting invoices,
- issuing and posting credit notes,
- customer and supplier return workflows.

UI availability is not considered an integrity control by itself; service and persistence layers enforce the relevant boundary.

## Storage

The selected relational database provider stores business data, workflow state and audit entries. Referential relationships connect documents and dependent lines. Version columns support concurrency control. Audit entries contain UTC timestamp, actor, entity type, entity id, action and serialized before/after state where applicable.

Database provider configuration and secrets are governed by `SecureConfiguration.md`. Database access must follow least-privilege requirements.

## Corrections

Finalized records are corrected by explicit business workflows rather than destructive editing. Depending on the record type this includes cancellation, reversal, return, closure or credit-note transactions. Required reasons are stored where the correction would otherwise be ambiguous.

The original record remains available. Corrections create new state and/or linked compensating records so the sequence remains reconstructable.

## Audit trail

Audited retained state changes should write the business mutation and corresponding audit entry in the same database transaction. Audit-log presentation and export sanitize sensitive values.

The audit viewer supports filtering by time, user, entity, action and entity id. CSV audit export and structured classified-record evidence export are permission controlled.

## Export

Depot supports several export classes:

- operational CSV/report exports,
- generated business documents such as PDFs,
- audit CSV export,
- privacy discovery JSON export,
- classified business-record evidence JSON export.

`AuditLogService.ExportBusinessRecordEvidenceAsync` is the technical reconstruction export for classified business records. It includes the chronological audit history and latest structured snapshot. Tax-authority-specific formats remain separate future work where legally required.

## Backup

Automatic backup retention preserves at least the ten newest automatic backups and deletes additional automatic backups only after they are older than the configured technical retention baseline. Production storage must be access controlled and protected according to the backup/security documentation.

Backups contain historical business and audit information and therefore inherit the protection requirements of the source database.

## Restore

Restore is an operationally privileged action. Recovery must be followed by integrity verification and schema migration where required. Restoring an older backup can reintroduce historic state; operational and privacy actions performed after the backup may need to be re-applied.

A restore must not be used as a mechanism to selectively rewrite or remove historical business records.

## Database migration

`DatabaseInfo.Version` records the application schema version. `DatabaseVersion.CurrentVersion` identifies the current core schema level. Provider initialization applies ordered migrations from older supported versions to the current version. Sales schema migration is executed during database composition initialization where applicable.

Migration rules for retained business data:

1. preserve stable primary keys where possible,
2. preserve permanent document numbers,
3. preserve actor/timestamp/correction relationships,
4. avoid silently changing historical semantic values,
5. use explicit data transformation when a representation changes,
6. test upgrades from supported predecessor versions,
7. document migrations that affect interpretation, integrity or export,
8. never reset audit history merely because the physical schema changes.

The source repository and version-control history are the authoritative technical change history for schema code.

## Numbering and identity

Permanent business document numbers use stable prefixes and database identities as documented in `BusinessRecordIntegrity.md`. Gaps are acceptable. Numbers must not be recycled or silently reassigned to improve apparent sequence continuity.

## Access control

RBAC permissions protect viewing, creation, editing, posting, reversing, approving and exporting operations. Administrative and audit exports require dedicated permissions. Database administrator access is outside normal application controls and is treated as privileged break-glass access.

## Time handling

Security/audit evidence and workflow attribution timestamps use UTC. Local time may be displayed in the UI for usability, but the stored UTC timestamp remains the technical reference for chronological reconstruction.

## Technical evidence

Relevant evidence includes:

- automated workflow and authorization tests,
- business-record integrity tests,
- audit records,
- schema version and migration code,
- source-control history,
- release hashes and build provenance,
- backup/recovery test evidence,
- security/compliance documentation.

## Deployment-specific completion required

A production operator must supplement this technical baseline with organization-specific information such as process owners, segregation of duties, approval rules, retention periods, tax-relevant classifications, operating procedures, backup responsibility, recovery records, change-management approvals and external system interfaces.
