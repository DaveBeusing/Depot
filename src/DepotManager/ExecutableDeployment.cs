using System.IO;

namespace DepotManager;

public static class ExecutableDeployment
{
	public static void BackupCurrent(string depotPath, string backupDirectory, Version currentVersion)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(depotPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
		ArgumentNullException.ThrowIfNull(currentVersion);
		Directory.CreateDirectory(backupDirectory);
		foreach (var oldBackup in Directory.EnumerateFiles(backupDirectory, "Depot-*.exe")) File.Delete(oldBackup);
		File.Copy(depotPath, Path.Combine(backupDirectory, VersionRules.BackupName(currentVersion)), true);
	}

	public static void Replace(string downloadedFile, string depotPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(downloadedFile);
		ArgumentException.ThrowIfNullOrWhiteSpace(depotPath);
		var staged = depotPath + ".new";
		File.Copy(downloadedFile, staged, true);
		try { File.Move(staged, depotPath, true); }
		catch
		{
			if (File.Exists(staged)) File.Delete(staged);
			throw;
		}
	}
}
