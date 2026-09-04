using System.IO;

namespace DepotManager;

internal static class LocalDataDeletion
{
	public static string DefaultLocalDataDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Depot");

	public static void DeleteDirectory(string localDataDirectory)
	{
		if (string.IsNullOrWhiteSpace(localDataDirectory))
			throw new ArgumentException("A local data directory is required.", nameof(localDataDirectory));

		var fullPath = Path.GetFullPath(localDataDirectory);
		if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
	}

	public static void DeleteSqliteDatabaseFiles(string? databasePath)
	{
		if (string.IsNullOrWhiteSpace(databasePath)) return;

		var fullPath = Path.GetFullPath(databasePath);
		foreach (var path in new[] { fullPath, fullPath + "-wal", fullPath + "-shm", fullPath + "-journal" })
		{
			if (File.Exists(path)) File.Delete(path);
		}
	}
}
