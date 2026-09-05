using Depot.Models;
using DepotManager;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests;

public sealed class DepotManagerHardeningTests
{
    [Theory]
    [InlineData(false, false, false, false, false, false, false, null, false, InstallationHealthState.NotInstalled)]
    [InlineData(true, false, false, true, true, true, true, 30, true, InstallationHealthState.InstallationDamaged)]
    [InlineData(true, true, true, true, false, false, false, null, true, InstallationHealthState.ProvisioningIncomplete)]
    [InlineData(true, true, true, true, true, false, false, null, true, InstallationHealthState.ConfigurationDamaged)]
    [InlineData(true, true, true, true, true, true, false, null, true, InstallationHealthState.DatabaseUnavailable)]
    [InlineData(true, true, true, true, true, true, true, 29, true, InstallationHealthState.DatabaseMigrationRequired)]
    [InlineData(true, true, true, true, true, true, true, 31, true, InstallationHealthState.RecoveryRequired)]
    [InlineData(true, true, true, false, true, true, true, 30, true, InstallationHealthState.RepairRecommended)]
    [InlineData(true, true, true, true, true, true, true, 30, true, InstallationHealthState.InstalledHealthy)]
    public void InstallationStateRules_AreDeterministic(
        bool registry,
        bool depot,
        bool depotValid,
        bool manager,
        bool settings,
        bool settingsReadable,
        bool databaseReachable,
        int? schema,
        bool windowsHealthy,
        InstallationHealthState expected)
    {
        var actual = InstallationStateRules.Determine(
            registry, depot, depotValid, manager, settings, settingsReadable, databaseReachable, schema, 30, windowsHealthy);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ManagerUpdateComparison_OnlyAcceptsNewerVersion()
    {
        Assert.True(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 17, 0), new Version(0, 1, 18)));
        Assert.False(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 18, 0), new Version(0, 1, 18)));
        Assert.False(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 18, 0), new Version(0, 1, 17)));
    }

    [Fact]
    public void RollbackCompatibility_RequiresExactSchemaMatch()
    {
        Assert.True(RollbackCompatibility.IsCompatible(30, 30));
        Assert.False(RollbackCompatibility.IsCompatible(31, 30));
        Assert.False(RollbackCompatibility.IsCompatible(29, 30));
    }

    [Fact]
    public void DiagnosticsSanitizer_RemovesCredentialBearingLines()
    {
        var input = "normal line\nPassword=secret\nUser ID=admin;Server=db\nnormal end\n";
        var sanitized = ManagerDiagnosticsService.SanitizeLog(input);
        Assert.Contains("normal line", sanitized, StringComparison.Ordinal);
        Assert.Contains("normal end", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqliteMigrationBackup_CapturesConsistentWalState()
    {
        var root = CreateTempDirectory();
        var database = Path.Combine(root, "depot.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Sample(Id INTEGER PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO Sample(Value) VALUES ('preserved');";
                await command.ExecuteNonQueryAsync();
            }

            var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.Local, LocalDatabasePath = database };
            var backup = await new MigrationSafetyService().CreateSqliteSafetyBackupAsync(
                settings, root, new Version(0, 15, 141), 30, CancellationToken.None);

            Assert.True(File.Exists(backup));
            await using var validation = new SqliteConnection($"Data Source={backup};Mode=ReadOnly");
            await validation.OpenAsync();
            await using var query = validation.CreateCommand();
            query.CommandText = "SELECT Value FROM Sample;";
            Assert.Equal("preserved", Convert.ToString(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DatabaseSchemaInspector_ReadsDepotSchemaWithoutMigrating()
    {
        var root = CreateTempDirectory();
        var database = Path.Combine(root, "schema.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE DatabaseInfo(Id INTEGER PRIMARY KEY, Version INTEGER NOT NULL); INSERT INTO DatabaseInfo(Id, Version) VALUES (1, 27);";
                await command.ExecuteNonQueryAsync();
            }
            var settings = new DatabaseConnectionSettings { Provider = DatabaseProvider.Local, LocalDatabasePath = database };
            Assert.Equal(27, await new DatabaseSchemaInspector().ReadSchemaVersionAsync(settings, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DepotManagerHardeningTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
