# Phase 4 Technical Status — Business Record Integrity

Date: 2026-08-22

## Status

**TECHNICAL IMPLEMENTATION COMPLETE — 2026-08-22**

This status describes the software controls and engineering evidence implemented for Phase 4. It does not constitute legal certification or deployment-specific GoBD acceptance.

## Implemented

- [x] Core business objects are classified by mutability, retention category, finalized state, correction mechanism and numbering rule in `BusinessRecordCatalog`.
- [x] Finalized workflow mutations use state/version predicates at service/repository boundaries for the core purchasing, warehouse and sales workflows reviewed in Phase 4.
- [x] Posted/finalized corrections use explicit cancellation, reversal, return, close or credit-note workflows instead of destructive rewriting in the reviewed core workflows.
- [x] Original records remain retained when correction transactions are created.
- [x] Historical before/after snapshots are preserved in audit entries for audited business-state changes.
- [x] Permanent document-number families and no-reuse/no-renumber rules are defined in `BUSINESS_RECORD_INTEGRITY.md`.
- [x] Workflow attribution uses actor ids and UTC timestamps where the workflow requires attribution.
- [x] Retained business mutations reviewed during Phase 4 write business changes and audit evidence through database transaction contexts.
- [x] Sales-order draft persistence and its created/updated audit entry now commit atomically in one `IDatabaseTransactionRunner` transaction.
- [x] A regression test deliberately rejects the sales-order audit insert and verifies that the corresponding draft record is rolled back as well.
- [x] Correction reasons are mandatory for reviewed reversal/cancellation/credit workflows where a reason is needed to explain the change.
- [x] Retention categories are defined without hard-coding statutory periods.
- [x] A machine-readable classified business-record evidence export is available through `AuditLogService.ExportBusinessRecordEvidenceAsync`.
- [x] The Audit Log UI exposes the evidence export for classified records.
- [x] Evidence export includes chronological events, sanitized structured before/after data and the latest retained snapshot.
- [x] Evidence export requires dedicated audit-export authorization in addition to audit-log viewing permission.
- [x] Backup/restore integrity expectations are documented together with schema/version verification requirements.
- [x] Technical procedural documentation covers creation, processing, storage, correction, export, backup, restore and migration.
- [x] Schema migration/change-control expectations preserve ids, permanent document numbers, audit history and semantic meaning.
- [x] Automated `BusinessRecordIntegrityTests` are part of the security/compliance CI gate.

## Engineering evidence

- `src/Depot/Models/BusinessRecordClassification.cs`
- `src/Depot/Services/AuditLogService.cs`
- `src/Depot/Repositories/SalesOrderRepository.cs`
- `src/Depot/Services/SalesOrderService.cs`
- `src/Depot/ViewModels/Administration/AuditLogViewModel.cs`
- `src/Depot/Views/Administration/AuditLogView.xaml`
- `tests/Depot.Tests/BusinessRecordIntegrityTests.cs`
- `docs/compliance/BUSINESS_RECORD_INTEGRITY.md`
- `docs/compliance/PROCEDURAL_DOCUMENTATION.md`

## Legal/organizational acceptance still required

A deployment-specific GoBD assessment must still determine at least:

- which Depot records are tax-relevant in the actual business process,
- concrete statutory/organizational retention periods,
- organizational controls and segregation of duties,
- procedural-documentation ownership and approval,
- external system/interface responsibilities,
- tax-authority-specific export requirements,
- operating and recovery procedures,
- change-management and release-approval evidence.

The repository documentation is technical input and must not be presented as legal certification.
