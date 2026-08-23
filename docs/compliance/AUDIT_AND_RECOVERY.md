# Depot Audit and Recovery Policy

## Audit trail

Depot audit timestamps are stored in UTC and the administration audit viewer converts date filters from the operator's local time to UTC. The viewer supports search, user/entity/action/date filters, pagination, sanitized detail comparison, and CSV export.

Audit records are append-oriented application evidence. Depot does not expose normal UI operations to edit or delete individual audit entries. Database administrators must treat direct modification of `AuditEntries` as a privileged break-glass operation and preserve independent database/audit evidence when it is ever required.

Security-relevant action names include `InitialAdministratorCreated`, `RetiredLegacyAdministrator`, authentication/authorization administration, role/permission changes, administrator overrides, backup/restore operations, and security configuration changes. New privileged workflows must create an attributable audit event or document why another evidence source is authoritative.

Audit retention is deployment policy. Production deployments should retain audit evidence for the applicable legal, contractual and operational period and should not use application-level deletion as a routine housekeeping mechanism.

## Backup retention

Automatic backups use a minimum-preservation rule: Depot always keeps the ten newest automatic backups and removes additional `Depot-Auto-*.depotbackup` files only after they are older than 30 days. Manual and restore-safety backups are not removed by the automatic retention job.

Backup directories must be writable only by the intended Windows user/service and authorized administrators. For enterprise deployments the backup location should reside on storage with access control, monitoring and an independent backup policy.

## Backup encryption evaluation

Depot backup archives contain application data, including authentication hashes and business data. Built-in archive encryption is intentionally not introduced without a key-management design: embedding or deriving a static application key would provide misleading protection and password-based backup encryption would create recovery-key loss risk. Until a managed key store is implemented, production deployments must protect backup storage using OS/storage encryption (for example BitLocker/EFS or encrypted enterprise backup storage) and access controls.

## Recovery objectives

A deployment owner must define an RPO and RTO appropriate to the business. The default scheduler interval is not an RPO guarantee. Recovery acceptance requires a validated backup plus a successful restore/integrity check.

## Recovery drill

For every supported database provider before production support is claimed:

1. create and validate a clean backup;
2. restore it into an isolated test environment;
3. confirm schema version and representative record counts;
4. run the provider integrity check;
5. verify authentication, critical inventory, purchasing and sales workflows;
6. record duration, operator, source version, target version and any deviations.

Automated SQLite tests provide baseline regression evidence. SQL Server and MySQL/MariaDB drills require configured provider instances and remain deployment/release acceptance evidence rather than unit-test substitutes.
