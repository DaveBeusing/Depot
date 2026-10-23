# Backup and Restore

## Summary
Database maintenance provides provider information, schema details, backup validation, backup creation, restore, integrity checks, automatic backup retention, and SQLite compaction where supported.

## Steps
1. Open **Administration > Database**.
2. Review the active provider, target, schema version, size, and last successful backup.
3. Choose **Create Backup** or configure the persistent automatic schedule.
4. Use **Check Backup** before any restore.
5. Confirm destructive maintenance actions explicitly.

## Retention and protection
Automatic backup retention preserves at least the ten newest automatic backups and only removes additional automatic backups after they are older than the configured technical retention threshold (currently 30 days).

Backup archives can contain business records, personal data, and authentication hashes. Store them on access-controlled and appropriately encrypted storage. Depot does not treat archive-level encryption as complete until a managed key-store design exists.

## Restore integrity
Restore validation happens before replacement and Depot can create a safety backup first. A valid recovery must preserve record IDs, permanent document numbers, relationships, status, audit history, and schema version as a consistent unit.

> [!WARNING] Restoring an older backup can reintroduce personal data or older business state. Reapply any required privacy restriction/anonymization actions and verify current operational state after recovery.

## Common problems
- Do not interrupt restore or SQLite compaction.
- Ensure no other Depot process is using a local SQLite file during exclusive maintenance.
- Clean/corrupt/interrupted/unavailable-target paths have automated baseline tests, but production SQL Server/MySQL/MariaDB recovery and real Windows ACL-denied scenarios must be validated in the actual environment.

## Required permissions

`Database.View` allows the overview, backup list, backup validation, and integrity checks. `Database.Manage` is required for manual backup creation, restore, SQLite compaction, and changes to the automatic-backup schedule. A view-only user can inspect maintenance information while mutation commands and configuration fields remain disabled.

An enabled automatic-backup schedule runs using its saved configuration even before login or while signed out. This does not grant manual maintenance access. Sign out and sign in again after role or permission changes to use the updated permissions.

## Related topics
- [Database Configuration](topic:administration.database)
- [Privacy Data](topic:administration.privacy-data)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)

