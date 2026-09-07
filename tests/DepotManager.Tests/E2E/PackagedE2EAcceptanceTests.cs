using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Depot.Models;
using DepotManager;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Depot.Tests.E2E;

[CollectionDefinition("DepotManager Packaged E2E", DisableParallelization = true)]
public sealed class PackagedE2ECollection
{
    public const string CollectionName = "DepotManager Packaged E2E";
}

[Collection(PackagedE2ECollection.CollectionName)]
public sealed class PackagedE2EAcceptanceTests
{
    private const int CurrentSchema = 30;

    [PackagedE2EFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Smoke")]
    public void PackagedArtifacts_AreRealSingleFileWindowsExecutables_WithExpectedVersionProgression()
    {
        var artifacts = new[]
        {
            PackagedE2EEnvironment.CurrentDepot,
            PackagedE2EEnvironment.CurrentManager,
            PackagedE2EEnvironment.PreviousDepot,
            PackagedE2EEnvironment.PreviousManager
        };

        foreach (var artifact in artifacts)
        {
            Assert.True(File.Exists(artifact), $"Missing packaged artifact: {artifact}");
            Assert.True(new FileInfo(artifact).Length > 5_000_000, $"Artifact is unexpectedly small: {artifact}");
            PortableExecutableValidator.ValidateWindowsExecutable(artifact);
            Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(artifact)!, "*", SearchOption.TopDirectoryOnly));
        }

