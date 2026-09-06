# Depot automated tests

The test suite uses xUnit and targets the same .NET Windows framework as the WPF applications.

## Running the tests

SQLite integration tests are self-contained. Tests are expected to create isolated temporary databases and remove them after completion; fixtures must not depend on execution order or state left by another test.

Run the complete solution test set with:

```powershell
dotnet test Depot.slnx --configuration Release
```

Run one bounded CI regression area with the same filters and hang protection used by GitHub Actions:

```powershell
./scripts/quality/run-test-area.ps1 -Area Finance -Configuration Release
./scripts/quality/run-test-area.ps1 -Area DepotManager -Configuration Release
```

The standard functional areas are Core, Persistence, Finance, Security-Auth, Sessions-Audit, Audit-Integrity, DepotManager, Sales, Purchasing, Procurement-Receiving, Supplier-Returns, Inventory-Warehouse, Inventory-Operations and Shell-UX. Core is the fallback for otherwise unclassified tests. Normal areas exclude `QualityGate=Performance`; the 100,000-row performance baseline runs as an independent quality job.

Depot production tests are in `tests/Depot.Tests`. DepotManager production tests are in `tests/DepotManager.Tests` and directly reference `src/DepotManager/DepotManager.csproj`, so they exercise the same assembly boundary that is published.

## Coverage

Production coverage uses `tests/coverage.runsettings` and includes only `Depot.dll` and `DepotManager.dll`. Area reports are merged by `scripts/quality/assert-code-coverage.ps1`, which prints Lines, Branches and Methods for Depot, DepotManager and Combined and enforces the configured line/branch quality gates.

To collect one area locally:

```powershell
./scripts/quality/run-test-area.ps1 -Area Finance -Configuration Release -Coverage -ResultsDirectory TestResults/Coverage/Finance
```

## Optional database-provider integration tests

Optional SQL Server and MySQL/MariaDB procurement integration tests are enabled with environment variables. The configured database name must contain `test`; the schema is initialized by the test and test-owned rows are removed afterward.

```powershell
$env:DEPOT_TEST_SQLSERVER_CONNECTION_STRING = "Server=localhost,1433;Database=DepotTest;User ID=<user>;Password=<password>;Encrypt=True;Trust Server Certificate=True"
$env:DEPOT_TEST_MYSQL_CONNECTION_STRING = "Server=localhost;Port=3306;Database=DepotTest;User ID=<user>;Password=<password>;SSL Mode=Required"
dotnet test tests/Depot.Tests/Depot.Tests.csproj
```

When an environment variable is absent or invalid, its server test is reported as skipped. Credentials belong only in the environment and must never be committed to the repository.
