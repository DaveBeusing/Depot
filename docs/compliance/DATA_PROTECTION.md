# Depot Data Protection Baseline

## Purpose

This document establishes the initial privacy and data-protection framework for Depot. Deployment-specific GDPR/DSGVO obligations remain the responsibility of the relevant controller/processor and require legal/organizational assessment in addition to software controls.

## Likely personal-data locations

Depot may process personal data in:

- application users,
- customers and contacts,
- supplier contacts,
- sales/purchasing documents,
- audit records,
- application logs,
- PDFs and spreadsheet exports,
- database backups,
- settings/configuration where user identifiers are stored.

## Principles

- Data minimization.
- Purpose limitation.
- Privacy by design and by default.
- Least-privilege access.
- Defined retention.
- Traceable administrative access where appropriate.
- Secure deletion/anonymization where legally and technically appropriate.

## Required work

### Data inventory

- [ ] Identify personal-data fields by entity.
- [ ] Identify storage locations and derived copies.
- [ ] Identify exports and generated documents.
- [ ] Identify backup copies and retention.
- [ ] Document future external integrations/telemetry before use.

### Lifecycle

For each data category determine whether Depot should support:

- deletion,
- deactivation,
- anonymization/pseudonymization,
- archival,
- legal/business retention.

Deletion must not destroy records that must remain immutable or retained for another lawful reason; those cases require a defined alternative such as restricted retention or anonymization where appropriate.

### Access

- [ ] Ensure permissions restrict access to personal data according to role.
- [ ] Review administrator capabilities.
- [ ] Record appropriate privileged actions.
- [ ] Avoid exposing unnecessary personal data in list views, errors, notifications, and logs.

### Data-subject support

- [ ] Provide reliable search/identification of relevant person-related records.
- [ ] Define export procedures/capability where applicable.
- [ ] Define correction workflows.
- [ ] Define deletion/anonymization workflows subject to retention constraints.

### Logs

Logs should not contain passwords, connection-string secrets, unnecessary customer details, full invoice content, or other sensitive values unless specifically justified and protected.

### Backups

Backup retention must be documented. Access must be restricted. Recovery procedures must account for data-protection obligations and avoid uncontrolled proliferation of restored copies.

## Privacy review triggers

Perform a privacy review when adding:

- telemetry,
- cloud services,
- email sending,
- external identity providers,
- APIs/integrations,
- new customer/supplier/person fields,
- analytics,
- remote support features.
