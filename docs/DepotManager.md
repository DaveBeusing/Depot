# Depot Manager

`DepotManager.exe` is Depot's Windows installation, update, repair, recovery, diagnostics, uninstall, and first-configuration application. `Depot.exe` remains the normal ERP runtime.

## Versioning

Depot Manager is versioned independently from Depot through `src/DepotManager/DepotManager.Version.props`. Manager-only changes increment only `DepotManagerVersion*`. Depot changes increment `DepotVersion*` in `Directory.Build.props`. `DatabaseVersion.CurrentVersion` changes only when the database schema actually changes.

The running manager version is shown permanently in the manager footer. Maintenance mode distinguishes the installed Depot version, latest published Depot version, installed manager version, latest published manager version, and database schema version.

## Installation model

The default per-user install location is `%LOCALAPPDATA%\Programs\Depot`. The installed directory contains at least:

```text
Depot.exe
DepotManager.exe
Backup\
```

`DepotManager.exe` is copied into the Depot directory during installation and that copy is the canonical manager used by Windows Installed Apps, Modify, and Uninstall. The original download location is not required after installation.

Application data remains separate. New SQLite installations default to `%LOCALAPPDATA%\Depot\Data\depot.db`. Existing custom paths remain authoritative. Install and SQLite paths support native Browse dialogs while manual path entry remains available. A desktop shortcut can be requested during installation; the Start Menu shortcut is always maintained.

## First installation

1. Discover the latest eligible stable GitHub Release and validate its exact `Depot-<version>.exe` asset.
2. Select `Lokal (sqlite3)`, `Remote (MySQL/MariaDB)`, or `Remote (SQL)` from the database cards.
3. Configure the database and run `Validate`. Validation runs entirely inside Depot Manager and does not launch the normal Depot UI.
4. Local SQLite installations create the initial administrator in the manager. Remote databases must already contain a valid Depot administrator.
5. `Continue` performs controlled provisioning, closes Depot Manager, and starts Depot only after provisioning succeeds.

Passwords are never written to manager logs or plaintext request files. Persistent settings remain protected by Depot's existing DPAPI `SettingsRepository`.

## Installation health states

Installation state is determined centrally rather than from a single `File.Exists` check. The manager can distinguish:

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

The inspection covers the Depot executable, canonical manager executable, protected settings, database connectivity, database schema, Windows uninstall registration, Start Menu shortcut, and persisted Desktop shortcut preference. A database outage is reported separately from damaged application files.

## Published update information

Depot and manager updates are discovered only from published, non-draft, non-prerelease GitHub Releases. Source-tree versions, branches, pull requests, and `Directory.Build.props` are never treated as installable versions.

Maintenance mode displays installed and available Depot/manager versions. Depot release metadata includes the target database schema and manager command protocol in:

```text
Depot-<version>.manifest.json
```

The metadata is produced by release-integrity CI and is required for a migration-sensitive Depot update. If migration compatibility cannot be proven, the update fails closed.

## Depot Manager self-update

Published releases include a versioned manager asset:

```text
DepotManager-<manager-version>.exe
```

The manager scans stable releases for the highest published manager version, validates the download length, Windows PE structure, file version, and GitHub SHA-256 digest when present, then stages the update beside the canonical manager.

A running manager is never overwritten in place. The staged new manager is launched in an update-helper mode, waits for the original process to exit, preserves the previous canonical executable, replaces it atomically, and relaunches the canonical manager. Cleanup removes staged/previous files, with Windows delayed deletion as a fallback. A failed replacement restores the prior manager when possible.

## Depot update and migration safety

Before updating Depot, the manager determines:

```text
Installed Depot version
Target Depot version
Current database schema
Target database schema
Migration required: yes/no
```

If the target schema is older than the current database, the update is blocked.

For SQLite, a required migration first creates a consistent snapshot beneath `Backups\Database`. The snapshot uses SQLite's backup API rather than copying only the main database file, so WAL state is included consistently. The backup is integrity-checked and retained if migration or health validation later fails.

