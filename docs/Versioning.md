# Versioning and schema evolution

Updated: 2026-09-16

## Current baselines

- Application: **0.15.x-preview**
- Core database schema: **30**
- Sales feature schema: **14**
- Finance feature schema: **9**
- User Sessions feature schema: **3**
- Security Events feature schema: **2**
- User Preferences feature schema: **2**
- Enterprise Identity feature schema: **1**
- Help manifest: **1.21**

Application, Core database and feature-schema versions are independent compatibility dimensions. `Directory.Build.props` is the authoritative source for the exact application patch/version; long-lived documentation records the development line rather than duplicating the moving patch number.

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

### Sales schema 12

Sales schema **12** introduced Advanced Pricing persistence and expanded Item Cost Build-up with explicit base-cost strategies plus versioned, directional, effective-dated `PricingExchangeRates`. Runtime Sales pricing still resolves through the established pricing service and historical commercial evidence remains immutable.

### Sales schema 13

Sales schema **13** adds the production electronic-invoice evidence required by the bounded XRechnung 3.0 CII path, including explicit VAT treatment, retained recipient/routing evidence, immutable finalization records and electronic Sales Credit Note evidence.

### Sales schema 14

Sales schema **14** is the current Sales persistence baseline. It adds immutable ZUGFeRD 2.5.2 / Factur-X 1.09.2 hybrid invoice artifacts for the XRECHNUNG profile. The exact PDF/A-3B bytes, the finalized embedded `xrechnung.xml` SHA-256, the PDF SHA-256 and conformance metadata are retained together; later export does not rebuild the hybrid document from mutable master data.

### Finance schema 9

Finance schema **9** is the current Finance persistence baseline and contains the sequential foundation, General Ledger, Accounts Receivable/Payable, Inventory Accounting, Banking, Financial Reporting and Localization structures.

### User Sessions schema 3

User Sessions schema **3** is the current persistent session/policy/history baseline.

### Security Events schema 2

Security Events schema **2** is the current authentication-security event/policy/throttle baseline.

### User Preferences schema 2

User Preferences schema **2** is the current persistent workspace/default-view preference baseline.

### Enterprise Identity schema 1

Enterprise Identity schema **1** is the current external-identity foundation. It stores non-secret enterprise identity-provider configuration plus exact external provider/issuer/subject links to existing local Depot users.

The schema deliberately does not persist external roles, groups or permission grants. External identity proves account identity only; local Depot roles and permissions remain authoritative. Provider/issuer/subject uniqueness is represented by a deterministic SHA-256 identity key so the invariant is consistent even when a server provider uses a case-insensitive default collation.

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

The lock covers Core initialization plus Sales, Finance, User Sessions, Security Events, User Preferences and Enterprise Identity feature migrations so parallel startup cannot independently advance the same database.

## Retry compatibility

Write-transaction retry is restricted to known transient provider failures. Retries recreate the complete connection/transaction and use bounded exponential backoff with jitter. Business errors and ordinary unique/FK/validation failures are never treated as transient.

## Release compatibility

The authoritative release manifest/evidence records Core plus all current feature-schema versions, including User Preferences and Enterprise Identity. A target release cannot be treated as compatible solely because its application version is newer.

DepotManager release metadata continues to record the target Core database schema and uses Depot's authoritative provisioning path for migrations. Remote database rollback/downgrade is never automatic. Provider-native backup responsibility and exact supported baselines are documented separately in the support matrix.
