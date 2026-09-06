# Software Quality Baseline

Depot uses ISO/IEC 25010 as a quality-model reference, not as a claim of certification.

## Quality characteristics and evidence

- Functional suitability: critical inventory, warehouse, purchasing, sales, finance, administration, security, backup/recovery and invoicing workflows are covered by automated acceptance/regression tests.
- Performance efficiency: a repeatable SQLite gate creates 100,000 records and verifies a representative paged/indexed query. Baseline limits are 30 seconds for the synthetic bulk insert and 2 seconds for the representative query on GitHub-hosted Windows runners. These limits are regression gates, not end-user SLAs.
- Compatibility: Windows Server 2025 is the primary full CI runner. Windows Server 2022 retains a warning-free build plus targeted Depot and DepotManager compatibility smoke tests without duplicating the full regression suite.
- Interaction capability: shared WPF resources define buttons, inputs, statuses, empty states, workflows and navigation; changes should use these patterns rather than local one-off styling.
- Reliability: regression jobs use explicit hang detection and bounded job timeouts; existing tests cover transaction rollback, concurrency predicates, backup/recovery failure paths and workflow failures.
- Security: security-supply-chain and release-integrity workflows remain independent release gates. Security tests are split by authentication/authorization, audit/privacy/sessions and record-integrity/approval boundaries.
- Maintainability: architecture, coding standards, repository/service boundaries, lock files, build/test pipelines and review guidance are version controlled.
- Flexibility: SQLite plus SQL Server/MySQL/MariaDB provider abstractions are retained; real-provider acceptance is required for configurations declared production-supported.
- Safety: final business records use explicit corrections/reversals and atomic audit/business transactions so failures do not silently misrepresent business state.

## Regression architecture

Normal regression execution is split into bounded functional areas through `scripts/quality/run-test-area.ps1`: Core, Persistence, Finance, Security-Auth, Sessions-Audit, Audit-Integrity, DepotManager, Sales, Purchasing, Procurement-Receiving, Supplier-Returns, Inventory-Warehouse, Inventory-Operations and Shell-UX. The Core filter is a negative fallback so tests that are not yet assigned to a named area are still executed rather than silently omitted.

DepotManager regression tests live in `tests/DepotManager.Tests` and reference the shipped `src/DepotManager/DepotManager.csproj` directly. They do not compile linked copies of DepotManager production sources into the Depot test assembly. This keeps test identity and production coverage aligned with `DepotManager.dll`.

The normal regression groups exclude the dedicated `QualityGate=Performance` tests. The 100,000-row performance baseline runs separately so performance runner variance cannot block unrelated functional groups structurally. Security boundary tests likewise remain a separate workflow from ordinary regression execution.

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

Performance tests intentionally use generous deterministic limits to catch severe regressions without making CI dependent on runner noise. Production sizing must additionally test representative database providers, network latency, concurrent users, actual report/export workloads and data distributions.