        Assert.Equal(new Version(0, 15, 160), ReadVersion(PackagedE2EEnvironment.CurrentDepot));
        Assert.Equal(new Version(0, 15, 159), ReadVersion(PackagedE2EEnvironment.PreviousDepot));
        Assert.Equal(new Version(0, 1, 23), ReadVersion(PackagedE2EEnvironment.CurrentManager));
        Assert.Equal(new Version(0, 1, 22), ReadVersion(PackagedE2EEnvironment.PreviousManager));
    }

    [PackagedE2EFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Smoke")]
    public async Task CleanInstall_NormalUpdate_AndRepair_PreserveProvisionedData()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(CleanInstall_NormalUpdate_AndRepair_PreserveProvisionedData));
        var install = Path.Combine(root, "install");
        var database = Path.Combine(root, "data", "depot.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        var service = new InstallationService(install, _ => { });
        var previousVersion = ReadVersion(PackagedE2EEnvironment.PreviousDepot);
        var currentVersion = ReadVersion(PackagedE2EEnvironment.CurrentDepot);

        service.Deploy(PackagedE2EEnvironment.PreviousDepot, previousVersion, createBackup: false);
        ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.PreviousManager, service.ManagerPath);
        Assert.Equal(previousVersion, service.InstalledVersion);
        Assert.Equal(ReadVersion(PackagedE2EEnvironment.PreviousManager), ReadVersion(service.ManagerPath));

        var provision = await RunDepotCommandAsync(service.DepotPath, install, "--manager-provision", CreateProvisioningRequest(database));
        Assert.True(provision.Success, provision.Message);
        Assert.Equal(CurrentSchema, provision.DatabaseSchemaVersion);
        Assert.True(File.Exists(service.SettingsPath));
        Assert.Equal(CurrentSchema, await ReadSchemaAsync(database));
        Assert.Equal(1, await CountAdministratorAsync(database));

        var settingsHash = await HashAsync(service.SettingsPath);
        service.Deploy(PackagedE2EEnvironment.CurrentDepot, currentVersion, createBackup: true);
        RollbackMetadataService.Write(service.BackupDirectory, previousVersion, CurrentSchema);
        ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.CurrentManager, service.ManagerPath);
        var health = await RunDepotCommandAsync(service.DepotPath, install, "--manager-health-check");
        Assert.True(health.Success, health.Message);
        Assert.Equal(currentVersion, service.InstalledVersion);
        Assert.Equal(1, await CountAdministratorAsync(database));

        await using (var damaged = new FileStream(service.DepotPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            damaged.SetLength(1024);
        }
        Assert.ThrowsAny<Exception>(() => PortableExecutableValidator.ValidateWindowsExecutable(service.DepotPath));

        service.Deploy(PackagedE2EEnvironment.CurrentDepot, currentVersion, createBackup: false);
        ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.CurrentManager, service.ManagerPath);
        var repairedHealth = await RunDepotCommandAsync(service.DepotPath, install, "--manager-health-check");
        Assert.True(repairedHealth.Success, repairedHealth.Message);
        Assert.Equal(settingsHash, await HashAsync(service.SettingsPath));
        Assert.Equal(1, await CountAdministratorAsync(database));
    }

    [PackagedE2EFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Smoke")]
    public async Task ArtifactAndManifestValidation_FailsClosed()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(ArtifactAndManifestValidation_FailsClosed));
        var currentVersion = ReadVersion(PackagedE2EEnvironment.CurrentDepot);
        Assert.Throws<InvalidOperationException>(() =>
            InstallationService.ValidateTargetVersion(PackagedE2EEnvironment.PreviousDepot, currentVersion));

        var corrupt = Path.Combine(root, "corrupt.exe");
        File.Copy(PackagedE2EEnvironment.CurrentDepot, corrupt);
        await using (var stream = new FileStream(corrupt, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.WriteByte(0);
            stream.WriteByte(0);
        }
        Assert.ThrowsAny<Exception>(() => PortableExecutableValidator.ValidateWindowsExecutable(corrupt));

        var bytes = await File.ReadAllBytesAsync(PackagedE2EEnvironment.CurrentDepot);
        var uri = new Uri("https://example.test/Depot.exe");
        var release = new ReleaseInfo(currentVersion, VersionRules.VersionText(currentVersion), VersionRules.AssetName(currentVersion), uri, bytes.LongLength, null);

        using (var client = new HttpClient(new StaticBytesHandler(bytes)))
        {
            var releaseClient = new GitHubReleaseClient(client);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                releaseClient.DownloadAsync(release with { Size = bytes.LongLength + 1 }, Path.Combine(root, "wrong-size.exe"), null, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                releaseClient.DownloadAsync(release with { Sha256 = new string('0', 64) }, Path.Combine(root, "wrong-sha.exe"), null, CancellationToken.None));
        }

        var missingAssetJson = BuildReleaseJson(currentVersion, null, null);
        using (var client = new HttpClient(new ReleaseMetadataHandler(missingAssetJson, null)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DepotReleaseMetadataClient(client).GetAsync(release, CancellationToken.None));
        }

        var invalidManifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            depotVersion = VersionRules.VersionText(currentVersion),
            depotManagerVersion = VersionRules.VersionText(ReadVersion(PackagedE2EEnvironment.CurrentManager)),
            databaseSchemaVersion = 0,
            managerCommandProtocol = 1
        }));
        var invalidReleaseJson = BuildReleaseJson(currentVersion, $"Depot-{VersionRules.VersionText(currentVersion)}.manifest.json", invalidManifest.LongLength);
        using (var client = new HttpClient(new ReleaseMetadataHandler(invalidReleaseJson, invalidManifest)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DepotReleaseMetadataClient(client).GetAsync(release, CancellationToken.None));
        }

        AuthenticodeVerifier.ValidateTrustedSignature(PackagedE2EEnvironment.CurrentManager);
        var tamperedManager = Path.Combine(root, "tampered-manager.exe");
        File.Copy(PackagedE2EEnvironment.CurrentManager, tamperedManager);
        await using (var stream = new FileStream(tamperedManager, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var position = Math.Min(8192L, stream.Length - 1);
            stream.Position = position;
            var original = stream.ReadByte();
            stream.Position = position;
            stream.WriteByte((byte)(original ^ 0x5A));
        }
        Assert.Throws<CryptographicException>(() => AuthenticodeVerifier.ValidateTrustedSignature(tamperedManager));
    }

    [PackagedE2EFullFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Full")]
    public async Task HistoricalSchema29_MigratesTo30_OnlyAfterVerifiedSqliteBackup_AndIsIdempotent()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(HistoricalSchema29_MigratesTo30_OnlyAfterVerifiedSqliteBackup_AndIsIdempotent));
        var install = Path.Combine(root, "install");
        var database = Path.Combine(root, "data", "historical.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        var service = new InstallationService(install, _ => { });
        var previousVersion = ReadVersion(PackagedE2EEnvironment.PreviousDepot);
        var currentVersion = ReadVersion(PackagedE2EEnvironment.CurrentDepot);

        service.Deploy(PackagedE2EEnvironment.PreviousDepot, previousVersion, createBackup: false);
        var provision = await RunDepotCommandAsync(service.DepotPath, install, "--manager-provision", CreateProvisioningRequest(database));
        Assert.True(provision.Success, provision.Message);
        await WriteSchemaAsync(database, 29);
        Assert.Equal(29, await ReadSchemaAsync(database));

        var settings = LocalSettings(database);
        var safetyBackup = await new MigrationSafetyService().CreateSqliteSafetyBackupAsync(
            settings, install, previousVersion, 29, CancellationToken.None);
        Assert.Equal(Path.GetFullPath(safetyBackup), MigrationBackupGate.EnsureVerifiedSqliteBackup(safetyBackup));
        Assert.Equal(29, await ReadSchemaAsync(safetyBackup));

        service.Deploy(PackagedE2EEnvironment.CurrentDepot, currentVersion, createBackup: true);
        var migration = await RunDepotCommandAsync(service.DepotPath, install, "--manager-migrate");
        Assert.True(migration.Success, migration.Message);
        Assert.Equal(CurrentSchema, migration.DatabaseSchemaVersion);
        Assert.Equal(CurrentSchema, await ReadSchemaAsync(database));
        Assert.Equal(29, await ReadSchemaAsync(safetyBackup));

        var secondMigration = await RunDepotCommandAsync(service.DepotPath, install, "--manager-migrate");
        Assert.True(secondMigration.Success, secondMigration.Message);
        Assert.Equal(CurrentSchema, secondMigration.DatabaseSchemaVersion);
        var health = await RunDepotCommandAsync(service.DepotPath, install, "--manager-health-check");
        Assert.True(health.Success, health.Message);

        Assert.Throws<InvalidOperationException>(() => MigrationBackupGate.EnsureVerifiedSqliteBackup(Path.Combine(root, "missing-backup.db")));
        Assert.Throws<InvalidOperationException>(() => MigrationBackupGate.EnsureExternalBackupConfirmed(false));
        MigrationBackupGate.EnsureExternalBackupConfirmed(true);
        Assert.Throws<InvalidOperationException>(() => MigrationBackupGate.EnsureTargetSchemaIsNotOlder(CurrentSchema, CurrentSchema - 1));

        var cancelledRoot = Path.Combine(root, "cancelled");
        Directory.CreateDirectory(cancelledRoot);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MigrationSafetyService().CreateSqliteSafetyBackupAsync(settings, cancelledRoot, previousVersion, CurrentSchema, cancelled.Token));
        var cancelledBackupDirectory = Path.Combine(cancelledRoot, "Backups", "Database");
        var cancelledBackups = Directory.Exists(cancelledBackupDirectory)
            ? Directory.EnumerateFiles(cancelledBackupDirectory, "*.db")
            : Enumerable.Empty<string>();
        Assert.Empty(cancelledBackups);
    }

    [PackagedE2EFullFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Full")]
    public async Task RollbackAndInterruptedReplacement_PreserveSchemaAndOriginalBinary()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(RollbackAndInterruptedReplacement_PreserveSchemaAndOriginalBinary));
        var install = Path.Combine(root, "install");
        var database = Path.Combine(root, "data", "depot.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        var service = new InstallationService(install, _ => { });
        var previousVersion = ReadVersion(PackagedE2EEnvironment.PreviousDepot);
        var currentVersion = ReadVersion(PackagedE2EEnvironment.CurrentDepot);

        service.Deploy(PackagedE2EEnvironment.PreviousDepot, previousVersion, createBackup: false);
        Assert.True((await RunDepotCommandAsync(service.DepotPath, install, "--manager-provision", CreateProvisioningRequest(database))).Success);
        service.Deploy(PackagedE2EEnvironment.CurrentDepot, currentVersion, createBackup: true);
        RollbackMetadataService.Write(service.BackupDirectory, previousVersion, CurrentSchema);
        var candidate = RollbackMetadataService.Read(service.BackupDirectory);
        Assert.NotNull(candidate);
        Assert.True(candidate!.IsValid, candidate.Message);

        await WriteSchemaAsync(database, CurrentSchema + 1);
        Assert.Throws<InvalidOperationException>(() => RollbackMetadataService.RestoreExecutable(service, candidate, CurrentSchema + 1));
        Assert.Equal(currentVersion, service.InstalledVersion);
        Assert.Equal(CurrentSchema + 1, await ReadSchemaAsync(database));

        await WriteSchemaAsync(database, CurrentSchema);
        RollbackMetadataService.RestoreExecutable(service, candidate, CurrentSchema);
        Assert.Equal(previousVersion, service.InstalledVersion);
        Assert.Equal(CurrentSchema, await ReadSchemaAsync(database));

        service.Deploy(PackagedE2EEnvironment.PreviousDepot, previousVersion, createBackup: false);
        var originalHash = await HashAsync(service.DepotPath);
        await using (var locked = new FileStream(service.DepotPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => ExecutableDeployment.Replace(PackagedE2EEnvironment.CurrentDepot, service.DepotPath));
        }
        Assert.Equal(originalHash, await HashAsync(service.DepotPath));
        Assert.False(File.Exists(service.DepotPath + ".new"));
    }

    [PackagedE2EFullFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Full")]
    public async Task ManagerSelfUpdate_UsesTrustedSignedArtifact_AndRevertsWhenUpdatedManagerCannotStart()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(ManagerSelfUpdate_UsesTrustedSignedArtifact_AndRevertsWhenUpdatedManagerCannotStart));
        var target = Path.Combine(root, "DepotManager.exe");
        File.Copy(PackagedE2EEnvironment.PreviousManager, target, true);
        AuthenticodeVerifier.ValidateTrustedSignature(PackagedE2EEnvironment.CurrentManager);
        AuthenticodeVerifier.ValidateTrustedSignature(PackagedE2EEnvironment.PreviousManager);

        await RunManagerUpdateHelperAsync(root, target, forceStartupFailure: false);
        Assert.Equal(ReadVersion(PackagedE2EEnvironment.CurrentManager), ReadVersion(target));
        await WaitUntilAsync(() => !File.Exists(ManagerSelfUpdatePaths.GetStagedPath(target)), TimeSpan.FromSeconds(15));

        await StopManagersAtPathAsync(target);
        File.Copy(PackagedE2EEnvironment.PreviousManager, target, true);
        await RunManagerUpdateHelperAsync(root, target, forceStartupFailure: true);
        Assert.Equal(ReadVersion(PackagedE2EEnvironment.PreviousManager), ReadVersion(target));
        await StopManagersAtPathAsync(target);
    }

    [PackagedE2EFullFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Full")]
    public async Task UninstallScopes_KeepOrDeleteLocalData_Idempotently_WithoutWindowsIntegration()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(UninstallScopes_KeepOrDeleteLocalData_Idempotently_WithoutWindowsIntegration));
        var install = Path.Combine(root, "install");
        var dataRoot = Path.Combine(root, "local-data");
        var database = Path.Combine(dataRoot, "depot.db");
        Directory.CreateDirectory(dataRoot);
        var service = new InstallationService(install, _ => { });
        var previousVersion = ReadVersion(PackagedE2EEnvironment.PreviousDepot);

        service.Deploy(PackagedE2EEnvironment.PreviousDepot, previousVersion, createBackup: false);
        ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.PreviousManager, service.ManagerPath);
        Assert.True((await RunDepotCommandAsync(service.DepotPath, install, "--manager-provision", CreateProvisioningRequest(database))).Success);

        service.RemoveApplicationFiles(removeConfiguration: false);
        Assert.False(File.Exists(service.DepotPath));
        Assert.False(File.Exists(service.ManagerPath));
        Assert.True(File.Exists(service.SettingsPath));
        Assert.True(File.Exists(database));
        service.RemoveApplicationFiles(removeConfiguration: false);

        service.Deploy(PackagedE2EEnvironment.CurrentDepot, ReadVersion(PackagedE2EEnvironment.CurrentDepot), createBackup: false);
        ExecutableDeployment.InstallManagerCopy(PackagedE2EEnvironment.CurrentManager, service.ManagerPath);
        service.RemoveApplicationFiles(removeConfiguration: true);
        LocalDataDeletion.DeleteSqliteDatabaseFiles(database);
        LocalDataDeletion.DeleteDirectory(dataRoot);
        Assert.False(File.Exists(service.SettingsPath));
        Assert.False(Directory.Exists(dataRoot));

        service.RemoveApplicationFiles(removeConfiguration: true);
        LocalDataDeletion.DeleteSqliteDatabaseFiles(database);
        LocalDataDeletion.DeleteDirectory(dataRoot);
    }

    [PackagedE2EFullFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Full")]
    public void DiagnosticsSupportPackage_RedactsSecrets_AndUsesOnlyIsolatedLogs()
    {
        var root = PackagedE2EEnvironment.CreateScenarioRoot(nameof(DiagnosticsSupportPackage_RedactsSecrets_AndUsesOnlyIsolatedLogs));
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "DepotManager.log"),
            "normal line\nPassword=super-secret\nAuthorization: Bearer abc123\nUser ID=admin;Server=db\napi-key=hidden\nnormal end\n");

        var snapshot = new InstallationSnapshot(
            InstallationHealthState.InstalledHealthy,
            Path.Combine(root, "install"),
            ReadVersion(PackagedE2EEnvironment.CurrentDepot),
            ReadVersion(PackagedE2EEnvironment.CurrentManager),
            CurrentSchema,
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
            "Packaged E2E healthy state.");
        var diagnostics = new ManagerDiagnosticsService(logs);
        var package = diagnostics.CreateSupportPackage(
            Path.Combine(root, "support.zip"),
            snapshot,
            ReadVersion(PackagedE2EEnvironment.CurrentManager),
            new ManagerDiagnosticsContext("0.15.160", "0.1.23", "30", DateTimeOffset.UtcNow.ToString("O"), "Available"));

        Assert.True(File.Exists(package));
        using var archive = ZipFile.OpenRead(package);
        var combined = new StringBuilder();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            combined.AppendLine(reader.ReadToEnd());
        }
        var text = combined.ToString();
        Assert.Contains("normal line", text, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer abc123", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID=admin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key=hidden", text, StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateProvisioningRequest(string databasePath) => new
    {
        Database = LocalSettings(databasePath),
        Administrator = new
        {
            DisplayName = "Packaged E2E Administrator",
            Email = "packaged-e2e-admin@depot.local",
            Password = "Depot-E2E-Only-7v!Q3x"
        }
    };

    private static DatabaseConnectionSettings LocalSettings(string databasePath) => new()
    {
        Provider = DatabaseProvider.Local,
        LocalDatabasePath = Path.GetFullPath(databasePath),
        AutomaticBackupsEnabled = false,
        BackupDirectory = "Backups",
        BackupIntervalDays = 1
    };

    private static async Task<ManagerCommandResult> RunDepotCommandAsync(string executable, string workingDirectory, string command, object? request = null)
    {
        var response = Path.Combine(workingDirectory, $"e2e-{Guid.NewGuid():N}.response.json");
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = request is not null
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(response);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start packaged Depot command {command}.");
        if (request is not null)
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request));
            process.StandardInput.Close();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        if (!File.Exists(response)) throw new InvalidOperationException($"Packaged Depot command {command} returned no response. Exit code: {process.ExitCode}.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(response, timeout.Token));
        var result = new ManagerCommandResult(
            document.RootElement.GetProperty("Success").GetBoolean(),
            document.RootElement.GetProperty("Message").GetString() ?? string.Empty,
            document.RootElement.TryGetProperty("DatabaseSchemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number ? schema.GetInt32() : null,
            process.ExitCode);
        if (!result.Success || result.ExitCode != 0)
            throw new InvalidOperationException($"Packaged Depot command {command} failed: {result.Message} (exit {result.ExitCode}).");
        return result;
    }

    private static async Task RunManagerUpdateHelperAsync(string root, string target, bool forceStartupFailure)
    {
        var staged = ManagerSelfUpdatePaths.GetStagedPath(target);
        File.Copy(PackagedE2EEnvironment.CurrentManager, staged, true);
        var marker = ManagerSelfUpdatePaths.CreateReadyMarkerPath(target);
        var startInfo = new ProcessStartInfo(staged)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--apply-manager-update");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add(marker);
        startInfo.Environment["DEPOT_PACKAGED_E2E"] = "1";
        startInfo.Environment["DEPOT_PACKAGED_E2E_EXIT_AFTER_STARTUP"] = "1";
        startInfo.Environment["DEPOT_PACKAGED_E2E_FORCE_STARTUP_FAILURE"] = forceStartupFailure ? "1" : "0";

        using var helper = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the packaged Depot Manager update helper.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await helper.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, helper.ExitCode);
    }

    private static async Task StopManagersAtPathAsync(string target)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            var found = false;
            foreach (var process in Process.GetProcessesByName("DepotManager"))
            {
                using (process)
                {
                    try
                    {
                        if (!string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                        found = true;
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        process.WaitForExit(10_000);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            if (!found) return;
            await Task.Delay(200);
        } while (DateTimeOffset.UtcNow < deadline);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(200);
        }
        Assert.True(condition(), "Timed out waiting for packaged E2E state transition.");
    }

    private static Version ReadVersion(string file)
    {
        var text = FileVersionInfo.GetVersionInfo(file).FileVersion;
        Assert.True(Version.TryParse(text, out var version), $"File version could not be read from {file}.");
        return VersionRules.ReleaseVersion(version!);
    }

    private static async Task<string> HashAsync(string file)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<int> ReadSchemaAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM DatabaseInfo LIMIT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WriteSchemaAsync(string database, int version)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DatabaseInfo SET Version = $version;";
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAdministratorAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE lower(Email) = 'packaged-e2e-admin@depot.local' AND IsActive = 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BuildReleaseJson(Version version, string? manifestName, long? manifestSize)
    {
        object[] assets = manifestName is null
            ? []
            : [new { name = manifestName, size = manifestSize, browser_download_url = "https://example.test/manifest" }];
        return JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease = false,
            tag_name = VersionRules.VersionText(version),
            name = "Packaged E2E release",
            body = "Acceptance fixture",
            published_at = DateTimeOffset.UtcNow,
            assets
        });
    }

    private sealed record ManagerCommandResult(bool Success, string Message, int? DatabaseSchemaVersion, int ExitCode);

    private sealed class StaticBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private sealed class ReleaseMetadataHandler(string releaseJson, byte[]? manifestBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("example.test", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(manifestBytes ?? [])
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
