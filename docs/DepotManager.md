# Depot Manager

`DepotManager.exe` is Depot's Windows installation, update, repair, uninstall, and first-configuration application. `Depot.exe` remains the normal ERP runtime.

## Versioning

Depot Manager is versioned independently from the Depot application. Its version is defined in `src/DepotManager/DepotManager.Version.props` through `DepotManagerVersionMajor`, `DepotManagerVersionMinor`, and `DepotManagerVersionPatch` and is applied to the manager's package, assembly, and file metadata. The initial independent manager version is `0.1.0-preview`.

A Depot application version change therefore does not imply a Depot Manager version change, and a Depot Manager release can advance independently from `DepotVersionMajor`, `DepotVersionMinor`, and `DepotVersionPatch`. Release-integrity CI validates that the produced `DepotManager.exe` file version matches the manager version file and stages a versioned `DepotManager-<manager-version>.exe` asset in addition to the stable `DepotManager.exe` distribution name.

## Installation model

The default per-user installation location is `%LOCALAPPDATA%\Programs\Depot`. Application data is kept separate: new SQLite installations default to `%LOCALAPPDATA%\Depot\Data\depot.db`. Existing installations may continue to use their current database and location; the manager does not move databases or business data.

The installed application directory contains `Depot.exe`, `DepotManager.exe`, and `Backup\`. Depot's existing `depot.settings` configuration remains authoritative so existing installations are not silently migrated to a second configuration mechanism.

## First installation

1. Install the latest valid GitHub release.
2. Select SQLite, Microsoft SQL Server, or MySQL/MariaDB.
3. Test the connection before provisioning. The manager binds the successful test to the exact current provider/host/database/credential selection and requires another test if those values change.
4. Provision the database through the same `DatabaseProvisioningService` used by normal Depot startup. It invokes Depot's existing provider initializer and migration classes; the manager contains no schema or migration copy.
5. Create the initial administrator through `AdministratorBootstrapService`, which applies the existing password policy, RBAC Administrator role, legacy-admin retirement, transaction handling, and audit behavior.
6. Start Depot and sign in through the normal login window.

For SQLite the manager creates the selected parent directory if necessary and verifies that it is writable before testing the database connection. SQL Server and MySQL/MariaDB fields follow the authentication and TLS capabilities currently supported by Depot. SQL Server Windows Authentication is not exposed because the current Depot settings model supports SQL credentials rather than integrated authentication. No provider-specific schema copy exists in Depot Manager.

Database and administrator credentials are sent to the short-lived provisioning subprocess over redirected standard input. They are never written to the manager log or a plaintext request file. Persistent database settings are saved only by Depot's existing `SettingsRepository`, which protects the settings payload with Windows DPAPI for the current user.

## Updates and repair

Depot Manager reads published GitHub releases, validates semantic release tags and the exact `Depot-<version>.exe` asset name, checks HTTP success and file length, validates the PE/managed executable structure, and verifies the GitHub-provided SHA-256 digest when present. The downloaded executable version is checked before any installed executable is replaced.

Update compares the installed executable's product/file version with the latest valid stable release. Repair deliberately resolves the exact currently installed release and obtains that same version again; repair therefore cannot silently turn into an update.

Before update or repair Depot must be closed normally. The manager does not kill the application by default. The current executable is copied to `Backup\Depot-<previous-version>.exe`; file-version revision metadata such as `.0` is not added to the release-style backup name. Older executable backups are removed so only one backup is retained. Business data, database content, audit history, and user preferences are not included in executable backup or rollback behavior.

Repair replaces the application binary without resetting configuration or data. The backup executable is a recovery artifact only; schema downgrade and automatic database rollback are deliberately unsupported.

## Uninstall

Normal uninstall removes application binaries, the executable backup, Start menu integration, and the per-user Installed Apps registration. It does not delete the database, remote SQL Server/MySQL/MariaDB data, audit history, or Depot configuration. Data removal is intentionally not part of the normal uninstall path.

## Windows integration

Depot Manager registers Depot under the current user's Windows `Installed Apps`/Uninstall registry key with the installed version, install location, icon, and manager as the modify/uninstall entry point. A Start menu shortcut launches `Depot.exe`. Multiple concurrent manager instances are blocked by a named Windows mutex.

## Logging

Manager operational logs are written beneath `%LOCALAPPDATA%\Depot\Logs\DepotManager.log`. Logs contain the manager version, source/target Depot versions, selected provider, non-sensitive database target information, download/validation status, deployment/provisioning results, and actionable failures. Passwords, usernames, and complete connection strings are never logged.

## Release assets

Depot retains the existing single-file release convention:

```text
Tag:   <version>
Asset: Depot-<version>.exe
```

Depot Manager is built as a self-contained, untrimmed `win-x64` single-file executable. Release-integrity CI emits both `DepotManager.exe` as the stable distribution filename and `DepotManager-<manager-version>.exe` as the version-explicit artifact. The manager version is not derived from the Depot release tag.

## Schema version

This implementation introduces no database schema changes. `DatabaseVersion.CurrentVersion` therefore remains unchanged. Depot's existing database initializers and migrations remain the only schema authority.
