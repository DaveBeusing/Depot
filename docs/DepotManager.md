# Depot Manager

`DepotManager.exe` is Depot's Windows installation, update, repair, uninstall, and first-configuration application. `Depot.exe` remains the normal ERP runtime.

## Versioning

Depot Manager is versioned independently from the Depot application. Its version is defined in `src/DepotManager/DepotManager.Version.props` through `DepotManagerVersionMajor`, `DepotManagerVersionMinor`, and `DepotManagerVersionPatch` and is applied to the manager's package, assembly, and file metadata.

A change that affects only Depot Manager increments only `DepotManagerVersion*`. It must not increment `DepotVersionMajor`, `DepotVersionMinor`, or `DepotVersionPatch` in `Directory.Build.props`. The Depot application version changes only when the Depot application itself changes. Database schema version changes remain tied exclusively to actual schema changes.

Release-integrity CI validates that the produced `DepotManager.exe` file version matches the manager version file and stages a versioned `DepotManager-<manager-version>.exe` asset in addition to the stable `DepotManager.exe` distribution name.

## Installable Depot releases

Depot Manager never treats the current source-tree version of Depot as an installable release. `Directory.Build.props`, the current branch, commits, pull requests, and other in-development versions are not installation sources.

Installable Depot versions are discovered exclusively from published GitHub Releases in `DaveBeusing/Depot`. A release is eligible only when it is not a draft, is not marked as a prerelease, has a valid semantic release tag, and contains the exact `Depot-<version>.exe` asset. The asset is then validated before deployment. If a version exists only in source control and has not been published as such a GitHub Release, Depot Manager must not offer or install it.

## Installation model

The default per-user installation location is `%LOCALAPPDATA%\Programs\Depot`. Application data is kept separate: new SQLite installations default to `%LOCALAPPDATA%\Depot\Data\depot.db`. Existing installations may continue to use their current database and location; the manager does not move databases or business data.

The installed application directory contains `Depot.exe`, `DepotManager.exe`, and `Backup\`. Depot's existing `depot.settings` configuration remains authoritative so existing installations are not silently migrated to a second configuration mechanism.

## First installation

1. Discover the latest eligible published GitHub Release, choose the installation location, optionally select `Create desktop shortcut`, and install the exact `Depot-<version>.exe` asset. The Start menu shortcut is always created; the desktop shortcut is created only when selected during the first installation.
2. Choose one database type from the three manager cards: `Lokal (sqlite3)`, `Remote (MySQL/MariaDB)`, or `Remote (SQL)`. Selecting a card immediately advances to connection configuration.
3. Enter the connection details and run `Validate`. Connection validation is executed completely inside `DepotManager.exe`; Depot is not launched for this step. The manager opens the selected SQLite, MySQL/MariaDB, or SQL Server connection itself, executes a lightweight `SELECT 1` validation, and always shows an inline success or failure result. Changing any validated connection value invalidates that result and requires another validation.
4. For `Lokal (sqlite3)`, enter the initial administrator details in Depot Manager. Remote databases skip this step and must already contain a valid Depot administrator.
5. Review the resulting configuration. Only the final `Continue` action provisions/saves the database configuration, closes Depot Manager, and starts Depot. If provisioning or administrator validation fails, Depot is not started and Depot Manager remains open.

For SQLite the manager creates the selected parent directory if necessary and verifies that it is writable before validating the database connection. SQL Server and MySQL/MariaDB fields follow the authentication and TLS capabilities currently supported by Depot. SQL Server Windows Authentication is not exposed because the current Depot settings model supports SQL credentials rather than integrated authentication. No provider-specific schema copy exists in Depot Manager.

Connection validation credentials remain in the Depot Manager process and are not sent to `Depot.exe`. Only the final provisioning operation uses Depot's controlled manager command path; database and administrator credentials for that operation are sent over redirected standard input. They are never written to the manager log or a plaintext request file. Persistent database settings are saved only by Depot's existing `SettingsRepository`, which protects the settings payload with Windows DPAPI for the current user.

## UI consistency

Depot Manager uses the same Depot application icon, dark Windows title-bar handling, shared color/spacing/button resources, and the actual Depot `Inputs.xaml` resource dictionary. The manager compiles the same shared `TextInput`, `PasswordInput`, and `SearchBox` control sources needed by that dictionary, so its text and password inputs use the same templates and interaction states as Depot without introducing a runtime dependency on `Depot.exe`.

## Updates and repair

Depot Manager reads published GitHub releases, validates semantic release tags and the exact `Depot-<version>.exe` asset name, checks HTTP success and file length, validates the Windows PE executable structure, and verifies the GitHub-provided SHA-256 digest when present. The downloaded executable version is checked before any installed executable is replaced.

Update compares the installed executable's product/file version with the latest eligible stable GitHub Release. Repair deliberately resolves the exact currently installed release and obtains that same published version again; repair therefore cannot silently turn into an update.

Before update or repair Depot must be closed normally. The manager does not kill the application by default. The current executable is copied to `Backup\Depot-<previous-version>.exe`; file-version revision metadata such as `.0` is not added to the release-style backup name. Older executable backups are removed so only one backup is retained. Business data, database content, audit history, and user preferences are not included in executable backup or rollback behavior.

Repair replaces the application binary without resetting configuration or data. The backup executable is a recovery artifact only; schema downgrade and automatic database rollback are deliberately unsupported. Update and repair do not add, remove, or otherwise change an existing desktop shortcut.

## Uninstall

Uninstall offers three explicit outcomes: cancel, remove only the application while retaining local data, or remove the application together with all local Depot data. The full local-data option removes `depot.settings`, `%LOCALAPPDATA%\Depot` including logs and the default SQLite database, and a configured SQLite database stored outside that folder together with its SQLite `-wal`, `-shm`, and `-journal` sidecars.

Remote MySQL/MariaDB and SQL Server databases are never deleted by Depot Manager. Application-only uninstall remains suitable when the local configuration or data should be retained for a later reinstall. Both the Start menu shortcut and a previously created desktop shortcut are removed during uninstall.

## Windows integration

Depot Manager registers Depot under the current user's Windows `Installed Apps`/Uninstall registry key with the installed version, install location, icon, and manager as the modify/uninstall entry point. A Start menu shortcut launches `Depot.exe`. During the initial installation the user may additionally request a desktop shortcut; it points to the same installed `Depot.exe`, uses the installation directory as working directory, and uses the Depot application icon. Multiple concurrent manager instances are blocked by a named Windows mutex.

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
