# Production Operations & Disaster Recovery

Updated: 2026-09-16

## Purpose

This document defines the production operating boundary for Depot backup, restore and disaster recovery.

Depot supports SQLite, SQL Server, MariaDB and MySQL, but it does not replace provider-native backup infrastructure. The product and CI prove technical restore boundaries; every production deployment must still own scheduling, retention, encryption, off-host copies, monitoring, RPO/RTO and real restore drills.

A deployment is not operationally accepted merely because a backup job reports success.

## Required deployment DR profile

Before production use, create an ACTIVE deployment profile based on:

`docs/operations/DisasterRecoveryProfile.example.json`

Validate it with:

```powershell
./scripts/operations/Test-DisasterRecoveryProfile.ps1 `
  -ProfilePath <deployment-profile.json> `
  -EvidencePath <validation-evidence.json>
```

The production profile must contain explicit:

- database provider;
- RPO in minutes;
- RTO in minutes;
- backup retention;
- minimum off-host copy count;
- encryption-at-rest requirement;
- backup-monitoring requirement;
- maximum restore-drill age;
- backup owner;
- restore owner;
- Depot application support owner;
- escalation owner.

Depot does not define one universal RPO or RTO. The numbers in the repository example are planning examples only and are not a product SLA or contractual commitment. The production owner must choose and accept values appropriate to the deployment.

## Responsibility model

### Customer / deployment operator

The deployment operator owns:

- backup schedule and infrastructure;
- provider credentials and privileged database operations;
- retention and legal retention requirements;
- encryption at rest and backup-key recovery;
- off-host/off-system copies;
- backup-job monitoring and alerting;
- storage capacity;
- database-service availability;
- provider-native restore execution for server databases;
- periodic isolated restore drills;
- accepted RPO/RTO;
- evidence retention and escalation contacts.

### Depot application support

Depot support owns:

- supported provider/version boundary documentation;
- DepotManager diagnostics and sanitized support package behavior;
- application/schema recognition after restore;
- guidance for application repair after database recovery;
- investigation of Depot-specific migration, schema or application failures;
- generic provider recovery CI evidence for the supported baselines.

Depot support does not silently perform destructive production database restores, choose customer retention policy, hold customer backup encryption keys or replace database-administrator responsibility.

## Recovery invariants

Every production restore drill must satisfy all of the following:

1. the drill restores into an isolated target unless the event is an explicitly authorized real disaster recovery;
2. the selected backup is identified and immutable for the duration of the drill;
3. the restore completes without using a production database as a disposable test target;
4. database integrity/provider validation succeeds;
5. Depot recognizes the restored schema without an unplanned destructive migration;
6. a known retained business marker or equivalent validation dataset is present;
7. the measured restore duration is retained;
8. the backup timestamp is compared with the accepted RPO;
9. the measured recovery duration is compared with the accepted RTO;
10. evidence contains no passwords, tokens or connection strings.

A backup is not considered proven recoverable until an isolated restore drill has succeeded.

## SQLite runbook

### Backup boundary

DepotManager already uses SQLite's online backup API to create verified migration-safety backups before schema-sensitive updates. Those files are application safety artifacts, not a complete production backup policy.

For production scheduling, use one of these controlled approaches:

- a backup product that uses SQLite-aware/VSS-consistent snapshots; or
- a planned application stop followed by a file-level backup after all Depot writers have exited; or
- an operator implementation using SQLite's online backup API.

Do not copy only the main `.db` file from an actively written WAL-mode database and assume the copy is consistent.

### Restore drill

1. select a retained SQLite backup;
2. create an isolated restore directory with sufficient free space and correct ACLs;
3. restore/copy the backup into that isolated target;
4. validate SHA-256 copy identity where the drill begins from a file backup;
5. run SQLite integrity validation;
6. validate Depot `DatabaseInfo` schema metadata;
7. run Depot health/application acceptance against the isolated database when the deployment procedure permits it;
8. retain drill evidence, then remove the temporary restored copy.

The repository regression uses `SqliteRecoveryDrillService`, which performs isolated copy, SHA-256 comparison, `PRAGMA integrity_check` and Depot schema-metadata validation.

## SQL Server runbook

Use SQL Server-native backup infrastructure. The exact account, path and storage policy are deployment-specific.

### Backup

A representative full backup command is:

```sql
BACKUP DATABASE [Depot]
TO DISK = N'<controlled-backup-path>\Depot-full.bak'
WITH COPY_ONLY, INIT, CHECKSUM;
```

Production schedules may use the deployment's approved full/differential/log strategy where supported. RPO claims must match the actual backup/log schedule rather than the example above.

Validate backup readability using the organization's SQL Server procedure, for example `RESTORE VERIFYONLY`, while recognizing that verification is not a substitute for a real restore drill.

### Restore drill

1. restore into an isolated SQL Server database/instance;
2. never overwrite production during a routine drill;
3. use `RESTORE DATABASE` with explicit file placement appropriate to the isolated target;
4. run SQL Server consistency checks required by the deployment;
5. verify the retained drill marker/data;
6. point a non-production Depot instance at the restored target and perform health/schema checks;
7. record start/end timestamps and evidence;
8. remove or quarantine the isolated target according to policy.

