using System.Diagnostics;
using System.Text.Json;

namespace DepotManager;

public sealed record RollbackCandidate(
    string ExecutablePath,
    Version Version,
    int SupportedSchemaVersion,
    DateTimeOffset CreatedUtc,
    bool IsValid,
    string Message);

public static class RollbackMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Write(string backupDirectory, Version version, int supportedSchemaVersion)
    {
        Directory.CreateDirectory(backupDirectory);
        var executablePath = Path.Combine(backupDirectory, VersionRules.BackupName(version));
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The executable rollback backup was not found.", executablePath);
        var metadataPath = Path.ChangeExtension(executablePath, ".json");
        var metadata = new RollbackMetadata
        {
            DepotVersion = VersionRules.VersionText(version),
            SupportedSchemaVersion = supportedSchemaVersion,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        return metadataPath;
    }

    public static RollbackCandidate? Read(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory)) return null;
        var executable = Directory.EnumerateFiles(backupDirectory, "Depot-*.exe").SingleOrDefault();
        if (executable is null) return null;

        try
        {
            PortableExecutableValidator.ValidateWindowsExecutable(executable);
            var text = FileVersionInfo.GetVersionInfo(executable).FileVersion;
            if (!Version.TryParse(text, out var parsed))
                return Invalid(executable, new Version(0, 0), "The rollback executable version cannot be determined.");
            var version = VersionRules.ReleaseVersion(parsed);
            var metadataPath = Path.ChangeExtension(executable, ".json");
            if (!File.Exists(metadataPath))
                return Invalid(executable, version, "Rollback metadata is missing; schema compatibility cannot be proven.");
            var metadata = JsonSerializer.Deserialize<RollbackMetadata>(File.ReadAllText(metadataPath), JsonOptions)
                ?? throw new InvalidDataException("Rollback metadata is empty.");
            if (!VersionRules.TryParseReleaseTag(metadata.DepotVersion, out var metadataVersion) || metadataVersion != version)
                return Invalid(executable, version, "Rollback metadata does not match the executable version.");
            if (metadata.SupportedSchemaVersion < 1)
                return Invalid(executable, version, "Rollback metadata contains an invalid database schema version.");
            return new RollbackCandidate(executable, version, metadata.SupportedSchemaVersion, metadata.CreatedUtc, true, "Rollback is available.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException or JsonException)
        {
            return Invalid(executable, new Version(0, 0), $"Rollback backup is invalid: {exception.Message}");
        }
    }

    public static void EnsureCompatible(RollbackCandidate candidate, int currentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsValid) throw new InvalidOperationException(candidate.Message);
        if (currentSchemaVersion != candidate.SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"Rollback unavailable because database schema {currentSchemaVersion} does not match schema {candidate.SupportedSchemaVersion} supported by Depot {VersionRules.VersionText(candidate.Version)}.");
    }

    public static void Apply(InstallationService installation, RollbackCandidate candidate, int currentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(installation);
        EnsureCompatible(candidate, currentSchemaVersion);
        installation.EnsureDepotStopped();
        InstallationService.ValidateTargetVersion(candidate.ExecutablePath, candidate.Version);
        ExecutableDeployment.Replace(candidate.ExecutablePath, installation.DepotPath);
        installation.RegisterInstalledApp(candidate.Version);
    }

    private static RollbackCandidate Invalid(string executable, Version version, string message) =>
        new(executable, version, 0, DateTimeOffset.MinValue, false, message);

    private sealed class RollbackMetadata
    {
        public string DepotVersion { get; set; } = string.Empty;
        public int SupportedSchemaVersion { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
    }
}
