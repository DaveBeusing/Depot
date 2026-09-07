# Database Provider Production Support Matrix

## Purpose

Depot does not treat provider-neutral SQL generation or SQLite compatibility as evidence that a remote database provider is production-ready. A provider/version combination is marked **Supported** only after the real-server workflow `.github/workflows/database-provider-acceptance.yml` has completed its full matrix successfully against that exact baseline.

## Depot 1.0 certification baselines

| Provider | Certification baseline | Provisioning | Migration | Finance | Concurrency / retry | Recovery | Performance | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SQLite | SQLite runtime shipped through `Microsoft.Data.Sqlite` | Existing reference baseline + provider acceptance | Global 29→30 and Sales 10→11 acceptance | Existing regression suite; shared provider primitives | Transaction/constraint/concurrency acceptance | Depot-managed SQLite backup path remains authoritative | 100k indexed lookup regression | Untested on this branch head |
| SQL Server | SQL Server 2022 Express, engine 16.x | Real Windows server | Global 29→30, Sales 10→11, concurrent provisioning | GL, AR, AP, payment/reversal, inventory valuation plus business workflows | Serializable transactions, lock inversion/deadlock retry, non-transient no-retry | Native `BACKUP DATABASE` / `RESTORE DATABASE`, then Depot reprovision recognition | 100k indexed lookup regression | Untested on this branch head |
| MariaDB | MariaDB 11.8 LTS | Real Windows server | Global 29→30, Sales 10→11, concurrent provisioning | GL, AR, AP, payment/reversal, inventory valuation plus business workflows | InnoDB serializable transactions, deadlock retry, non-transient no-retry | `mariadb-dump` / `mariadb` restore, then Depot reprovision recognition | 100k indexed lookup regression | Untested on this branch head |
| MySQL | MySQL 8.4 LTS | Real Windows server | Global 29→30, Sales 10→11, concurrent provisioning | GL, AR, AP, payment/reversal, inventory valuation plus business workflows | InnoDB serializable transactions, deadlock retry, non-transient no-retry | `mysqldump` / `mysql` restore, then Depot reprovision recognition | 100k indexed lookup regression | Untested on this branch head |

The status column is deliberately conservative. It is updated to **Supported** only after a green full provider workflow has produced reproducible evidence for the branch head. Newer or older server releases are not implicitly certified by a green baseline run.

## Acceptance layers

The provider matrix exercises the production database path, not mocks:

```text
real database server
→ DatabaseProvisioningService
→ global + feature migrations
→ repositories
→ business services
→ real transactions / locks / constraints
```

The shared acceptance includes:

- empty-server database creation and repeated provisioning;
- global schema version validation and historical 29→30 migration;
- Sales feature migration 10→11 and active-reservation uniqueness;
- parallel provisioning guarded by provider-native application/advisory locks;
- GUID, decimal, UTC timestamp, date-only, NULL, unique and foreign-key round trips;
- atomic rollback after an injected business-operation failure;
- concurrent mutations and deterministic final state;
- controlled remote deadlock generation with transaction recreation and bounded retry;
- proof that non-transient unique-constraint failures are not retried;
- Procurement goods-receipt concurrency, Supplier Return and audit rollback;
- Sales order → reservation → shipment → invoice completion and stock impact;
- Finance GL precision/idempotence/reversal, AR posting/payment/reversal, AP posting/payment/reversal and Three-Way-Match exception approval;
- Finance inventory valuation consumption/reversal reporting;
- session persistence, heartbeat and termination;
- connection-unavailable failure followed by a healthy connection-pool recovery;
- provider-server restart followed by Depot re-entry;
- provider-native backup/restore with a persisted recovery marker and post-restore Depot recognition;
- a representative 100,000-row indexed lookup regression guard.

## CI policy

Pull requests run:

- the SQLite provider baseline;
- critical SQL Server smoke acceptance;
- critical MariaDB smoke acceptance;
- critical MySQL smoke acceptance.

`master`, `database-provider-*` certification branches and manual workflow dispatches run the complete matrix, including server restart, native backup/restore and full business/performance acceptance.

Each remote job gets a unique database name and ephemeral credentials. Passwords and customer data are not committed. Provider/server version and schema versions are diagnostic output; connection secrets are not.

## Provider-specific behavior

### SQL Server

Depot uses serializable write transactions and explicit row locks (`UPDLOCK`, `HOLDLOCK`) where business serialization is required. Provisioning uses `sp_getapplock` around the entire global/feature migration sequence.

### MariaDB and MySQL

Both products use the same `MySqlConnector` implementation path in Depot but are certified independently. InnoDB `FOR UPDATE`, serializable write transactions and `GET_LOCK` provisioning serialization are exercised on both server families.

The Sales active-reservation invariant cannot use SQL Server-style filtered indexes. Depot therefore exposes an `ActiveInventoryId` generated column whose value is the inventory ID only for active reservations; a unique index on `(SalesOrderLineId, ActiveInventoryId)` provides the same business invariant while allowing multiple released rows through NULL semantics.

### SQLite

SQLite remains the embedded reference baseline. Local-only capabilities such as `VACUUM` and DepotManager's local pre-migration safety-copy path are not claims about remote-provider functionality.

## Backup / disaster-recovery boundary

Depot does not claim to replace SQL Server, MariaDB or MySQL operational backup tooling. Production operators remain responsible for provider-native backup scheduling, retention, off-host copies and disaster-recovery procedures.

The production acceptance proves only the required technical boundary: a provider-native backup can be restored, Depot recognizes the restored schema, and persisted database identity/state is not silently re-provisioned as a fresh business database.

## Support policy

- **Supported**: exact baseline has a green full provider acceptance run for the release candidate.
- **Best effort**: technically compatible family/version outside the certified baseline; no production guarantee.
- **Untested**: no reproducible full acceptance evidence exists for the combination.
- **Not supported**: known incompatible or intentionally excluded combination.

MariaDB and MySQL are never inferred from one another. A green run for one does not change the support status of the other.
