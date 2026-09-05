# Depot Manager

`DepotManager.exe` is Depot's Windows installation, update, repair, recovery, diagnostics, uninstall, and first-configuration application. `Depot.exe` remains the normal ERP runtime.

## Versioning

Depot Manager is versioned independently from Depot through `src/DepotManager/DepotManager.Version.props`. Manager-only changes increment only `DepotManagerVersion*`. Depot changes increment `DepotVersion*` in `Directory.Build.props`. `DatabaseVersion.CurrentVersion` changes only when the database schema actually changes.

The running manager version is shown permanently in the footer. Maintenance mode distinguishes the installed Depot version, latest published Depot version, installed manager version, latest published manager version, and database schema version.

## Installation model

The default per-user install location is `%LOCALAPPDATA%\Programs\Depot`. The installed directory contains at least:

```text
Depot.exe
DepotManager.exe
Backup\
```

The canonical manager copy in the Depot directory is used by Windows Installed Apps, Modify, and Uninstall. Application data remains separate. New SQLite installations default to `%LOCALAPPDATA%\Depot\Data\depot.db`; existing custom paths remain authoritative. Native Browse dialogs are available for the install directory and local database path.

## First installation

1. Discover the latest eligible stable GitHub Release and validate its exact `Depot-<version>.exe` asset.
2. Select `Lokal (sqlite3)`, `Remote (MySQL/MariaDB)`, or `Remote (SQL)`.
3. Configure the database and run `Validate`. Validation runs inside Depot Manager without opening the normal Depot UI.
4. Local SQLite installations create the initial administrator in the manager. Remote databases must already contain a valid Depot administrator.
5. `Continue` performs controlled provisioning and starts Depot only after the complete configuration succeeds.

Passwords are not written to manager logs or plaintext request files. Persistent settings remain protected by Depot's DPAPI `SettingsRepository`.

## Installation health states

Installation state is determined centrally rather than from a single file check. The manager distinguishes:

```text
NotInstalled
InstallationIncomplete
InstalledHealthy
RepairRecommended
InstallationDamaged
ProvisioningIncomplete
ConfigurationDamaged
DatabaseUnavailable
DatabaseMigrationRequired
RecoveryRequired
```

Inspection covers Depot and manager executables, protected settings, database connectivity, database schema, Windows uninstall registration, Start Menu integration, and the persisted Desktop shortcut preference. Database outages remain distinct from damaged application files.

## Published update information

Depot and manager updates are discovered only from published, non-draft, non-prerelease GitHub Releases with semantic Depot release tags. Source-tree versions, branches, pull requests, and `Directory.Build.props` are never installable versions.

Depot release metadata is carried by:

```text
Depot-<version>.manifest.json
```

The manifest declares the target database schema and manager command protocol. A migration-sensitive update fails closed when compatibility cannot be proven.

## Download and signature validation

Manager self-update assets use the exact name:

```text
DepotManager-<manager-version>.exe
```

Before a manager update can execute, Depot Manager validates HTTPS transport, published asset size, the Windows PE structure, the published file version, the GitHub SHA-256 digest when supplied, and the Windows Authenticode trust chain through `WinVerifyTrust`. An unsigned, tampered, expired/untrusted, or otherwise invalidly signed manager executable is rejected before it can become the update helper.

The release workflow signs tagged Depot and Depot Manager executables before release assets are staged. No certificate identity is invented or hard-coded in the client; Windows Authenticode trust is the signature authority while GitHub release metadata and SHA-256 provide independent artifact-integrity checks.

## Depot Manager self-update

A running manager is never overwritten in place. The new manager is staged beside the canonical executable as `DepotManager.update.exe`, so Windows can execute the helper normally. The helper waits for the original process to exit, preserves the prior manager as `DepotManager.previous.exe`, validates its own signed executable again, and atomically replaces the canonical manager.

The replacement is not considered successful merely because `Process.Start` succeeds. The canonical updated manager is launched with a scoped one-time readiness marker. Only after the WPF `MainWindow` has reached its Loaded state does the new manager acknowledge startup. The helper waits for that acknowledgement and a short post-start stability interval. If startup is not acknowledged, or the new manager exits immediately, the helper terminates the failed process, restores the previous manager executable, and attempts to relaunch it. Previous/helper artifacts are deleted only after a verified successful startup; delayed Windows deletion is used only as a cleanup fallback.

Self-update command-line paths are constrained to the expected canonical manager directory and generated marker naming convention so the maintenance bootstrap cannot be repurposed to delete arbitrary files.

## Cancellable operations and critical sections

Long-running manager work exposes a visible `Cancel` action in the footer while the installation/maintenance panels are busy. Cancellation propagates through the existing operation `CancellationTokenSource`; after cancellation or a normal failure the controls are re-enabled so the user can retry.

