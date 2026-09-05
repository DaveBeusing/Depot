using Depot.Models;
using Microsoft.Data.Sqlite;

namespace DepotManager;

public sealed class MigrationSafetyService
{
    public async Task<string> CreateSqliteSafetyBackupAsync(
        DatabaseConnectionSettings settings,
        string installDirectory,
        Version depotVersion,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Provider != DatabaseProvider.Local)
            throw new InvalidOperationException("Automatic migration safety backups are available only for local SQLite databases.");

        var sourcePath = Path.GetFullPath(settings.LocalDatabasePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The configured SQLite database was not found.", sourcePath);

        var backupDirectory = Path.Combine(Path.GetFullPath(installDirectory), "Backups", "Database");
        Directory.CreateDirectory(backupDirectory);
        var versionText = VersionRules.VersionText(depotVersion);
        var backupPath = Path.Combine(
            backupDirectory,
            $"Depot-{versionText}-Schema{schemaVersion}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");

        cancellationToken.ThrowIfCancellationRequested();
        var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadWrite };
        var destinationBuilder = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate };
        await using var source = new SqliteConnection(sourceBuilder.ConnectionString);
        await using var destination = new SqliteConnection(destinationBuilder.ConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
        await destination.CloseAsync();
        await source.CloseAsync();

        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
            throw new InvalidOperationException("The SQLite migration safety backup could not be validated.");

        await using var validation = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
        await validation.OpenAsync(cancellationToken);
        await using var command = validation.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The SQLite migration safety backup failed integrity validation: {result}");

        return backupPath;
    }
}
