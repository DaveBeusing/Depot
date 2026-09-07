namespace DepotManager;

public static class MigrationBackupGate
{
    public static void EnsureTargetSchemaIsNotOlder(int currentSchemaVersion, int targetSchemaVersion)
    {
        if (targetSchemaVersion < currentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Update blocked because release schema {targetSchemaVersion} is older than database schema {currentSchemaVersion}.");
        }
    }

    public static string EnsureVerifiedSqliteBackup(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var fullPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length <= 0)
            throw new InvalidOperationException("Database migration is blocked because the verified SQLite safety backup is unavailable.");
        return fullPath;
    }

    public static void EnsureExternalBackupConfirmed(bool confirmed)
    {
        if (!confirmed)
            throw new InvalidOperationException("Database migration is blocked until a current server-side backup is explicitly confirmed.");
    }
}
