# Depot Audit and Recovery Policy

Updated: 2026-08-28

## Audit trail

Depot audit timestamps are stored in UTC and the administration audit viewer converts date filters from the operator's local time to UTC. Audit records are append-oriented application evidence; normal UI operations do not edit or delete individual audit entries. Direct database modification of `AuditEntries` is a privileged break-glass operation and must preserve independent evidence.

Audit retention is deployment policy. Production deployments should retain evidence for applicable legal, contractual, accounting and operational periods.

## Finance atomic audit model

`FinanceJournalEntry` is an accounting-relevant retained record. General Ledger persistence and required audit evidence share one write transaction. A successful posting can include number-sequence allocation, journal header/lines, dimension snapshots, operation/source idempotency evidence, reversal links and central Audit Log evidence.

If required Audit persistence fails, the Finance transaction rolls back. Reversal creates a new linked journal entry; it never edits the original. Posting-profile create/update uses service authorization, optimistic concurrency, validation and Audit evidence.

## Backup retention and protection

Automatic backups keep at least the ten newest automatic backups and remove older excess automatic files only after 30 days. Manual and restore-safety backups are not removed by automatic retention. Production backup locations require appropriate access controls, monitoring and independent storage protection.

Depot backup archives can contain authentication hashes, business data, audit evidence and Finance configuration/accounting data. Built-in archive encryption is not claimed without a key-management design; deployments must protect storage using OS/storage encryption and access controls.

## Recovery objectives and drill

Deployment owners define RPO/RTO. Recovery acceptance requires a validated backup plus successful restore/integrity checks. For Finance deployments, recovery decisions must include accounting records, sequence state, source/subledger reconciliation, period state and downstream obligations.

For each supported provider before production support is claimed:

1. create and validate a clean backup;
2. restore it into an isolated environment;
3. confirm core and feature-schema versions;
4. run provider integrity checks;
5. verify authentication and representative inventory, purchasing, sales and Finance workflows;
6. verify representative journal entries, lines, dimensions, source identities, numbers, posting profiles, reversals and Audit evidence;
7. verify supported Finance schema upgrade paths through current schema **9**;
8. reconcile representative source/subledger records to GL;
9. record duration, operator, versions and deviations.

Automated SQLite tests provide baseline regression evidence. SQL Server and MySQL/MariaDB migration/locking/concurrency/recovery drills remain deployment/release acceptance evidence.

## Recovery integrity rules

A restore must preserve accounting/business relationships as a consistent point-in-time set. Operators must not selectively replace General Ledger entries, number-sequence state, posting profiles, audit evidence or source records when doing so makes the database internally inconsistent. Discrepancies in entry numbering, source idempotency, reversal links, account/dimension references or Audit evidence require investigation before production accounting resumes.
