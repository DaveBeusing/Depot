# Database schema compatibility

Depot Manager protocol **2** uses `DatabaseSchemaCompatibility` contract **1** for release requirements, read-only database inspection, protected migrations, health checks and rollback. A Core schema integer alone is not compatibility evidence.

## Authoritative requirements

`DatabaseSchemaRegistry.CurrentFeatureVersions` registers all fourteen feature migrations. Backup validation, provisioning metadata recovery and the startup fast path use that registry. `DatabaseSchemaContract.Current` combines it with the Core version, the complete required table/column inventory generated from the independent provisioned reference schema used by `DatabaseBackupSchema`, and the required unversioned item master/traceability indexes. Index requirements include ordered columns, uniqueness, enabled state and the GTIN non-null predicate; MySQL/MariaDB use their native nullable unique-index semantics.

`Depot.exe --manager-schema-contract <response-path>` exports these requirements without opening or migrating the configured database. Release-integrity CI runs this command on the actual published executable and places its output in the manifest's `schemaCompatibility` field. Manifest format 4 retains `databaseSchemaVersion` and `schemaVersions` for diagnostic consumers; neither is sufficient to authorize an update. New feature registrations must be included in the registry. Structural changes must update the provisioned schema and any required index contract, with coverage proving the generated requirements include them.

## Read-only observations

`DatabaseCompatibilityReader` is shared source compiled into Depot and Depot Manager. SQLite is opened read-only by Manager. SQL Server and MySQL/MariaDB use provider metadata queries; inspection never invokes initialization or feature migrations.

An observation contains exactly one Core version, all recorded feature versions, table/column inventory, column definitions (including types, nullability, defaults and generated expressions), index definitions and ordered keys, uniqueness/filter/enabled information, and foreign-key/check/trigger/view definitions where applicable. SQLite table SQL retains its inline constraints. Providers retain their own structural representations; observations are compared only within the same provider. Sequence values and row contents are intentionally excluded because ordinary business activity does not change schema compatibility.

Malformed or unsupported contract versions, ambiguous/duplicate metadata and unreadable object definitions fail closed. An older or missing required feature/table/column/index requires the protected migration path. Unknown or newer feature versions and newer Core versions block automatic updates. Health validation requires exact feature versions and all declared structural requirements; additional customer tables are retained in observations and therefore participate in rollback comparisons.

## Protected update sequence

1. Require a protocol-2 target manifest and validate its full contract.
2. Stop Depot and run the installed executable's existing health command. This establishes the operational baseline for the upgrade source.
3. Read the full live database state twice and reject changing observations.
4. Compare Core, every feature and structural requirements. A same-Core feature or structural change is a migration.
5. For SQLite, create an integrity-checked backup and confirm that its full compatibility observation matches the inspected database. For SQL Server and MySQL/MariaDB, require explicit confirmation of a current verified server backup and stopped clients.
6. After download/confirmation, re-read the database before executable replacement. Any schema change since backup invalidates the operation and requires fresh backup evidence.
7. Preserve the previous executable and SHA-bound rollback observation, replace the executable, and run `--manager-migrate` whenever any requirement changed.
8. Run `--manager-health-check`, re-read compatibility and validate the complete target contract before reporting success.

All Depot clients must remain stopped during maintenance, including remote clients on other machines. Manager checks local processes and rejects inconsistent repeated observations, but does not provide a distributed lease preventing another administrator from performing DDL. Database operators must hold the deployment maintenance window through migration/rollback. Cancellation is disabled only after the operation enters its existing non-cancellable replacement/migration boundary.

## Rollback

Rollback metadata format 2 associates the previous executable version and SHA-256 with the complete observed pre-update schema. Metadata must include the provider and structural definitions. A fresh live read occurs inside the executable-restoration operation, after any confirmation dialog; cached maintenance snapshots cannot authorize replacement. The executable hash is rechecked immediately before restoration.

Rollback is allowed only when the full recorded observation is unchanged, including Core, feature versions, tables/columns and their definitions, indexes, constraints, triggers and views. A changed Sales version at the same Core version blocks both automatic and manual rollback. A changed unique index or column definition also blocks it. Database downgrade is never automatic.

## Earlier releases and local health evidence

Earlier Depot releases supporting the existing health command remain upgrade **sources**. Their successful pre-update health result and newly captured complete database observation form the baseline; this does not infer that a legacy executable supports later feature schemas. Rollback to that executable is permitted only while the recorded baseline is unchanged.

Core-only target manifests and old rollback metadata are explicitly unsupported. Select a protocol-2 release for automated updates; recover an old backup using a verified executable/database pair rather than inferring compatibility from Core equality. Future protocol versions require a Manager update.

After a protocol-2 provisioning or health check, `depot.schema.json` retains the executable's declared requirements and SHA-256. Manager uses this evidence for maintenance health reporting. Missing or stale evidence produces `CompatibilityUnknown`; it does not show a healthy state from Core equality. Repair revalidates the exact installed release. Repair cannot replace an unidentifiable executable with the latest release outside protected migration. After rollback, stale requirements are discarded and the restored executable is health-checked; protocol-2 responses re-establish executable-bound requirements. Legacy installations can be upgraded through the protected path while their full health evidence is unavailable.

No database schema number or product version changes are required for this protocol and inspection correction.

## Regression coverage

- SQLite plus independent SQL Server, MariaDB and MySQL acceptance cases exercise same-Core older, missing and newer feature metadata against the actual provider reader.
- Contract coverage checks all fourteen registered features, generated table/column requirements and unversioned indexes.
- Manager tests reject unknown features, missing compatibility contracts, structural definition changes, nonunique/disabled indexes and stale rollback observations.
- Packaged executable tests prove that health rejects a same-Core feature mismatch, backup precedes migration, successful migration restores health, and the old rollback state becomes incompatible.