CI exercises SQL Server 2022 Express with native `BACKUP DATABASE` / `RESTORE DATABASE`, a retained recovery marker and post-restore Depot provider acceptance.

## MariaDB runbook

Use MariaDB-native operational tooling selected by the deployment. The generic certified recovery boundary uses a transactional logical dump.

Representative isolated-drill backup:

```text
mariadb-dump --single-transaction --routines --triggers --hex-blob <database> > depot.sql
```

Do not put passwords on the command line or in retained evidence. Use the deployment's protected credential mechanism.

Restore the dump into a newly created isolated database, verify the retained drill marker/data, then run Depot provider health/application acceptance against the restored database.

For large deployments that require physical backup tooling, replication, point-in-time recovery or tighter RPO/RTO, the deployment must qualify that infrastructure separately. Depot does not claim that logical dumps alone meet every production objective.

## MySQL runbook

Use MySQL-native operational tooling selected by the deployment. The generic certified recovery boundary uses a transactional logical dump.

Representative isolated-drill backup:

```text
mysqldump --single-transaction --routines --triggers --hex-blob <database> > depot.sql
```

Restore into a newly created isolated database, verify retained drill data, then run Depot provider health/application acceptance against the restored target.

GTID/binlog/PITR configuration is deployment infrastructure and must be documented separately when required by the accepted RPO.

## Retention, encryption and off-host copies

Every ACTIVE DR profile requires:

- an explicit retention period;
- at least one off-host copy;
- encryption at rest;
- backup-job monitoring.

These are minimum policy fields, not evidence that a specific storage system implements them correctly. The operator must retain configuration/audit evidence from the actual backup platform.

Backup encryption is incomplete without documented key recovery. A backup whose encryption key cannot be recovered must be treated as non-restorable.

## Restore-drill cadence

The DR profile contains `restoreDrill.maximumAgeDays`. A production owner must schedule drills often enough that the most recent successful evidence never exceeds that limit.

Drill evidence should retain at minimum:

- deployment identifier;
- provider and provider version;
- backup timestamp/identifier without credentials;
- drill start/end UTC;
- measured duration;
- accepted RPO/RTO at drill time;
- restore result;
- integrity result;
- Depot schema/application recognition result;
- operator identity or organizational owner;
- follow-up incident/problem reference for failures.

The repository's provider workflow retains generic JSON recovery evidence for SQLite, SQL Server, MariaDB and MySQL. That evidence proves the product/provider boundary; it does not replace deployment-specific drill evidence.

## Failure recovery guidance

### Access denied / ACL denied

Do not work around an ACL failure by granting broad permanent permissions.

1. capture the sanitized support package/evidence;
2. identify the exact service/operator identity performing backup or restore;
3. verify NTFS/share/database permissions on the isolated target and backup source;
4. correct the minimum required ACL/role;
5. rerun the drill;
6. record the permission change and outcome.

Depot recovery diagnostics classify this as `AccessDenied` and return guidance without echoing exception secrets.

### Disk full

1. stop the restore before retrying;
2. verify free space for both restored data and temporary/provider growth requirements;
3. extend or select a larger isolated target;
4. rerun from the original immutable backup;
5. do not delete the only valid backup to make room for the restore.

### File lock / active writer

For SQLite, stop conflicting Depot writers before file-level recovery operations. For server providers, follow provider-native session/restore procedures. Do not force-delete database files owned by a running engine.

### Credential failure

Credentials belong to the deployment's secret-management process. Do not place passwords in command arguments, support ZIPs, screenshots or recovery evidence. Rotate/repair credentials through the approved database process and repeat the drill.

### Database unavailable

Confirm service health, network/DNS/firewall reachability and provider authentication independently from Depot before attempting schema repair. Depot must not convert infrastructure unavailability into an automatic destructive restore.

## Recovery Support Package

DepotManager's support package contains:

- `Diagnostics.json`;
- `RecoveryReadiness.json`;
- `InstallationState.txt`;
- up to five sanitized logs;
- `CollectionWarnings.txt` when log collection itself is blocked by ACL/I/O failures.

Credential-bearing log lines are redacted. Recovery collection failures are represented as sanitized categories/action guidance rather than raw exception details.

The package intentionally does not contain database passwords, private keys, signing secrets or customer backup archives.

Attach the deployment's validated ACTIVE DR profile and latest restore-drill evidence separately when escalating a recovery incident.

## Production acceptance

Repository-level H4 implementation is complete when CI and the required provider gate prove:

- the DR profile contract validates;
- SQLite isolated restore validation is green;
- SQL Server native backup/restore is green;
- MariaDB native dump/restore is green;
- MySQL native dump/restore is green;
- restored databases are recognized by Depot;
- recovery evidence is retained without credentials;
- DepotManager recovery diagnostics remain usable when ancillary log collection is denied.

A specific production deployment remains `BLOCKED` for operational acceptance until it has an ACTIVE DR profile and a successful restore drill using its real backup infrastructure and accepted RPO/RTO.
