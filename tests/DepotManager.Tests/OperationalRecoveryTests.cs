using System.IO.Compression;

using Depot.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepotManager.Tests;

public sealed class OperationalRecoveryTests
{
    [Fact]
    public async Task SqliteRecoveryDrill_RestoresVerifiedCopyAndReportsSchema()
    {
        var root = CreateTempDirectory();
        var database = Path.Combine(root, "depot.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE DatabaseInfo(Id INTEGER PRIMARY KEY, Version INTEGER NOT NULL); INSERT INTO DatabaseInfo(Id, Version) VALUES (1, 30); CREATE TABLE RecoveryMarker(Id INTEGER PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO RecoveryMarker(Id, Value) VALUES (1, 'preserved');";
                await command.ExecuteNonQueryAsync();
            }

            var settings = new DatabaseConnectionSettings
            {
                Provider = DatabaseProvider.Local,
                LocalDatabasePath = database
            };
            var backup = await new MigrationSafetyService().CreateSqliteSafetyBackupAsync(
                settings,
                root,
                new Version(0, 15, 172),
                30,
                CancellationToken.None);

            var evidence = await new SqliteRecoveryDrillService().ValidateBackupAsync(
                backup,
                Path.Combine(root, "isolated-restore"),
                CancellationToken.None);

            Assert.Equal("SQLite", evidence.Provider);
            Assert.Equal("PASS", evidence.Result);
            Assert.Equal(30, evidence.DatabaseSchemaVersion);
            Assert.Equal(evidence.BackupSha256, evidence.RestoredSha256);
            Assert.True(evidence.BackupSizeBytes > 0);
            Assert.True(File.Exists(backup));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "isolated-restore"), "*.db"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("AccessDenied")]
    [InlineData("MissingResource")]
    [InlineData("FileSystemIo")]
    public void RecoveryFailureDiagnostics_ProducesActionableSanitizedGuidance(string expected)
    {
        Exception exception = expected switch
        {
            "AccessDenied" => new UnauthorizedAccessException("Password=super-secret"),
            "MissingResource" => new FileNotFoundException("token=super-secret"),
            _ => new IOException("authorization=super-secret")
        };

        Assert.Equal(expected, RecoveryFailureDiagnostics.Classify(exception));
        var guidance = RecoveryFailureDiagnostics.Describe(exception);
        Assert.False(string.IsNullOrWhiteSpace(guidance));
        Assert.DoesNotContain("super-secret", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization=", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoveryWritableProbe_FailsClosedWhenTargetIsAFile()
    {
        var root = CreateTempDirectory();
        try
        {
            var file = Path.Combine(root, "not-a-directory");
            File.WriteAllText(file, "occupied");

            var exception = Assert.ThrowsAny<Exception>(() => SqliteRecoveryDrillService.EnsureWritableDirectory(file));
            Assert.True(exception is IOException or UnauthorizedAccessException or InvalidOperationException);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SupportPackage_IncludesRecoveryReadiness_AndSurvivesLockedLog()
    {
        var root = CreateTempDirectory();
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        var log = Path.Combine(logs, "DepotManager.log");
        File.WriteAllText(log, "Password=super-secret");

        try
        {
            var snapshot = new InstallationSnapshot(
                InstallationHealthState.InstalledHealthy,
                Path.Combine(root, "install"),
                new Version(0, 15, 173),
                new Version(0, 1, 23),
                30,
                DatabaseProvider.Local,
                Path.Combine(root, "data", "depot.db"),
                false,
                true,
                true,
                false,
                false,
                true,
                false,
                false,
                false,
                false,
                "Recovery support package test.");

            using var locked = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var package = new ManagerDiagnosticsService(logs).CreateSupportPackage(
                Path.Combine(root, "support.zip"),
                snapshot,
                new Version(0, 1, 23));

            using var archive = ZipFile.OpenRead(package);
            Assert.Contains(archive.Entries, entry => entry.FullName == "RecoveryReadiness.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "CollectionWarnings.txt");

            var combined = string.Join("\n", archive.Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }));
            Assert.DoesNotContain("super-secret", combined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("filesystem I/O failure", combined, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DepotOperationalRecovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
