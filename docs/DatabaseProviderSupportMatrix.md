# Database Provider Production Support Matrix

Updated: 2026-09-08

## Purpose

Depot does not treat provider-neutral SQL generation, successful compilation, or SQLite-only regression coverage as evidence that a remote database provider is production-ready. A provider/version combination is marked **Supported** only after the real-server workflow `.github/workflows/database-provider-acceptance.yml` completes the applicable full matrix successfully.

## Depot 1.0 certification baselines

| Provider | Certified baseline | Provisioning / migration | Finance and business workflows | Concurrency / retry | Recovery | Performance | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SQLite | SQLite runtime bundled with the Depot release through `Microsoft.Data.Sqlite` | Fresh provisioning, idempotent provisioning, Core 29→30 and Sales 10→11 acceptance | Sales, Procurement, sessions, GL, AR, AP, FIFO/inventory accounting, Banking/reconciliation and Financial Reporting/snapshots | Immediate write transactions, rollback, constraints and concurrent mutation acceptance | Depot-managed SQLite backup/reopen boundary | 100k indexed lookup regression | **Supported** |
| SQL Server | SQL Server 2022, engine 16.x; certification environment uses SQL Server 2022 Express | Real Windows server, Core 29→30, Sales 10→11, concurrent provisioning | Sales, Procurement, sessions, GL, AR, AP, FIFO/inventory accounting, Banking/reconciliation and Financial Reporting/snapshots | Serializable transactions, provider locks, controlled deadlock/retry and non-transient no-retry | Native `BACKUP DATABASE` / `RESTORE DATABASE`, restart and Depot re-entry | 100k indexed lookup regression | **Supported** |
| MariaDB | MariaDB **11.8.9** LTS | Real Windows server, Core 29→30, Sales 10→11, concurrent provisioning | Sales, Procurement, sessions, GL, AR, AP, FIFO/inventory accounting, Banking/reconciliation and Financial Reporting/snapshots | InnoDB serializable transactions, `FOR UPDATE`, transient write-conflict/deadlock retry and non-transient no-retry | `mariadb-dump` / `mariadb` restore, restart and Depot re-entry | 100k indexed lookup regression | **Supported** |
| MySQL | MySQL **8.4.11** LTS | Real Windows server, Core 29→30, Sales 10→11, concurrent provisioning | Sales, Procurement, sessions, GL, AR, AP, FIFO/inventory accounting, Banking/reconciliation and Financial Reporting/snapshots | InnoDB serializable transactions, `FOR UPDATE`, deadlock retry and non-transient no-retry | `mysqldump` / `mysql` restore, restart and Depot re-entry | 100k indexed lookup regression | **Supported** |

Support applies to the listed baseline and the Depot release that carries the corresponding green full provider workflow. Newer or older server versions are not implicitly certified merely because they belong to the same product family.

SQL Server editions share the same engine line for the database behavior used by Depot, but CI certification is executed on SQL Server 2022 Express. Edition-specific capabilities that Depot does not exercise are outside this certification.

## Acceptance layers

The provider matrix exercises production database paths rather than mocks:

```text
real database server / bundled SQLite runtime
→ DatabaseProvisioningService
→ global + feature migrations
→ repositories
→ business services
→ real transactions / locks / constraints
```

The acceptance suite covers:

- empty-server/database provisioning and repeated idempotent provisioning;
- Core database schema **29 → 30** and Sales feature schema **10 → 11** migration;
- active Sales reservation uniqueness on every supported provider;
- parallel remote provisioning serialized by provider-native application/advisory locks;
- GUID, decimal, UTC timestamp, date-only, NULL, boolean, unique and foreign-key behavior;
- atomic rollback after an injected operation failure;
- concurrent mutation with deterministic final state;
- controlled remote lock inversion/deadlock behavior with complete transaction recreation and bounded retry;
- proof that non-transient unique-constraint failures are not retried;
- Procurement goods-receipt concurrency, Supplier Return and audit rollback;
- Sales order → reservation → shipment → invoice completion and stock impact;
- Finance General Ledger posting/idempotence/reversal;
- Accounts Receivable posting/payment/reversal;
- Accounts Payable posting/payment/reversal and Three-Way-Match exception authorization;
- FIFO inventory valuation consumption/reversal reporting;
- Banking account persistence, CSV statement import/idempotence, GL reconciliation and reconciliation reversal;
- Financial Reporting Trial Balance generation, deterministic CSV export and immutable report-snapshot idempotence;
- persistent user sessions, heartbeat and termination;
- unavailable-connection failure followed by healthy connection-pool recovery;
- remote database service restart followed by successful Depot re-entry;
- provider-native backup/restore with a persisted recovery marker and post-restore Depot recognition;
- a representative 100,000-row indexed lookup regression guard.

