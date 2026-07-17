# Depot versioning

Depot uses [Semantic Versioning](https://semver.org/) for application releases:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

The single version source is `Directory.Build.props` in the repository root.

## Version components

- `MAJOR` changes for incompatible public releases.
- `MINOR` changes for backward-compatible features. Before 1.0, it may also mark significant product changes.
- `PATCH` changes for backward-compatible fixes.
- `PRERELEASE` identifies development builds such as `preview.1`, `beta.1`, or `rc.1`.
- `BUILD` is generated from source revision metadata and does not affect version precedence.

The current development line is `0.9.0-preview.1`.

## .NET versions

- `Version` and `InformationalVersion` carry the SemVer application version.
- `AssemblyVersion` remains stable for a major/minor release line to avoid unnecessary binary-binding changes.
- `FileVersion` is numeric and uses `MAJOR.MINOR.PATCH.BUILD` for Windows file properties.
- The database schema version is independent from the application version and remains managed by `DatabaseVersion`.

## Creating releases

Update the Depot version components in `Directory.Build.props` in the release commit. For a stable build, publish with:

```powershell
dotnet publish src\Depot\Depot.csproj -c Release -p:DepotStableRelease=true -p:DepotVersionBuild=1
```

Prerelease builds retain `DepotVersionSuffix`. CI builds are deterministic and include the source revision in the informational version.
