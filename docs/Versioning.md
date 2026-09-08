# Versioning and schema evolution

Updated: 2026-09-08

## Current baselines

- Application development line: **0.15.168-preview**
- Core database schema: **30**
- Sales feature schema: **11**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- Help manifest: **1.21**

Application, Core database and feature-schema versions are independent compatibility dimensions.

Every repository commit increments `DepotVersionPatch` in `Directory.Build.props`. Database schema versions change only when the corresponding persisted schema contract changes.

## Core database schema

`DatabaseVersion.CurrentVersion` is the authoritative Core database schema version. The provider-certification work does not change the Core database schema; it remains **30**.

Core migrations must:

1. be deterministic and forward-only;
2. preserve existing data unless a controlled transformation is explicitly required;
3. execute through the provider/database initialization path rather than a UI-specific implementation;
4. remain idempotent where initialization can be repeated safely;
5. update Core schema tests and the live-provider acceptance path;
6. update documentation that states the Core schema version or persistence contract.

## Feature schemas

Feature schemas are tracked independently through `DepotFeatureVersions`. A feature migration does not increment Core schema unless it also changes the Core schema contract.

### Sales schema 10

Sales schema **10** introduced Item Cost Build-up persistence:

- `ItemCostProfiles` with explicit Base Cost source, ISO currency and optimistic version;
- `ItemCostComponents` with Absolute/Percentage calculation type, explicit percentage base, value, deterministic sequence, activity/validity and optimistic version;
- deterministic item/component ordering and lookup indexes.

### Sales schema 11

Sales schema **11** establishes the active inventory-reservation uniqueness invariant consistently across all supported database providers.

- SQLite uses a partial unique index for active reservations.
- SQL Server uses a filtered unique index for active reservations.
- MariaDB/MySQL use generated `ActiveInventoryId` semantics plus a unique `(SalesOrderLineId, ActiveInventoryId)` index so released reservations may coexist while active duplicates are rejected.

The migration also reconciles databases that had previously advanced the Sales feature version while the remote-provider invariant was not physically present. This is a Sales feature-schema/data-integrity correction; **Core schema remains 30**.

### Finance schema 9

Finance schema **9** is the current Finance persistence baseline and contains the sequential foundation, General Ledger, Accounts Receivable/Payable, Inventory Accounting, Banking, Financial Reporting and Localization structures.

### User Sessions schema 3

User Sessions schema **3** is the current persistent session/policy/history baseline.

### Security Events schema 2

Security Events schema **2** is the current authentication-security event/policy/throttle baseline.

## Provider compatibility and certification

Provider-neutral DDL/code is an implementation property, not a support decision. Production provider support is governed by `.github/workflows/database-provider-acceptance.yml` and [Database Provider Production Support Matrix](DatabaseProviderSupportMatrix.md).

Current certified database baselines are:

- Depot-bundled SQLite runtime;
- SQL Server 2022 / engine 16.x, certified in CI with SQL Server 2022 Express;
- MariaDB 11.8.9 LTS;
- MySQL 8.4.11 LTS.

MariaDB and MySQL are accepted independently even though both use `MySqlConnector`.

A schema/feature change that affects provider persistence must include live-provider migration/behavior coverage before the corresponding provider/version remains production-supported.

## Migration concurrency

Remote provisioning serializes the entire authoritative provisioning sequence, not only the initial Core initializer:

- SQL Server: `sp_getapplock` session lock scoped to the Depot database;
- MariaDB/MySQL: `GET_LOCK` advisory lock scoped to the Depot database.

The lock covers Core initialization plus Sales, Finance, User Sessions and Security Events feature migrations so parallel startup cannot independently advance the same database.

## Retry compatibility

Write-transaction retry is restricted to known transient provider failures. Retries recreate the complete connection/transaction and use bounded exponential backoff with jitter. Business errors and ordinary unique/FK/validation failures are never treated as transient.

## Release compatibility

DepotManager release metadata continues to record the target Core database schema. A target release cannot be treated as compatible solely because its application version is newer; the manager validates the Core schema contract and uses Depot's authoritative provisioning path for migrations.

Remote database rollback/downgrade is never automatic. Provider-native backup responsibility and exact supported baselines are documented separately in the support matrix.
