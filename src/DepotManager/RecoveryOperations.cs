using System.Security.Cryptography;

using Microsoft.Data.Sqlite;

namespace DepotManager;

public sealed record RecoveryDrillEvidence(
    string Provider,
    string Result,
    int DatabaseSchemaVersion,
    string BackupSha256,
    string RestoredSha256,
    long BackupSizeBytes,
    string StartedUtc,
    string CompletedUtc,
    double DurationSeconds);

public static class RecoveryFailureDiagnostics
{
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    public static string Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnauthorizedAccessException => "AccessDenied",
            FileNotFoundException or DirectoryNotFoundException => "MissingResource",
            IOException ioException when ioException.HResult is ErrorDiskFull or ErrorHandleDiskFull => "DiskFull",
            IOException => "FileSystemIo",
            SqliteException sqliteException when sqliteException.SqliteErrorCode is 5 or 6 => "DatabaseLocked",
            _ => "Unknown"
        };
    }

    public static string Describe(Exception exception) => Classify(exception) switch
    {
        "AccessDenied" => "Access denied. Verify NTFS/share ACLs and the service or operator identity before retrying recovery.",
        "MissingResource" => "A required backup, directory, or recovery resource is missing. Verify the selected evidence and restore source.",
        "DiskFull" => "The recovery target does not have sufficient free space. Free or extend capacity before retrying.",
        "DatabaseLocked" => "The database is locked by another writer. Stop conflicting application activity before retrying recovery.",
        "FileSystemIo" => "A filesystem I/O failure occurred. Check file locks, storage health, free space, and path availability before retrying.",
        _ => "Recovery failed for an unclassified reason. Preserve the sanitized support package and escalate with the retained recovery evidence."
    };
}

public sealed class SqliteRecoveryDrillService
{
    public async Task<RecoveryDrillEvidence> ValidateBackupAsync(
        string backupPath,
        string isolatedRestoreDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(isolatedRestoreDirectory);

        var started = DateTimeOffset.UtcNow;
        var sourcePath = Path.GetFullPath(backupPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The SQLite backup selected for the recovery drill was not found.", sourcePath);

        var restoreRoot = Path.GetFullPath(isolatedRestoreDirectory);
        EnsureWritableDirectory(restoreRoot);
        var restorePath = Path.Combine(restoreRoot, $"Depot-recovery-drill-{Guid.NewGuid():N}.db");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePath, restorePath, overwrite: false);

            var sourceHash = await HashAsync(sourcePath, cancellationToken);
            var restoredHash = await HashAsync(restorePath, cancellationToken);
            if (!sourceHash.Equals(restoredHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The isolated SQLite restore copy does not match the selected backup SHA-256 hash.");

            var validationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = restorePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            await using var validation = new SqliteConnection(validationBuilder.ConnectionString);
            await validation.OpenAsync(cancellationToken);

            await using (var integrity = validation.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                var result = Convert.ToString(
                    await integrity.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The isolated SQLite restore failed integrity validation: {result}");
            }

            int schemaVersion;
            await using (var schema = validation.CreateCommand())
            {
                schema.CommandText = "SELECT Version FROM DatabaseInfo WHERE Id = 1;";
                var value = await schema.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The restored database does not contain Depot schema metadata.");
                schemaVersion = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                if (schemaVersion <= 0)
                    throw new InvalidOperationException("The restored database contains an invalid Depot schema version.");
            }

            await validation.CloseAsync();
            var completed = DateTimeOffset.UtcNow;
            return new RecoveryDrillEvidence(
                "SQLite",
                "PASS",
                schemaVersion,
                sourceHash,
                restoredHash,
                new FileInfo(sourcePath).Length,
                started.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                completed.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                Math.Round((completed - started).TotalSeconds, 3));
        }
        finally
        {
            try
            {
                if (File.Exists(restorePath)) File.Delete(restorePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void EnsureWritableDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        var probe = Path.Combine(fullPath, $".depot-recovery-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0x44);
            stream.Flush(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(RecoveryFailureDiagnostics.Describe(exception), exception);
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