## CI policy

Pull requests run:

- the complete SQLite provider acceptance baseline;
- critical SQL Server smoke acceptance;
- critical MariaDB smoke acceptance;
- critical MySQL smoke acceptance.

`master`, `database-provider-*` certification branches and manual workflow dispatches run the full remote matrix, including server restart, provider-native backup/restore and the complete business/performance acceptance suite.

Each remote job receives a unique database name and ephemeral credentials. Passwords are masked and are not committed. Provider/server version and schema versions are diagnostic output; connection secrets are not.

## Provider-specific behavior

### SQL Server

Depot uses serializable write transactions and explicit row locks (`UPDLOCK`, `HOLDLOCK`) where business serialization is required. Provisioning uses `sp_getapplock` around the complete global and feature migration sequence.

### MariaDB and MySQL

Both products use the established `MySqlConnector` implementation path but are certified independently. A green MariaDB run never implies MySQL support and vice versa.

InnoDB `FOR UPDATE`, serializable write transactions and `GET_LOCK` provisioning serialization are exercised on both server families. Known transient lock/deadlock/write-conflict codes use bounded exponential retry with jitter and complete transaction recreation; non-transient failures are not retried.

MySQL/MariaDB `DATETIME(6)` persistence receives UTC `DateTime` parameters at the provider boundary for Finance timestamps instead of ISO-8601 text values that the server would reject.

The Sales active-reservation invariant cannot use SQL Server-style filtered indexes. Depot therefore uses an `ActiveInventoryId` generated column whose value is the inventory ID only for active reservations; a unique index on `(SalesOrderLineId, ActiveInventoryId)` provides the same business invariant while allowing multiple released rows through NULL semantics.

### SQLite

SQLite remains the embedded reference baseline. Local-only capabilities such as `VACUUM` and DepotManager's local pre-migration safety-copy path are not claims about remote-provider functionality.

SQLite uses dynamic typing and `NUMERIC` affinity rather than a server-style fixed `DECIMAL(28,9)` implementation. The acceptance suite proves nine fractional decimal digits for representative business-scale values, but SQLite cannot guarantee the full fixed-decimal magnitude/precision range available from SQL Server, MariaDB and MySQL. Deployments requiring exact very-large high-scale decimal values should select one of the certified server providers.

## Backup / disaster-recovery boundary

Depot does not claim to replace SQL Server, MariaDB or MySQL operational backup tooling. Production operators remain responsible for provider-native backup scheduling, retention, encryption where required, off-host copies, restore drills and disaster-recovery procedures.

The automated provider acceptance proves the technical boundary required by Depot: a provider-native backup can be restored, Depot recognizes the restored schema and persisted database identity/state is not silently treated as a new business database.

Depot's portable `.depotbackup` format is not advertised as a complete remote-provider disaster-recovery solution.

## Support policy

- **Supported** — the exact certification baseline has a green full provider acceptance run for the release candidate/release.
- **Best effort** — a technically compatible family/version outside the certified baseline; no production guarantee is implied.
- **Untested** — no reproducible full acceptance evidence exists for the combination.
- **Not supported** — known incompatible or intentionally excluded combination.

Examples outside the current certified matrix include SQL Server 2025, MariaDB 12.x and MySQL 9.x; they remain untested/best-effort until an explicit certification run is added.

Database-provider certification is a technical data-integrity/runtime statement. It does not constitute jurisdiction-specific accounting, tax, legal, accessibility, operating-system, banking-network or regulatory certification.
