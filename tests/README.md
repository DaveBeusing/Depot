# Depot automated tests

The test suite uses xUnit and targets the same .NET Windows framework as the WPF application.

## Running the tests

SQLite integration tests are self-contained and always run locally. Each test creates an isolated temporary database and removes it after completion.

```powershell
dotnet test tests\Depot.Tests\Depot.Tests.csproj
```

Optional SQL Server and MySQL/MariaDB procurement integration tests are enabled with environment variables. The configured database name must contain `test`; the schema is initialized by the test and test-owned rows are removed afterward.

```powershell
$env:DEPOT_TEST_SQLSERVER_CONNECTION_STRING = "Server=localhost,1433;Database=DepotTest;User ID=<user>;Password=<password>;Encrypt=True;Trust Server Certificate=True"
$env:DEPOT_TEST_MYSQL_CONNECTION_STRING = "Server=localhost;Port=3306;Database=DepotTest;User ID=<user>;Password=<password>;SSL Mode=Required"
dotnet test tests\Depot.Tests\Depot.Tests.csproj
```

When an environment variable is absent or invalid, its server test is reported as skipped. Credentials belong only in the environment and must never be committed to the repository.
