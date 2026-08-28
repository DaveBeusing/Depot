# Depot Audit and Recovery Policy

Updated: 2026-08-28

## Audit trail

Depot audit timestamps are stored in UTC and the administration audit viewer converts date filters from the operator's local time to UTC. The viewer supports search, user/entity/action/date filters, pagination, sanitized detail comparison, and CSV export.

Audit records are append-oriented application evidence. Depot does not expose normal UI operations to edit or delete individual audit entries. Database administrators must treat direct modification of `AuditEntries` as a privileged break-glass operation and preserve independent database/audit evidence when it is ever required.

Security/privileged action names include administrator bootstrap/retirement, authentication/authorization administration, role/permission changes, administrator overrides, backup/restore operations, security configuration changes, and Finance posting-profile/journal/reversal actions where applicable.

Audit retention is deployment policy. Production deployments should retain audit evidence for the applicable legal, contractual, accounting, and operational period and should not use application-level deletion as routine housekeeping.

## Finance F1 atomic audit model

Finance F1 treats `FinanceJournalEntry` as an accounting-relevant retained record. General Ledger persistence and required audit evidence share one write transaction.

For a successful journal posting, the transaction can include:

- Finance number-sequence allocation;
- journal-entry header;
- journal-entry lines;
- dimension snapshots;
- operation/source idempotency evidence embedded in the journal identity;
- reversal link where the operation is a reversal;
- central Audit Log evidence.

If required Audit Log persistence fails, the Finance transaction rolls back. The application therefore does not intentionally leave a committed General Ledger entry or consumed Finance number without its required audit evidence.

Reversal creates a new linked journal entry; it does not edit the original. Both the reversal entry and the reversal action on the original are recorded transactionally.

Posting-profile create/update also uses service-layer authorization, optimistic concurrency, validation, and Audit Log evidence.

## Backup retention

Automatic backups use a minimum-preservation rule: Depot always keeps the ten newest automatic backups and removes additional `Depot-Auto-*.depotbackup` files only after they are older than 30 days. Manual and restore-safety backups are not removed by the automatic retention job.

Backup directories must be writable only by the intended Windows user/service and authorized administrators. For enterprise deployments the backup location should reside on storage with access control, monitoring and an independent backup policy.

## Backup encryption evaluation

Depot backup archives contain application data, including authentication hashes, business data, audit evidence, and—when Finance is enabled—General Ledger/posting-profile/accounting configuration data.

Built-in archive encryption is intentionally not introduced without a key-management design: embedding or deriving a static application key would provide misleading protection and password-based backup encryption would create recovery-key loss risk. Until a managed key store is implemented, production deployments must protect backup storage using OS/storage encryption and access controls.

## Recovery objectives

A deployment owner must define an RPO and RTO appropriate to the business. The default scheduler interval is not an RPO guarantee. Recovery acceptance requires a validated backup plus a successful restore/integrity check.

For deployments using Finance, the RPO/RTO decision must account for accounting records, sequence state, source/subledger reconciliation, period state, and downstream statutory/operational obligations.

## Recovery drill

For every supported database provider before production support is claimed:

1. create and validate a clean backup;
2. restore it into an isolated test environment;
3. confirm core schema and all applicable feature-schema versions;
4. run the provider integrity check;
5. verify authentication and representative inventory, purchasing, sales, and Finance workflows/data;
6. when Finance is in use, verify representative journal entries, lines, dimensions, operation/source identities, entry numbers, posting profiles, reversal links, and Audit Log evidence;
7. verify a restored Finance v1 database can migrate to Finance feature schema v2 where that upgrade path is supported;
8. reconcile representative source/subledger records to GL once those integrations exist;
9. record duration, operator, source version, target version, schema versions, and any deviations.

Automated SQLite tests provide baseline regression evidence. SQL Server and MySQL/MariaDB Finance migration/locking/concurrency/recovery drills require configured provider instances and remain deployment/release acceptance evidence rather than unit-test substitutes.

## Recovery integrity rules

A restore must preserve accounting/business relationships as a consistent point-in-time set. Operators must not selectively replace General Ledger entries, number-sequence state, posting profiles, audit evidence, or source records independently when doing so would make the database internally inconsistent.

After restore/migration, any discrepancy in Finance entry numbering, source idempotency, reversal links, account/dimension references, or Audit Log evidence requires investigation before production accounting use resumes.
