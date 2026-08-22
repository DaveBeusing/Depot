# Phase 4 Technical Status — Business Record Integrity

Date: 2026-08-22

## Status

**TECHNICAL BASELINE IMPLEMENTED; ONE TRANSACTION-CONSISTENCY REMEDIATION OPEN**

This status intentionally separates implemented technical controls from legal/organizational GoBD acceptance and from one identified engineering gap.

## Implemented

- [x] Core business objects are classified by mutability, retention category, finalized state, correction mechanism and numbering rule in `BusinessRecordCatalog`.
- [x] Finalized workflow mutations already use state/version predicates at service/repository boundaries for the core purchasing, warehouse and sales workflows reviewed in Phase 4.
- [x] Posted/finalized corrections use explicit cancellation, reversal, return, close or credit-note workflows instead of destructive rewriting in the reviewed core workflows.
- [x] Original records remain retained when correction transactions are created.
- [x] Historical before/after snapshots are preserved in audit entries for audited business-state changes.
- [x] Permanent document-number families and no-reuse/no-renumber rules are defined in `BUSINESS_RECORD_INTEGRITY.md`.
- [x] Workflow attribution uses actor ids and UTC timestamps where the workflow requires attribution.
- [x] Core posted/finalized workflows reviewed during Phase 4 write business changes and audit evidence through database transaction contexts.
- [x] Correction reasons are mandatory for reviewed reversal/cancellation/credit workflows where a reason is needed to explain the change.
- [x] Retention categories are defined without hard-coding statutory periods.
- [x] A machine-readable classified business-record evidence export is available through `AuditLogService.ExportBusinessRecordEvidenceAsync`.
- [x] Evidence export includes chronological events, sanitized structured before/after data and the latest retained snapshot.
- [x] Backup/restore integrity expectations are documented together with schema/version verification requirements.
- [x] Technical procedural documentation covers creation, processing, storage, correction, export, backup, restore and migration.
- [x] Schema migration/change-control expectations preserve ids, permanent document numbers, audit history and semantic meaning.
- [x] Automated `BusinessRecordIntegrityTests` are part of the security/compliance CI gate.

## Open technical remediation

### Sales-order draft save and audit atomicity

`SalesOrderRepository.SaveDraftAsync` commits the draft record transaction before `SalesOrderService.SaveDraftAsync` writes the corresponding audit entry. This means a failure between those operations could leave a successfully saved draft without its audit entry.

This does not permit editing a finalized sales order and therefore does not currently undermine posted/finalized invoice or shipment immutability. It does, however, fail the stronger Phase 4 rule that every audited retained business mutation should commit atomically with its audit evidence.

**Required remediation before Phase 4 is marked technically complete:** move sales-order draft persistence and its created/updated audit insert into one `IDatabaseTransactionRunner`/repository transaction, matching the pattern already used by finalized workflow transitions.

## Legal/organizational acceptance still required

Even after the remaining technical remediation, a deployment-specific GoBD assessment must determine at least:

- which Depot records are tax-relevant in the actual business process,
- concrete retention periods,
- organizational controls and segregation of duties,
- procedural documentation ownership and approval,
- external system/interface responsibilities,
- required tax-authority export formats,
- operating and recovery procedures,
- change-management and release approval evidence.

The repository documentation is technical input and must not be presented as a legal certification.
