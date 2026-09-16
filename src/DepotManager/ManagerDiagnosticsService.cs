using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Depot.Repositories;

namespace DepotManager;

public sealed record ManagerDiagnosticsContext(
    string AvailableDepotVersion,
    string AvailableManagerVersion,
    string TargetDatabaseSchemaVersion,
    string ReleasePublishedAt,
    string RollbackStatus)
{
    public static ManagerDiagnosticsContext Empty { get; } = new("Unknown", "Unknown", "Unknown", "Unknown", "Unknown");
}

public sealed record ManagerDiagnosticsDocument(
    string DepotManagerVersion,
    string DepotVersion,
    string AvailableDepotVersion,
    string AvailableManagerVersion,
    string InstallDirectory,
    string OperatingSystem,
    string ProcessArchitecture,
    string DatabaseProvider,
    string DatabaseTarget,
    string DatabaseSchemaVersion,
    string TargetDatabaseSchemaVersion,
    string LastSuccessfulBackupUtc,
    string ReleasePublishedAt,
    string RollbackStatus,
    string InstallationHealth,
    string InstallationMessage,
    string GeneratedUtc);

public sealed record ManagerRecoveryDiagnosticsDocument(
    string DatabaseProvider,
    string DatabaseTarget,
    bool DatabaseReachable,
    string DatabaseSchemaVersion,
    string LastSuccessfulBackupUtc,
    string OperationalBackupBoundary,
    string DeploymentDrProfileStatus,
    string RestoreDrillEvidenceStatus,
    string GeneratedUtc);

