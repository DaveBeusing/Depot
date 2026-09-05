using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DepotManager;

public sealed record ManagerDiagnosticsDocument(
    string DepotManagerVersion,
    string DepotVersion,
    string InstallDirectory,
    string OperatingSystem,
    string ProcessArchitecture,
    string DatabaseProvider,
    string DatabaseTarget,
    string DatabaseSchemaVersion,
    string InstallationHealth,
    string InstallationMessage,
    string GeneratedUtc);

public sealed class ManagerDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] SensitiveMarkers =
    [
        "password", "passwd", "pwd=", "token", "secret", "authorization", "connectionstring",
        "connection string", "user id=", "userid=", "uid=", "username", "email="
    ];

    public string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot", "Logs");

    public ManagerDiagnosticsDocument CreateDocument(InstallationSnapshot snapshot, Version runningManagerVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ManagerDiagnosticsDocument(
            VersionRules.VersionText(runningManagerVersion),
            snapshot.DepotVersion is null ? "Unknown" : VersionRules.VersionText(snapshot.DepotVersion),
            snapshot.InstallDirectory,
            Environment.OSVersion.VersionString,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            snapshot.DatabaseProvider?.ToString() ?? "Unknown",
            snapshot.DatabaseTarget,
            snapshot.DatabaseSchemaVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown",
            snapshot.State.ToString(),
            snapshot.Message,
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    public string ToJson(ManagerDiagnosticsDocument document) => JsonSerializer.Serialize(document, JsonOptions);

    public string CreateSupportPackage(string destinationPath, InstallationSnapshot snapshot, Version runningManagerVersion)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var document = CreateDocument(snapshot, runningManagerVersion);
                WriteText(archive, "Diagnostics.json", ToJson(document));
                WriteText(archive, "InstallationState.txt", BuildStateText(snapshot));
                if (Directory.Exists(LogDirectory))
                {
                    foreach (var log in Directory.EnumerateFiles(LogDirectory, "*.log")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .Take(6))
                    {
                        var sanitized = SanitizeLog(File.ReadAllText(log));
                        WriteText(archive, $"Logs/{Path.GetFileName(log)}", sanitized);
                    }
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

    public static string SanitizeLog(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var builder = new StringBuilder();
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var sensitive = SensitiveMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
            builder.AppendLine(sensitive ? "[redacted sensitive log line]" : line);
        }
        return builder.ToString();
    }

    private static void WriteText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildStateText(InstallationSnapshot snapshot) =>
        $"State: {snapshot.State}{Environment.NewLine}" +
        $"Message: {snapshot.Message}{Environment.NewLine}" +
        $"Depot executable: {snapshot.DepotExecutablePresent}{Environment.NewLine}" +
        $"Manager executable: {snapshot.ManagerExecutablePresent}{Environment.NewLine}" +
        $"Settings present/readable: {snapshot.SettingsPresent}/{snapshot.SettingsReadable}{Environment.NewLine}" +
        $"Database reachable: {snapshot.DatabaseReachable}{Environment.NewLine}" +
        $"Registry consistent: {snapshot.RegistrationConsistent}{Environment.NewLine}" +
        $"Start Menu shortcut: {snapshot.StartMenuShortcutPresent}{Environment.NewLine}" +
        $"Desktop shortcut expected/present: {snapshot.DesktopShortcutExpected}/{snapshot.DesktopShortcutPresent}{Environment.NewLine}";
}
