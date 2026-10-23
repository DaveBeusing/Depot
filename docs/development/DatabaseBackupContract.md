# Complete logical database backup contract

## Supported recovery boundary

`.depotbackup` format **2**, schema-contract version **1**, is a complete logical snapshot of a provisioned Depot application database. Restore requires the same provider, current core version, exact current feature-version vector, and matching table/column contract. It does not create or migrate a database schema and is not a cross-provider migration format.

Format 1 archives contain only an older subset of tables and columns. They are rejected before a safety backup, transaction replacement, or deletion. They must not be relabelled as format 2: missing financial, sales, attachment, session, and other feature records cannot be reconstructed from those archives. Create a new backup with the current application; use native database recovery or a separately reviewed extraction process for historical partial archives.

The contract covers every user table in the active database, including core and feature metadata, finance, sales, CRM, subscriptions, production, service management, project accounting, item master fields and traceability, sessions, preferences, and attachment revisions and binary contents. The minimum required logical table/column inventory is derived once per process from a temporary reference database initialized by the shipped provisioning code. This independent inventory prevents a missing feature table from being accepted merely because its version metadata remains present. Provider-specific generated columns and legacy extra columns remain part of the actual source/target contract.

SQLite internal tables are excluded from row export; `sqlite_sequence` high-water marks are represented explicitly in the manifest. SQL Server application tables must be in `dbo`; MySQL/MariaDB tables must use InnoDB. Custom MySQL/MariaDB triggers, cross-database foreign keys, unsupported SQL identifiers, and disabled or untrusted SQL Server foreign-key/check constraints are rejected. Native backups are required for unsupported custom schemas.

## Archive contents and validation

The ZIP archive contains `manifest.json` and exactly one `tables/<trusted-table-name>.json` entry for every application table. Missing, duplicate, and unexpected entries are rejected. The manifest records:

- Archive and schema-contract versions, creation time in UTC, and provider.
- Core version, every feature version, and a SHA-256 fingerprint of the ordered table/column contract.
- Column names, provider store types, nullability, generated/identity flags, and primary-key ordering.
- Per-table row counts, SHA-256 checksums, and maximum stored identity values.
- Provider identity high-water marks, including identifiers allocated to subsequently deleted rows.

Rows are arrays of stored columns in the manifest's canonical order. Each non-null cell contains an explicit type and invariant text representation. Binary cells are base64-decoded back to `byte[]`; strings and binary contents cannot be interchanged. Decimal values preserve decimal precision; floating-point values use a round-trippable representation. Date/time values retain their original kind/offset and precision without a local-time-to-UTC conversion. Generated columns are described in the contract and omitted from inserts so the provider recomputes them.

Validation streams all rows, verifies checksums, counts, row shapes, supported typed values, and core/feature metadata rows. It compares archive schema entries to the trusted live catalog. Archive-controlled names or SQL are never used to construct commands. The same opened file and ZIP archive remain in use until restore finishes, so changing the selected path cannot substitute a different archive after validation. Checksums detect corruption; they are not signatures or proof of who created an archive.

## Restore transaction

Restore follows this sequence:

1. Authorize `Database.Manage`, inspect the current complete schema, and validate the complete selected archive without deleting data.
2. Start the provider's write transaction, reread the live contract, and check compatibility again.
3. Write and validate a uniquely named safety backup from that same locked transaction snapshot. Failure to create it stops recovery before replacement.
4. Suspend only the provider mechanisms that prevent complete cyclic data replacement. Delete and reinsert all application rows using parameterized values and trusted catalog identifiers. Explicit SQL Server identity insertion is enabled only for tables that actually have an identity column.
5. Restore identity high-water marks where supported; reinstate constraints and triggers; validate every foreign-key relationship and compare every restored table's row count and typed-value checksum to the archive.
6. Commit only when every check passes. A failure or cancellation rolls back table contents and transactional schema operations. Connection-local foreign-key settings are restored in a `finally` block.

SQLite disables connection-local foreign-key enforcement before starting the transaction, then checks `PRAGMA foreign_key_check` before commit. This supports cyclic relationships and avoids delete cascades rewriting archived relationships. SQLite `BEFORE` validation triggers remain active; `AFTER` triggers are dropped and recreated transactionally from the trusted live schema so CRM link maintenance cannot modify imported records.

SQL Server temporarily suspends constraints transactionally and re-enables them with `WITH CHECK CHECK CONSTRAINT ALL`. Previously enabled table triggers are suspended and resumed in that transaction. MySQL/MariaDB uses connection-local `FOREIGN_KEY_CHECKS=0` and explicit orphan queries over every catalogued foreign key before commit, because re-enabling that setting alone does not validate inserted rows. No MySQL/MariaDB `ALTER TABLE`, `TRUNCATE`, or other implicit-commit DDL is issued during restore.

## Identity allocation policy

Existing record identifiers are restored exactly. Allocators are never intentionally lowered below either the target's existing high-water mark or the source backup's high-water mark. Gaps are allowed; server identity allocation is not transactional, so a failed restore can consume unused identifiers even when all rows roll back.

- **SQLite:** restore raises `sqlite_sequence` to at least the archived high-water mark. A fresh target therefore does not reuse identifiers of deleted source rows.
- **SQL Server:** explicit inserts advance identities for restored rows. If required, `DBCC CHECKIDENT` raises the allocator beyond the archived high-water mark. A never-populated/truncated empty table is handled separately because its next insert uses the reseed value itself.
- **MySQL/MariaDB:** explicit inserts advance `AUTO_INCREMENT` for restored rows. Restore is rejected before deletion if both the target allocator and the highest archived live identity are below an archived high-water mark. This affects fresh targets when the source had deleted its highest allocated identities. Prepare those target counters using native administration before retrying, or use native database recovery. Depot does not use an implicit-commit `ALTER TABLE` inside an otherwise atomic restore.

## Operational use

Create and test backups after schema upgrades. Stop other application instances for planned recovery and allow enough disk space for the source archive, safety archive, and transactional database/log growth. Automatic backups use the same complete archive contract through the internal configured scheduler entry point. Backup files include authentication hashes and session records and need access-controlled storage. Filesystem configuration and external services are outside this database snapshot.

Archive validation establishes structural/schema compatibility. Provider constraints and actual restored values are also checked in the replacement transaction; relational violations or values the provider cannot reproduce cause rollback. Sign out/restart clients after a successful restore so cached authentication and business state are refreshed. Reapply any privacy restrictions or external-system reconciliation required by restoring an older business state.

## Regression coverage

`DatabaseManagementTests` exercises the full SQLite schema and compares every table hash after roundtrip. Representative nonempty records include item fields, sales invoices and lines, finance journal entries and amounts, projects, sessions, preferences, attachment metadata/revisions, and binary contents. Additional cases cover cyclic/self-referential foreign keys, generated columns, deleted identity high-water marks on a fresh target, metadata completeness, malformed typed values, invalid names, duplicate/missing entries, legacy archives, foreign-key rollback, and cancellation after deletion.

`DatabaseBackupProviderAcceptanceTests` exercises real SQL Server, MySQL, and MariaDB configurations in the provider acceptance collection. It preserves the pre-test database through a baseline snapshot, checks all table signatures, binary/decimal/time values, CRM links, generated reservation columns, post-backup row removal, rollback after deletion, and foreign-key enforcement after recovery. Provider tests require their configured services; a skipped provider case is not execution evidence.

The application and test project target Windows/.NET 10. The build, Windows test suite, and configured provider runs remain the execution gate for this implementation; static inspection alone is not a substitute for those runs.
