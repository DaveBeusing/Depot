# Backup and Restore

## Summary
Database maintenance provides provider information, schema details, backup validation, backup creation, restore, integrity checks, and SQLite compaction where supported.

## Prerequisites
- You have database management permission.
- The destination path is writable and has sufficient space.
- Restore operations have an exclusive and validated backup source.

## Steps
1. Open **Administration > Database**.
2. Review the active provider, target, schema version, size, and last successful backup.
3. Choose **Create Backup** or configure the persistent automatic schedule.
4. Use **Check Backup** before any restore.
5. Confirm destructive maintenance actions explicitly.

## Result
Successful backups are recorded with time, size, and path. Restore validation occurs before replacement, and Depot may create a safety backup first.

## Common problems
> [!WARNING] Never interrupt restore or compaction. Ensure no other Depot process is using a local SQLite file.

- SQLite supports file backup, integrity check, restore, and VACUUM.
- Provider-specific capabilities shown by the page determine which server maintenance actions are available.

## Required permissions
`Database.View` and `Database.Manage`.

## Related topics
- [Database Configuration](topic:administration.database)
- [Database Connection Failures](topic:troubleshooting.database-connection-failures)