Database migration and the post-replacement compatibility check are intentionally a non-cancellable critical section. Immediately before executable replacement the manager checks for a pending cancellation request, then disables cancellation and completes replacement, migration, health validation, and recovery deterministically. This prevents user cancellation from leaving the application binary and database schema at different compatibility levels.

## Depot update and migration safety

Before updating Depot, the manager determines:

```text
Installed Depot version
Target Depot version
Current database schema
Target database schema
Migration required: yes/no
```

A target schema older than the current database is blocked.

For SQLite, a required migration first creates a consistent snapshot beneath `Backups\Database`. The snapshot uses SQLite's backup API rather than copying only the main database file, so WAL state is included consistently. The backup is integrity-checked and retained after migration or health-check failures.

For SQL Server and MySQL/MariaDB, Depot Manager does not assume server backup privileges. A schema-changing update requires explicit confirmation that a current server-side backup exists. Declining the confirmation aborts the update. Remote databases are never deleted, restored, or downgraded automatically.

Schema migration runs only through Depot's authoritative `DatabaseProvisioningService` and existing provider/migration pipeline. Depot Manager contains no duplicate schema implementation.

## Post-update health check and automatic binary recovery

Depot releases expose non-UI manager commands intercepted before WPF startup:

```text
--manager-migrate
--manager-health-check
```

An update is not reported as successful until the health command validates protected settings, database connectivity, exact schema compatibility, and completed administrator bootstrap.

If a failure occurs after the new Depot executable has been deployed, Depot Manager inspects the database again before attempting executable recovery. The previous executable is restored automatically only when the current database schema is known and exactly matches the schema recorded for that previous executable. If migration already advanced the schema, database connectivity is unavailable, or compatibility cannot be proven, the new binary is left in place and recovery is reported instead of risking an incompatible binary downgrade. Database schema downgrade is never automatic.

## Repair

Repair determines the intended installed version from a valid executable or Windows registration, downloads the exact stable release where possible, restores `Depot.exe`, ensures the canonical `DepotManager.exe` exists, repairs Windows Installed Apps registration, recreates the Start Menu shortcut, and recreates the Desktop shortcut when its persisted preference is enabled.

Repair does not reset `depot.settings`, user accounts, or business data. After binary/Windows integration repair the installation is inspected again; unreadable settings, unavailable databases, incompatible schemas, and incomplete provisioning remain visible as separate health states.

## Rollback

Before an update replaces Depot, the previous executable is kept under `Backup\Depot-<version>.exe` with rollback metadata recording the schema it supported. Manual rollback is offered only when the executable is valid, its version matches the metadata, and the current database schema exactly matches the recorded schema. No database downgrade is performed.

## Diagnostics and support

Maintenance mode provides:

```text
Open log folder
Copy diagnostics
Create support package
```

Diagnostics include manager/Depot versions, available release versions, install directory, OS/process architecture, database provider, non-secret database target, current and target database schema, last successful backup timestamp when readable, release publication metadata, rollback availability, and installation health. Support packages contain `Diagnostics.json`, `InstallationState.txt`, and a limited set of sanitized logs.

Log export redacts lines containing password, token, authorization, connection-string, username, e-mail, API-key, cookie, bearer, access-key, private-key, SAS, or similar credential markers. The support package never intentionally contains decrypted DPAPI secrets, administrator/database passwords, session tokens, or complete secret-bearing connection strings.

## Uninstall

Uninstall offers cancel, application-only removal, or application plus all local Depot data. Full local removal deletes `depot.settings`, `%LOCALAPPDATA%\Depot`, the configured local SQLite database even when stored elsewhere, and its `-wal`, `-shm`, and `-journal` sidecars. Remote SQL Server and MySQL/MariaDB databases are never deleted.

## UI consistency

Depot Manager uses the Depot icon, dark title bar, shared colors, typography, spacing, button resources, and the actual Depot `Inputs.xaml` dictionary. Text/password controls therefore share Depot templates and interaction behavior. Status and operation states use the existing semantic brushes and button styles.

## Release assets

Release-integrity CI produces:

```text
Depot-<depot-version>.exe
Depot-<depot-version>.manifest.json
DepotManager.exe
DepotManager-<manager-version>.exe
```

The manifest records Depot version, independently versioned manager version, database schema version, and manager command protocol. Tagged release executables remain Authenticode-signed and SHA-256 integrity evidence is generated by CI.

## Schema authority

This hardening work introduces no database schema change. `DatabaseVersion.CurrentVersion` remains the core schema authority, and Depot's existing initializers/migrations remain the only code allowed to advance it.
