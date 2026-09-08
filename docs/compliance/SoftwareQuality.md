# Software Quality Baseline

Updated: 2026-09-08

Depot uses ISO/IEC 25010 as a quality-model reference, not as a claim of certification.

## Quality characteristics and evidence

- Functional suitability: critical inventory, warehouse, purchasing, sales, finance, administration, security, backup/recovery and invoicing workflows are covered by automated acceptance/regression tests.
- Performance efficiency: provider acceptance creates 100,000 representative indexed rows on the bundled SQLite runtime, SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11. These are regression guards, not end-user SLAs.
- Compatibility: Windows Server 2025 is the primary full CI/provider runner. Windows Server 2022 retains a warning-free build plus targeted Depot and DepotManager compatibility smoke tests without duplicating the full regression suite.
- Interaction capability: shared WPF resources define buttons, inputs, statuses, empty states, workflows and navigation; changes should use these patterns rather than local one-off styling.
- Reliability: regression jobs use explicit hang detection and bounded job timeouts; provider acceptance additionally covers transactional rollback, concurrent mutations, deadlock/write-conflict retry, server restart and native remote backup/restore boundaries.
- Security: security-supply-chain and release-integrity workflows remain independent release gates. Security tests are split by authentication/authorization, audit/privacy/sessions and record-integrity/approval boundaries.
- Maintainability: architecture, coding standards, repository/service boundaries, lock files, build/test pipelines and review guidance are version controlled.
- Flexibility: SQLite, SQL Server, MariaDB and MySQL remain behind the established provider abstractions. Exact production-supported baselines are controlled by `docs/DatabaseProviderSupportMatrix.md` and live acceptance rather than inferred from code similarity.
- Safety: final business records use explicit corrections/reversals and atomic audit/business transactions so failures do not silently misrepresent business state.

## Regression architecture

Normal regression execution is split into bounded functional areas through `scripts/quality/run-test-area.ps1`: Core, Persistence, Finance, Security-Auth, Sessions-Audit, Audit-Integrity, DepotManager, Sales, Purchasing, Procurement-Receiving, Supplier-Returns, Inventory-Warehouse, Inventory-Operations and Shell-UX. The Core filter is a negative fallback so tests that are not yet assigned to a named area are still executed rather than silently omitted.

DepotManager regression tests live in `tests/DepotManager.Tests` and reference the shipped `src/DepotManager/DepotManager.csproj` directly. They do not compile linked copies of DepotManager production sources into the Depot test assembly. This keeps test identity and production coverage aligned with `DepotManager.dll`.

The normal regression groups exclude dedicated `QualityGate=Performance` tests. The provider matrix owns its separate real-provider performance baseline so runner variance cannot structurally block unrelated functional groups. Security boundary tests likewise remain a separate workflow from ordinary regression execution.

## Database provider production acceptance

`.github/workflows/database-provider-acceptance.yml` is the production database evidence gate. On certification branches, `master` and manual full runs it validates:

- real SQL Server 2022, MariaDB 11.8.9 and MySQL 8.4.11 servers plus the bundled SQLite baseline;
- fresh/idempotent and concurrent provisioning;
- Core 29→30 and Sales 10→11 migration behavior;
- provider SQL/type/constraint/date/decimal behavior;
- rollback, concurrency and bounded transient retry;
- Sales, Procurement, sessions, Finance GL/AR/AP/FIFO, Banking/reconciliation and Financial Reporting/snapshot paths;
- server restart and post-restart application re-entry;
- provider-native remote backup/restore and post-restore recognition;
- a representative 100,000-row indexed performance guard.

Pull requests intentionally use a faster remote smoke profile while retaining full SQLite acceptance. Full certification remains required before a provider baseline is declared Supported.

## Production coverage

`tests/coverage.runsettings` scopes coverage to the production assemblies `Depot.dll` and `DepotManager.dll` and excludes test assemblies. `scripts/quality/assert-code-coverage.ps1` merges area reports by production class/line identity and reports Lines, Branches and Methods separately for Depot, DepotManager and Combined.

The first usable production-assembly calibration merge on 2026-09-06 produced the following conservative measured floor. Nine area reports were available in that merge; because the aggregator OR-merges execution hits for the same production line, branch and method, adding further area reports cannot reduce these percentages.

| Area | Lines | Branches | Methods |
| --- | ---: | ---: | ---: |
| Depot | 39.89% | 40.57% | 36.98% |
| DepotManager | 23.35% | 26.69% | 33.77% |
| Combined | 38.93% | 39.72% | 36.88% |

The enforced regression thresholds intentionally sit below that measured floor to tolerate small instrumentation changes while still preventing silent coverage loss:

| Gate | Minimum |
| --- | ---: |
| Combined line coverage | 38% |
| Combined branch coverage | 39% |
| DepotManager line coverage | 22% |
| DepotManager branch coverage | 25% |

Method coverage remains reported for Depot, DepotManager and Combined but is informational until a stable method baseline has been observed across subsequent releases. Coverage gates are expected to move upward with sustained test growth; they must not be weakened to make a failing change pass without an explicit quality decision.

## Performance interpretation

Performance tests intentionally use generous deterministic limits to catch severe regressions without making CI dependent on runner noise. Production sizing must additionally test realistic network latency, concurrent users, actual report/export workloads, retention history, customer-specific data distributions and operational peak behavior.

The SQLite dynamic-typing/`NUMERIC` precision boundary and exact remote provider support versions are documented in [Database Provider Production Support Matrix](../DatabaseProviderSupportMatrix.md).
