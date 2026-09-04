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
}
