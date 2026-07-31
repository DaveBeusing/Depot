# Depot Versioning

Depot uses [Semantic Versioning](https://semver.org/) for application releases:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

The single application-version source is `Directory.Build.props` in the repository root.

## Current versions

- Application development line: **0.9.1-preview**
- Database schema version: **17**

The application version and database schema version are independent. A patch application release can retain the same schema, while a schema migration can occur during a prerelease line.

## Application version components

- `MAJOR` changes for incompatible stable releases.
- `MINOR` changes for backward-compatible features. Before 1.0 it may also mark a significant preview milestone.
- `PATCH` changes for backward-compatible fixes.
- `PRERELEASE` identifies preview, beta, or release-candidate builds.
- `BUILD` contributes revision metadata and does not change SemVer precedence.

The MSBuild properties produce:

- `Version` and `InformationalVersion` from the SemVer components;
- stable `AssemblyVersion` within a major/minor line;
- numeric `FileVersion` in `MAJOR.MINOR.PATCH.BUILD` form;
- source revision metadata in deterministic CI informational versions.

The About page reads the built assembly information and displays the application, file, informational, runtime, and database-schema versions.

## Database schema versioning

`src/Depot/Data/DatabaseVersion.cs` is the schema-version source. All providers must use the same current version:

- SQLite: `DepotDatabase`
- Microsoft SQL Server: `SqlServerDatabase`
- MySQL/MariaDB: `MySqlDatabase`

Schema version 17 separates goods receipts from supplier invoices. It adds the supplier delivery-note number and receiving user, retains historical invoice columns as nullable legacy data, and preserves receipt-line references. Existing receipts receive deterministic `LEGACY-GR-…` delivery-note numbers and use the original audit user where available. Schema version 16 added immutable technical reason-code keys and system-code metadata.

For every schema change:

1. Add equivalent current-schema definitions for all three providers.
2. Add a forward migration from the previous supported version for all three providers.
3. Increment `DatabaseVersion.CurrentVersion` once.
4. Update database-backup table definitions when persisted data changes.
5. Add or update migration and core-workflow tests.
6. Update README, Architecture, Roadmap, and release documentation.

SQLite migrations are exercised by the automated integration suite. SQL Server and MySQL/MariaDB migration scripts must also be tested against live supported server versions before a stable release.

## Creating a release

1. Complete `docs/RELEASE_1_0.md` for the target release.
2. Ensure the working tree contains the intended release changes only.
3. Run the full build and automated test suite.
4. Set the required version components in `Directory.Build.props`.
5. For a stable build, publish with:

```powershell
dotnet publish src\Depot\Depot.csproj -c Release -p:DepotStableRelease=true -p:DepotVersionBuild=1
```

Prerelease builds retain `DepotVersionSuffix`. Stable releases set `DepotStableRelease=true` so the suffix is omitted.

## Release documentation rule

Do not call a provider or workflow production-ready solely because its implementation compiles. Stable-release documentation requires automated coverage where practical and recorded manual acceptance for environment-dependent behavior such as server migrations, recovery, deployment, and multi-client operation.