public sealed class ManagerDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] SensitiveMarkers =
    [
        "password", "passwd", "pwd=", "token", "secret", "authorization", "connectionstring",
        "connection string", "user id=", "userid=", "uid=", "username", "email=", "apikey",
        "api-key", "api key", "cookie", "bearer ", "access_key", "access key", "private key", "sas="
    ];
    private readonly string? _logDirectory;

    public ManagerDiagnosticsService(string? logDirectory = null)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? null : Path.GetFullPath(logDirectory);
    }

    public string LogDirectory => _logDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Depot",
        "Logs");

    public ManagerDiagnosticsDocument CreateDocument(
        InstallationSnapshot snapshot,
        Version managerVersion,
        ManagerDiagnosticsContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(managerVersion);
        context ??= ManagerDiagnosticsContext.Empty;
        return new ManagerDiagnosticsDocument(
            VersionRules.VersionText(managerVersion),
            snapshot.DepotVersion is null ? "Unknown" : VersionRules.VersionText(snapshot.DepotVersion),
            context.AvailableDepotVersion,
            context.AvailableManagerVersion,
            snapshot.InstallDirectory,
            Environment.OSVersion.VersionString,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            snapshot.DatabaseProvider?.ToString() ?? "Unknown",
            snapshot.DatabaseTarget,
            snapshot.DatabaseSchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
            context.TargetDatabaseSchemaVersion,
            ReadLastSuccessfulBackup(snapshot.InstallDirectory),
            context.ReleasePublishedAt,
            context.RollbackStatus,
            snapshot.State.ToString(),
            snapshot.Message,
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    public ManagerRecoveryDiagnosticsDocument CreateRecoveryDocument(InstallationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var provider = snapshot.DatabaseProvider?.ToString() ?? "Unknown";
        var backupBoundary = snapshot.DatabaseProvider == Depot.Models.DatabaseProvider.Local
            ? "DepotManager can create verified SQLite migration-safety backups; scheduled operational backup, retention, encryption and off-host copies require the deployment DR profile."
            : "Server-provider backup, retention, encryption and off-host copies are operator-owned through provider-native infrastructure; Depot does not run a second remote backup engine.";

        return new ManagerRecoveryDiagnosticsDocument(
            provider,
            snapshot.DatabaseTarget,
            snapshot.DatabaseReachable,
            snapshot.DatabaseSchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
            ReadLastSuccessfulBackup(snapshot.InstallDirectory),
            backupBoundary,
            "Attach the validated ACTIVE deployment Disaster Recovery Profile; Depot does not embed customer ownership/RPO/RTO commitments in product settings.",
            "Attach the latest isolated restore-drill evidence. CI evidence proves the generic provider boundary only and is not a customer restore drill.",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    public string ToJson(ManagerDiagnosticsDocument diagnostics) => JsonSerializer.Serialize(diagnostics, JsonOptions);
    public string ToJson(ManagerRecoveryDiagnosticsDocument diagnostics) => JsonSerializer.Serialize(diagnostics, JsonOptions);

    public string CreateSupportPackage(
        string destinationPath,
        InstallationSnapshot snapshot,
        Version managerVersion,
        ManagerDiagnosticsContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteTextEntry(archive, "Diagnostics.json", ToJson(CreateDocument(snapshot, managerVersion, context)));
                WriteTextEntry(archive, "RecoveryReadiness.json", ToJson(CreateRecoveryDocument(snapshot)));
                WriteTextEntry(archive, "InstallationState.txt", BuildInstallationState(snapshot, context));

                var collectionWarnings = new List<string>();
                CollectSanitizedLogs(archive, collectionWarnings);
                if (collectionWarnings.Count > 0)
                {
                    WriteTextEntry(
                        archive,
                        "CollectionWarnings.txt",
                        string.Join(Environment.NewLine, collectionWarnings) + Environment.NewLine);
                }
            }
            File.Move(temporary, fullPath, true);
            return fullPath;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public static string SanitizeLog(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var builder = new StringBuilder();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (SensitiveMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("[redacted sensitive log line]");
            }
            else
            {
                builder.AppendLine(line);
            }
        }
        return builder.ToString();
    }

    private void CollectSanitizedLogs(ZipArchive archive, ICollection<string> warnings)
    {
        if (!Directory.Exists(LogDirectory)) return;

        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(LogDirectory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            warnings.Add($"Log enumeration skipped: {RecoveryFailureDiagnostics.Describe(exception)}");
            return;
        }

        foreach (var logPath in paths)
        {
            try
            {
                var sanitized = SanitizeLog(File.ReadAllText(logPath));
                WriteTextEntry(archive, $"Logs/{Path.GetFileName(logPath)}", sanitized);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Log '{Path.GetFileName(logPath)}' skipped: {RecoveryFailureDiagnostics.Describe(exception)}");
            }
        }
    }

    private static string ReadLastSuccessfulBackup(string installDirectory)
    {
        try
        {
            var repository = new SettingsRepository(Path.Combine(installDirectory, "depot.settings"));
            if (!repository.Exists()) return "Unknown";
            var backup = repository.Load().LastSuccessfulBackupUtc;
            return backup?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "Never";
        }
        catch
        {
            return "Unavailable";
        }
    }

    private static string BuildInstallationState(InstallationSnapshot snapshot, ManagerDiagnosticsContext? context)
    {
        context ??= ManagerDiagnosticsContext.Empty;
        var builder = new StringBuilder();
        builder.AppendLine($"State: {snapshot.State}");
        builder.AppendLine($"Message: {snapshot.Message}");
        builder.AppendLine($"Install directory: {snapshot.InstallDirectory}");
        builder.AppendLine($"Depot executable: {snapshot.DepotExecutablePresent}");
        builder.AppendLine($"Manager executable: {snapshot.ManagerExecutablePresent}");
        builder.AppendLine($"Settings present/readable: {snapshot.SettingsPresent}/{snapshot.SettingsReadable}");
        builder.AppendLine($"Database reachable: {snapshot.DatabaseReachable}");
        builder.AppendLine($"Database schema: {snapshot.DatabaseSchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}");
        builder.AppendLine($"Target database schema: {context.TargetDatabaseSchemaVersion}");
        builder.AppendLine($"Available Depot: {context.AvailableDepotVersion}");
        builder.AppendLine($"Available Manager: {context.AvailableManagerVersion}");
        builder.AppendLine($"Rollback: {context.RollbackStatus}");
        builder.AppendLine($"Registry consistent: {snapshot.RegistrationConsistent}");
        builder.AppendLine($"Start Menu shortcut: {snapshot.StartMenuShortcutPresent}");
        builder.AppendLine($"Desktop shortcut expected/present: {snapshot.DesktopShortcutExpected}/{snapshot.DesktopShortcutPresent}");
        return builder.ToString();
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
