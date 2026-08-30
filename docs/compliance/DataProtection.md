# Depot Data Protection Baseline

## Purpose

This document establishes the technical privacy and data-protection baseline for Depot. Deployment-specific GDPR/DSGVO obligations remain the responsibility of the relevant controller/processor and require legal/organizational assessment in addition to software controls.

## Personal-data inventory

The authoritative technical inventory is maintained in `DataInventory.md`. It covers application users, customer and supplier/contact data, business documents, audit evidence, notifications, generated documents, exports, backups, protected settings, and diagnostics/logs.

## Principles

- Data minimization and purpose limitation.
- Privacy by design and by default.
- Least-privilege access.
- Defined lifecycle and retention decisions.
- Traceable privileged access where appropriate.
- No destructive erasure of records that must remain as business/audit evidence.
- No background external transmission without privacy/security review.

## Technical baseline

### Data inventory and flows

- [x] Identify personal-data fields/categories by entity and workflow (`DataInventory.md`).
- [x] Identify primary storage and derived copies including PDFs, spreadsheet/CSV exports, backups and diagnostics.
- [x] Document purposes and typical propagation paths.
- [x] Require inventory review when future telemetry/integrations are introduced.

### Lifecycle

- [x] Distinguish deletion, deactivation, anonymization, archival, and legal/business retention (`RetentionPolicy.md`).
- [x] Prefer deactivation over destructive deletion when historical references exist.
- [x] Keep posted/finalized business records and audit evidence outside blanket erasure workflows.
- [x] Document backup expiry/restoration implications for prior erasure/restriction actions.

Concrete legal retention periods and final erasure decisions are deployment/controller policy and are intentionally not hard-coded into automatic destructive jobs.

### Access and minimization

- [x] Existing RBAC limits customer, supplier, user, audit and administration surfaces.
- [x] Master-data services normalize inputs and apply bounded field lengths.
- [x] Audit/diagnostic presentation redacts credentials, hashes, tokens, keys and connection secrets.
- [x] Privacy discovery/export requires `AdministrationView` permission.
- [x] Privacy exports intentionally exclude password hashes and database credentials.

### Data-subject support

- [x] Provide Administration → Privacy Data discovery workflow.
- [x] Search users, customers, customer contacts, suppliers and attributable audit evidence by person-related identifiers.
- [x] Provide machine-readable JSON discovery export.
- [x] Bound per-source search results and require at least two search characters to reduce unintended bulk disclosure.
- [x] Preserve correction/deactivation workflows rather than introducing unsafe one-click deletion.

The discovery package is an administrative aid. A controller must still verify derived files, external recipients, backups and immutable business records when answering a real data-subject request.

### Logs and backups

Logs must not contain passwords, connection-string secrets, unnecessary full business-document content, or protected settings. Backup retention/access/encryption controls are defined in `AuditAndRecovery.md`; lifecycle interaction is defined in `RetentionPolicy.md`.

## Privacy review triggers

`PrivacyByDesign.md` and `TelemetryPolicy.md` require review before adding telemetry, cloud services, email providers, external identity, APIs/integrations, new person-related fields, analytics, crash upload, remote support, or other external/background transmission.