For SQL Server and MySQL/MariaDB, Depot Manager does not assume server backup privileges. A schema-changing update requires explicit confirmation that a current server-side backup exists. Declining that confirmation aborts the update. Depot Manager does not automatically delete, restore, or downgrade remote databases.

Schema migration itself is performed only through Depot's authoritative `DatabaseProvisioningService` and existing provider/migration pipeline. Depot Manager contains no second schema implementation.

## Post-update health check

New Depot releases expose non-UI manager commands that are intercepted before WPF startup:

```text
--manager-migrate
--manager-health-check
```

The migration command loads protected settings, validates connectivity, runs the existing Depot migration pipeline, and verifies the resulting schema version. The health command validates settings, database connectivity, exact schema compatibility, and completion of administrator bootstrap without opening the normal Depot UI.

An update is not reported as successful until the post-update health check passes. Failure preserves safety/rollback artifacts and reports recovery as required.

## Repair

Repair is an installation recovery operation rather than merely an update alias. It determines the installed version from a valid executable or Windows registration, downloads the exact stable release where possible, restores `Depot.exe`, ensures the canonical `DepotManager.exe` exists, repairs Windows Installed Apps registration, recreates the Start Menu shortcut, and recreates the Desktop shortcut when that persisted preference is enabled.

Repair never silently resets `depot.settings`, user accounts, or business data. After binary/Windows integration repair, the installation is inspected again. Unreadable protected settings, unavailable databases, incompatible schemas, or incomplete provisioning remain visible as separate recovery states rather than being masked by a successful file copy.

## Rollback

Before an update replaces an installed Depot executable, the prior `Depot.exe` remains under `Backup\Depot-<version>.exe`. The manager writes rollback metadata alongside it containing the schema version supported at the time the backup was created.

Rollback is offered only when the backup is a valid Windows executable, its file version and metadata agree, and the current database schema exactly matches the schema recorded for that previous Depot version. No automatic database schema downgrade is performed. If schema compatibility cannot be proven, rollback is blocked.

## Diagnostics and support

Maintenance mode provides:

```text
Open log folder
Copy diagnostics
Create support package
```

Diagnostics include manager/Depot versions, install directory, OS/process architecture, database provider, non-secret database target, database schema, and installation health. Support packages contain `Diagnostics.json`, `InstallationState.txt`, and a limited set of sanitized logs.

Lines containing password, token, authorization, connection-string, username, or similar credential markers are redacted before export. The support package must never contain database passwords, administrator passwords, decrypted DPAPI secrets, session tokens, or complete secret-bearing connection strings.

## Update, repair and rollback safety

Depot must be closed before executable replacement. The manager does not kill Depot by default. Downloads are staged and validated before installed executables are replaced. Executable backups contain no business data. Temporary files are cleaned after success or failure where possible.

A failed migration or health check is never presented as a successful update. Remote databases are never deleted. Automatic schema downgrade is unsupported.

## Uninstall

Uninstall offers cancel, application-only removal, or application plus all local Depot data. Full local removal deletes `depot.settings`, `%LOCALAPPDATA%\Depot`, the configured local SQLite database even when stored elsewhere, and its `-wal`, `-shm`, and `-journal` sidecars. Remote SQL Server and MySQL/MariaDB databases are never deleted.

## UI consistency

Depot Manager uses the Depot icon, dark title bar, shared colors, typography, spacing, button resources, and the actual Depot `Inputs.xaml` dictionary. Text/password controls therefore share the same templates and interaction behavior as Depot. Status colors use the existing success, warning, error, and primary semantic brushes.

## Release assets

Release-integrity CI produces the single-file Windows artifacts and migration metadata:

```text
Depot-<depot-version>.exe
Depot-<depot-version>.manifest.json
DepotManager.exe
DepotManager-<manager-version>.exe
```

The manifest records the Depot version, independently versioned manager version, database schema version, and manager command protocol. Tagged releases continue to use the existing signing workflow and SHA-256 integrity evidence.

## Schema authority

This hardening work introduces no database schema change. `DatabaseVersion.CurrentVersion` remains the only core schema version authority, and Depot's existing initializers/migrations remain the only code allowed to advance that schema.
