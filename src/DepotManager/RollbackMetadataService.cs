using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using Depot.Models;

namespace DepotManager;

public sealed record RollbackCandidate(
    string ExecutablePath,
    Version Version,
    int SupportedSchemaVersion,
    DateTimeOffset CreatedUtc,
    bool IsValid,
    string Message,
    DatabaseSchemaCompatibility? Compatibility = null,
    string? ExecutableSha256 = null);

public static class RollbackMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string Write(string backupDirectory, Version version, DatabaseSchemaCompatibility compatibility)
    {
        RequireObservation(compatibility);
        Directory.CreateDirectory(backupDirectory);
        var executablePath = Path.Combine(backupDirectory, VersionRules.BackupName(version));
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The executable rollback backup was not found.", executablePath);
        var metadataPath = Path.ChangeExtension(executablePath, ".json");
        var metadata = new RollbackMetadata
        {
            FormatVersion = 2,
            DepotVersion = VersionRules.VersionText(version),
            Compatibility = compatibility,
            ExecutableSha256 = Hash(executablePath),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
        return metadataPath;
    }

    public static RollbackCandidate? Read(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory)) return null;
        var executables = Directory.EnumerateFiles(backupDirectory, "Depot-*.exe").ToArray();
        if (executables.Length == 0) return null;
        if (executables.Length != 1)
            return Invalid(executables[0], new Version(0, 0), "Rollback is disabled because more than one executable backup exists.");
        var executable = executables[0];
        try
        {
            PortableExecutableValidator.ValidateWindowsExecutable(executable);
            var text = FileVersionInfo.GetVersionInfo(executable).FileVersion;
            if (!Version.TryParse(text, out var parsed))
                return Invalid(executable, new Version(0, 0), "The rollback executable version cannot be determined.");
            var version = VersionRules.ReleaseVersion(parsed);
            var metadataPath = Path.ChangeExtension(executable, ".json");
            if (!File.Exists(metadataPath))
                return Invalid(executable, version, "Rollback metadata is missing; complete schema compatibility cannot be proven.");
            var metadata = JsonSerializer.Deserialize<RollbackMetadata>(File.ReadAllText(metadataPath), JsonOptions)
                ?? throw new InvalidDataException("Rollback metadata is empty.");
            if (!VersionRules.TryParseReleaseTag(metadata.DepotVersion, out var metadataVersion) || metadataVersion != version)
                return Invalid(executable, version, "Rollback metadata does not match the executable version.");
            if (metadata.FormatVersion != 2 || metadata.Compatibility is null)
                return Invalid(executable, version, "Legacy Core-only rollback metadata is unsupported. Restore a verified database/executable pair through the documented recovery procedure.");
            RequireObservation(metadata.Compatibility);
            if (!string.Equals(Hash(executable), metadata.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return Invalid(executable, version, "The rollback executable no longer matches the recorded compatibility evidence.");
            return new RollbackCandidate(executable, version, metadata.Compatibility.CoreVersion, metadata.CreatedUtc, true,
                "Rollback requires an unchanged complete database compatibility state.", metadata.Compatibility, metadata.ExecutableSha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException or JsonException)
        {
            return Invalid(executable, new Version(0, 0), $"Rollback backup is invalid: {exception.Message}");
        }
    }

    public static void EnsureCompatible(RollbackCandidate candidate, DatabaseSchemaCompatibility current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsValid) throw new InvalidOperationException(candidate.Message);
        if (!RollbackCompatibility.IsCompatible(current, candidate.Compatibility))
            throw new InvalidOperationException("Rollback unavailable because the live core, feature or structural schema differs from the recorded pre-update state. Database downgrade is never automatic.");
    }

    public static async Task RestoreExecutableAsync(InstallationService installation, RollbackCandidate candidate,
        DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        installation.EnsureDepotStopped();
        // Never trust a cached UI snapshot or a caller-supplied schema integer at this boundary.
        var current = await new DatabaseSchemaInspector().ReadStableCompatibilityAsync(settings, cancellationToken);
        EnsureCompatible(candidate, current);
        if (!string.Equals(Hash(candidate.ExecutablePath), candidate.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The rollback executable changed after compatibility inspection.");
        InstallationService.ValidateTargetVersion(candidate.ExecutablePath, candidate.Version);
        cancellationToken.ThrowIfCancellationRequested();
        installation.EnsureDepotStopped();
        ExecutableDeployment.Replace(candidate.ExecutablePath, installation.DepotPath);
        InstalledSchemaContract.Delete(installation.InstallDirectory);
    }

    public static async Task ApplyAsync(InstallationService installation, RollbackCandidate candidate,
        DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        await RestoreExecutableAsync(installation, candidate, settings, cancellationToken);
        installation.RegisterInstalledApp(candidate.Version);
    }

    internal static void RequireObservation(DatabaseSchemaCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        compatibility.Validate();
        if (compatibility.Provider is null || compatibility.TableDefinitions.Count != compatibility.Tables.Count ||
            compatibility.Tables.Keys.Any(table => !compatibility.TableDefinitions.ContainsKey(table)) ||
            compatibility.Indexes.Values.SelectMany(indexes => indexes).Any(index => string.IsNullOrEmpty(index.Definition)))
            throw new InvalidDataException("Rollback requires a complete live schema observation, including structural definitions.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static RollbackCandidate Invalid(string executable, Version version, string message) =>
        new(executable, version, 0, DateTimeOffset.MinValue, false, message);

    private sealed class RollbackMetadata
    {
        public int FormatVersion { get; set; }
        public string DepotVersion { get; set; } = string.Empty;
        public DatabaseSchemaCompatibility? Compatibility { get; set; }
        public string? ExecutableSha256 { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
    }
}
