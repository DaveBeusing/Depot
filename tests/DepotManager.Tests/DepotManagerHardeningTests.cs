using System.Security.Cryptography;

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
        Assert.True(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 20, 0), new Version(0, 1, 21)));
        Assert.False(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 21, 0), new Version(0, 1, 21)));
        Assert.False(ManagerReleaseClient.IsUpdateAvailable(new Version(0, 1, 21, 0), new Version(0, 1, 20)));
    }

    [Fact]
    public void ManagerSelfUpdatePaths_UseExecutableHelperAndScopedMarker()
    {
        var root = CreateTempDirectory();
        try
        {
            var manager = Path.Combine(root, "DepotManager.exe");
            var staged = ManagerSelfUpdatePaths.GetStagedPath(manager);
            var previous = ManagerSelfUpdatePaths.GetPreviousPath(manager);
            var marker = ManagerSelfUpdatePaths.CreateReadyMarkerPath(manager);

            Assert.EndsWith("DepotManager.update.exe", staged, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("DepotManager.previous.exe", previous, StringComparison.OrdinalIgnoreCase);
            Assert.True(ManagerSelfUpdatePaths.IsExpectedStagedHelper(manager, staged));
            Assert.True(ManagerSelfUpdatePaths.IsReadyMarkerForTarget(manager, marker));
            Assert.False(ManagerSelfUpdatePaths.IsReadyMarkerForTarget(manager, Path.Combine(Path.GetTempPath(), "foreign.marker")));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void UpdateRecovery_RequiresExactKnownSchemaMatch()
    {
        Assert.True(UpdateRecoveryRules.CanRestorePreviousExecutable(30, 30));
        Assert.False(UpdateRecoveryRules.CanRestorePreviousExecutable(31, 30));
        Assert.False(UpdateRecoveryRules.CanRestorePreviousExecutable(29, 30));
        Assert.False(UpdateRecoveryRules.CanRestorePreviousExecutable(null, 30));
        Assert.False(UpdateRecoveryRules.CanRestorePreviousExecutable(30, null));
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
        var input = "normal line\nPassword=secret\nUser ID=admin;Server=db\nAuthorization: Bearer abc\napi-key=123\nnormal end\n";
        var sanitized = ManagerDiagnosticsService.SanitizeLog(input);
        Assert.Contains("normal line", sanitized, StringComparison.Ordinal);
        Assert.Contains("normal end", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer abc", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key=123", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthenticodeVerifier_RejectsUnsignedFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var file = Path.Combine(root, "unsigned.exe");
            File.WriteAllText(file, "not signed");
            Assert.Throws<CryptographicException>(() => AuthenticodeVerifier.ValidateTrustedSignature(file));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
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
            DeleteTempDirectory(root);
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
            DeleteTempDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DepotManagerHardeningTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
