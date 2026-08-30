# Depot Versioning

Depot uses [Semantic Versioning](https://semver.org/) for application releases:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

The single application-version source is `Directory.Build.props` in the repository root.

## Current versions

- Application development line: **0.15.x-preview**
- Core database schema: **30**
- Sales feature schema: **10**
- Finance feature schema: **9**

Application, core database and feature-schema versions are independent compatibility dimensions. Every repository commit increments `DepotVersionPatch`. A persistence change increments the schema version that owns that persistence contract.

## Application version components

- `MAJOR` changes for incompatible stable releases.
- `MINOR` changes for backward-compatible features. Before 1.0 it may also mark a significant preview milestone.
- `PATCH` changes for repository commits on the current development line.
- `PRERELEASE` identifies preview, beta, or release-candidate builds.
- `BUILD` contributes revision metadata and does not change SemVer precedence.

The MSBuild properties produce `Version`/`InformationalVersion`, a stable `AssemblyVersion` within a major/minor line and numeric `FileVersion` in `MAJOR.MINOR.PATCH.BUILD` form. The About page reads built assembly information and displays application and database compatibility information.

## Core database schema versioning

`src/Depot/Data/DatabaseVersion.cs` is the shared/core schema-version source. Core schema **30** remains the compatibility baseline used by SQLite, Microsoft SQL Server and MySQL/MariaDB.

The core schema version changes when shared/core persistence owned by the provider initializers changes. Feature-local schemas use their own ordered migration ledgers and do not artificially increment the core version merely because a feature adds persistence.

For every **core** schema change:

1. add equivalent current-schema definitions for all three providers;
2. add a forward migration from the previous supported core version;
3. increment `DatabaseVersion.CurrentVersion` once;
4. update backup/table definitions when persisted data changes;
5. add/update migration and workflow tests;
6. update architecture, status, roadmap and version documentation.

## Feature schema versioning

Feature schemas are tracked in `DepotFeatureVersions`. A feature persistence change increments that feature's `CurrentVersion` and adds a forward migration from the previous feature version for every supported provider.

### Sales schema 10

Sales schema 9 introduced provider-neutral scoped PriceLists, optional Sales Regions, scope/region constraints/indexes and retained price-source metadata on quote/order lines.

Sales schema **10** adds the Item Cost Build-up persistence contract:

- `ItemCostProfiles` with an explicit Base Cost source, ISO currency and optimistic version;
- `ItemCostComponents` with Absolute/Percentage calculation type, explicit percentage base, value, deterministic sequence, activity/validity and optimistic version;
- deterministic item/sequence lookup ordering;
- provider-equivalent SQLite, SQL Server and MySQL/MariaDB DDL.

The Sales 9 → 10 migration is additive. It does not rewrite existing PriceLists, PriceList entries, supplier purchase prices or historical Sales-document snapshots.

### Finance schema 9

Finance maintains an independent sequential feature schema through Finance schema 9. Finance feature migrations remain governed by their own current-version contract and tests.

For every **feature** schema change:

1. add provider-equivalent schema definitions under the owning feature;
2. add a forward migration from the previous feature version;
3. increment the owning feature's schema version exactly once;
4. preserve existing data unless the migration explicitly documents a controlled transformation;
5. add SQLite migration coverage and supported live-provider coverage where infrastructure permits;
6. update documentation that states the feature version or persistence contract.

## Provider acceptance

Automated SQLite migration coverage is required. SQL Server and MySQL/MariaDB migrations use the same functional contract and optional live-provider suites. Provider-neutral implementation alone is not production certification; supported server versions still require live migration, locking/concurrency, recovery and performance acceptance before a stable release.

## Creating a release

1. Complete `docs/Release1.0.md` for the target release.
2. Ensure the working tree contains the intended release changes only.
3. Run the full build and automated test suite.
4. Set the required version components in `Directory.Build.props`.
5. For a stable build, publish with:

```powershell
dotnet publish src\Depot\Depot.csproj -c Release -p:DepotStableRelease=true -p:DepotVersionBuild=1
```

Prerelease builds retain `DepotVersionSuffix`. Stable releases set `DepotStableRelease=true` so the suffix is omitted.

## Release documentation rule

Do not call a provider or workflow production-ready solely because its implementation compiles. Stable-release documentation requires automated coverage where practical and recorded manual acceptance for environment-dependent behavior such as server migrations, recovery, deployment and multi-client operation.
