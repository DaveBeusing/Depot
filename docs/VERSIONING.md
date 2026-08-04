# Depot Versioning

Depot uses [Semantic Versioning](https://semver.org/) for application releases:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

The single application-version source is `Directory.Build.props` in the repository root.

## Current versions

- Application development line: **0.13.0-preview**
- Database schema version: **29**

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

Schema version 29 adds `Notifications` and `NotificationRecipients` for the private internal Notification Center. It includes unique materialized recipients, user-scoped unread/archive state, expiry handling, source indexes, stable inbox sorting, and provider-neutral migrations for SQLite, SQL Server, and MySQL/MariaDB. Initial purchase-order and inventory-count events join the existing workflow/audit transaction; scheduled backup failures create independent system notifications.

Schema version 28 replaces fixed single-role authorization with provider-neutral database-backed RBAC. `Roles`, `Permissions`, `RolePermissions`, and `UserRoles` support multiple roles per user and effective-permission union. Protected system roles are seeded from the central permission catalog. Existing administrator, purchasing, approver, warehouse-operator, and standard users are assigned equivalent roles; legacy user columns remain only for compatibility and do not drive authorization.

Schema version 27 introduces the `WorkflowOperations` idempotency ledger for critical procurement and material-booking transitions. Operation IDs are unique across providers and are committed atomically with status, movements, and audit data, allowing clients to reconcile ambiguous network outcomes without repeating a booking.

Schema version 26 adds fixed user roles and their provider-neutral permission mapping. Existing administrators migrate to Administrator, existing purchase approvers to Approver, and all other accounts to User. Purchasing and WarehouseOperator can then be assigned through user administration. The migration retains legacy flags for compatibility while `AuthorizationService` and workflow services use the fixed role matrix as the effective contract.

Schema version 25 adds structured supplier-return documents and lines. Returns are tied to received positions, validate the remaining net-received quantity and current stock inside the posting transaction, create immutable negative movements, and retain receipt and purchase-order received quantities as historical facts. Reversal metadata allows counter-booked returns to be excluded from later net-return calculations across SQLite, SQL Server, and MySQL/MariaDB.

Schema version 24 adds independent material-return documents and lines. Returns optionally reference a posted material issue, create their own positive movement type, and remain distinct from reversal movements. Draft lifecycle, posting metadata, reason-code references, constraints, indexes, optimistic concurrency, and provider-specific document locking share the established transactional architecture.

Schema version 23 adds structured material-issue documents and lines, including generated issue numbers, lifecycle status, responsible users and timestamps, required per-line reason-code references, optimistic-concurrency versions, constraints, and indexes. Posting and reversal use the shared provider-neutral transaction and movement infrastructure without direct stock updates.

Schema version 22 adds explicit purchase-order closure metadata. A closure records the required reason, acting user, and UTC timestamp without altering received or remaining quantities. Closed orders reject further goods receipts; cancellation remains reserved for orders without posted receipts. The status change and audit entry share one transaction on every provider.

Schema version 21 adds the purchase-order approval workflow. Purchase orders store creator, submission, and compact approval-decision metadata; users can receive the explicit `CanApprovePurchaseOrders` permission. Existing numeric status values remain stable for migration compatibility, while new approval, rejection, and closed states extend the workflow.

`src/Depot/Data/DatabaseVersion.cs` is the schema-version source. All providers must use the same current version:

- SQLite: `DepotDatabase`
- Microsoft SQL Server: `SqlServerDatabase`
- MySQL/MariaDB: `MySqlDatabase`

Schema version 20 introduces immutable movement reversals. `StockMovement.ReversalOfMovementId` is a unique optional self-reference and is accompanied by reversal reason, timestamp, and user metadata. Goods receipts, stock transfers, and inventory counts receive versioned reversal metadata so their business correction and audit entry can be committed with all counter-movements in one transaction. Original movements remain unchanged, duplicate full reversals and reversal chains are rejected, and all three providers share the same schema contract.

Schema version 19 adds inventory counts and inventory-count lines, including warehouse and inventory references, unique snapshot lines, status and counted-quantity constraints, indexes, audit-ready timestamps, and optimistic-concurrency versions. Starting a count creates its complete movement-derived warehouse snapshot atomically. Posting does not require another schema change: it preserves that snapshot and calculates each correction against the movement-derived current stock inside the posting transaction. Schema version 18 adds warehouse stock transfers, including transfer lines, foreign keys, uniqueness constraints, indexes, and optimistic-concurrency versions. The application posts transfers atomically as paired TransferOut/TransferIn movements while retaining the same schema version. Schema version 17 separates goods receipts from supplier invoices. It adds the supplier delivery-note number and receiving user, retains historical invoice columns as nullable legacy data, and preserves receipt-line references. Existing receipts receive deterministic `LEGACY-GR-…` delivery-note numbers and use the original audit user where available. Schema version 16 added immutable technical reason-code keys and system-code metadata.

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
