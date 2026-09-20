# Deployment Sizing and Operations Readiness

## Purpose

Deployment sizing validates one concrete Depot build in one concrete target environment under representative data volume, network latency and concurrency. It produces capacity evidence and known limits for that environment; it does not establish a universal maximum user count or hardware recommendation.

Generic repository performance and database-provider gates remain prerequisites. They are not substitutes for customer-specific sizing.

## Evidence identity

Every sizing run must record:

- exact Depot commit SHA and application version;
- database provider and tested provider version;
- environment name and relevant client/database host characteristics;
- data profile and representative record volume;
- measured client-to-database latency;
- concurrent-user level;
- scenario iterations and PASS/FAIL state;
- p50/p95/max duration;
- average CPU and peak working set;
- applied optimizations, known limits and CI state.

Do not include customer names, document numbers, amounts or other business payload in sizing evidence.

## Required scenarios

A complete profile covers:

1. Startup;
2. Home;
3. Navigation;
4. Search;
5. My Work;
6. Large List;
7. Finance Report;
8. Export;
9. Import;
10. Document/PDF;
11. Backup Window.

The exact workflow inside each scenario must be documented with the evidence. Use representative permissions and business paths; do not bypass authorization, validation or transaction behavior to improve timings.

## Initial calibration matrix

The following matrix is a reproducible engineering starting point, not a support limit or deployment recommendation.

| Profile | Representative records | Concurrent users | Added client-to-DB latency | Purpose |
| --- | ---: | ---: | ---: | --- |
| Local reference | 100,000 | 1 | 0 ms | compare with repository/local baselines |
| Remote light | 250,000 | 5 | 10 ms | low-concurrency remote behavior |
| Remote standard | 1,000,000 | 20 | 25 ms | normal multi-user sizing exercise |
| Remote heavy | 5,000,000 | 50 | 50 ms | stress large lists, reporting and concurrency |
| Latency stress | 1,000,000 | 20 | 100 ms | isolate network sensitivity and retry behavior |

Adjust the matrix when the expected deployment shape differs. Record the changed profile explicitly instead of treating the table as a product guarantee.

## Measurement procedure

Use synthetic or otherwise approved test data. Start from a clean current build and record the exact SHA before testing.

For each scenario:

1. perform at least three warm-up runs that are excluded from reported percentiles;
2. capture at least twenty measured iterations per profile when practical;
3. record duration, CPU percentage, working set and PASS/FAIL for every measured iteration;
4. keep workflow inputs structurally equivalent between repetitions;
5. repeat the complete run when an outlier or failure cannot be reproduced.

The sizing aggregator uses nearest-rank p50/p95. A scenario with missing required coverage is not a complete sizing profile.

## Scenario CSV

Create a template:

```powershell
pwsh ./scripts/performance/Measure-DeploymentSizing.ps1 `
  -InitializeTemplate `
  -OutputDirectory ./artifacts/deployment-sizing
```

The generated CSV contains:

```text
Scenario,Iteration,DurationMs,CpuPercent,WorkingSetMb,Status
```

Use invariant numeric values and `PASS` or `FAIL`. Each row represents one measured iteration.

## Generate evidence

Example for a SQL Server target:

```powershell
pwsh ./scripts/performance/Measure-DeploymentSizing.ps1 `
  -ScenarioCsvPath ./artifacts/deployment-sizing/DeploymentSizingScenarios.csv `
  -PerformanceLogPath ./artifacts/runtime/performance.log `
  -EnvironmentName "Customer-like staging" `
  -Provider SqlServer `
  -CommitSha "<tested-sha>" `
  -DepotVersion "0.15.x-preview" `
  -DataProfile "Remote standard" `
  -DataVolumeRecords 1000000 `
  -ConcurrentUsers 20 `
  -NetworkLatencyMs 25 `
  -CiState "Required gates green" `
  -RequireCompleteProfile `
  -OutputDirectory ./artifacts/deployment-sizing
```

The script writes:

- `DeploymentSizingEvidence.json` for machine-readable retained evidence;
- `DeploymentSizingReport.md` for review and acceptance.

When supplied, `performance.log` contributes structural Home first-content and eligible My Work provider timing distributions. Those runtime signals supplement rather than replace the required scenario CSV.

## Concurrency and network validation

Concurrency must exercise real supported workflows against the target provider. Preserve provider locking, retry, authorization and transaction semantics. Do not replace production database access with mocks for deployment sizing.

Network latency must be measured or deliberately injected outside Depot and recorded as part of the environment. A local-provider baseline cannot be presented as representative of a remote database deployment.

## CPU, memory and UI responsiveness

Capture CPU and working set for each measured iteration. Investigate sustained resource growth, UI stalls, long garbage-collection pauses or increasing latency across repeated runs instead of reporting only final average duration.

Large-list scenarios must retain paging/virtualization/cancellation behavior. UI responsiveness must not be improved by loading unbounded result sets or removing stale-request protection.

## Hotspot rule

Only change runtime code after the evidence identifies a reproducible hotspot.

A hotspot is suitable for optimization when:

- the same path is slow across repeated measured iterations;
- the cost can be isolated to a query, allocation, serialization, report/export path, network round trip or UI application step;
- correctness and authorization remain unchanged;
- a focused regression or measurement can demonstrate the improvement.

After a fix, rerun the affected scenario and the complete provider/security/transaction acceptance relevant to the changed boundary.

## Backup window

The Backup Window scenario records the operational duration and success state of the deployment's supported provider-native backup procedure. Repository provider acceptance proves the generic restore boundary only; production scheduling, retention, off-host copies and recovery objectives remain deployment responsibilities.

## Interpretation and limits

Do not convert one environment's results into universal maximum values.

The final deployment statement should identify:

- the tested environment;
- tested provider/version;
- representative data volume;
- measured network latency;
- concurrent users;
- p50/p95 scenario timings;
- CPU/memory behavior;
- report/export/import/PDF and backup-window durations;
- optimizations actually applied;
- known limits and untested dimensions;
- final required CI/provider/security state.

A deployment is ready only for the tested boundary. Higher data volume, higher concurrency, materially different latency, different provider versions or different infrastructure require additional evidence.
