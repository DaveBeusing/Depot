# Depot Threat Model

## Status

Initial threat-model framework. Detailed threat enumeration and risk scoring remain to be completed as part of the security roadmap.

## Scope

Depot is a Windows desktop application with WPF/MVVM architecture and a provider-neutral persistence layer supporting SQLite, SQL Server, MySQL, and MariaDB. It handles inventory, warehouse, procurement, sales, administration, reporting, authentication, authorization, audit data, documents, imports/exports, and backups.

## Assets

Key assets include:

- user identities and credentials,
- roles and permissions,
- customer and supplier data,
- inventory quantities and valuations,
- purchase and sales records,
- invoices and credit notes,
- audit records,
- database credentials,
- application configuration,
- backups,
- generated PDFs and exports,
- release/signing assets.

## Trust boundaries

1. User -> Depot desktop UI.
2. UI/ViewModel -> business services.
3. Business services -> repositories/data access.
4. Depot -> local SQLite database.
5. Depot -> remote SQL Server/MySQL/MariaDB server.
6. Depot -> filesystem for settings, logs, backups, PDFs, and exports.
7. Build system -> package/dependency sources.
8. CI/release process -> signing and published artifacts.

## Threat categories

Use STRIDE as the initial analysis method.

### Spoofing

Examples:

- stolen/reused credentials,
- insecure first-run administrator credentials,
- impersonation through compromised database accounts.

### Tampering

Examples:

- unauthorized database modification,
- alteration of finalized business records,
- modification/deletion of audit records,
- malicious modification of settings or backups,
- dependency/build artifact compromise.

### Repudiation

Examples:

- sensitive action without attributable audit event,
- administrator override without reason,
- correction/reversal without linkage to the original transaction.

### Information disclosure

Examples:

- credentials in settings/logs,
- customer/invoice data in logs,
- unprotected backups,
- unencrypted remote database connections,
- excessive information in exception messages.

### Denial of service

Examples:

- database lock/contention,
- oversized imports/reports,
- storage exhaustion from logs/backups,
- malicious or malformed files,
- unavailable remote database provider.

### Elevation of privilege

Examples:

- UI-only permission checks,
- role union mistakes,
- insecure administrator override,
- direct repository path bypassing service authorization.

## High-priority scenarios

- [ ] Compromise of initial administrator setup.
- [ ] Privilege escalation through missing service-level authorization.
- [ ] Unauthorized modification of finalized transactions.
- [ ] Audit-log tampering.
- [ ] Database credential disclosure.
- [ ] Sensitive information disclosure through logs/backups/exports.
- [ ] SQL/database-provider injection or unsafe dynamic SQL.
- [ ] Malicious Excel/import input.
- [ ] Backup substitution or malicious restore.
- [ ] Supply-chain compromise through NuGet dependency/build process.
- [ ] Release binary modification or unsigned distribution.

## Risk record

Each material threat should eventually record:

| Field | Description |
| --- | --- |
| ID | Stable threat identifier |
| Asset | Affected asset |
| Threat | Threat scenario |
| Boundary | Trust boundary involved |
| Likelihood | Defined project scale |
| Impact | Defined project scale |
| Risk | Resulting risk rating |
| Controls | Existing/proposed mitigations |
| Tests | Evidence validating controls |
| Residual risk | Remaining risk |
| Owner | Responsible role |
| Status | Open/mitigated/accepted |

## Review triggers

Review the threat model when authentication, authorization, database connectivity, remote services, update mechanisms, import/export, backup/restore, identity integration, or major business workflows materially change.
