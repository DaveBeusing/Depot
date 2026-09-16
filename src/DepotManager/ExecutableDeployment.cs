using System.IO;
using System.Threading;

namespace DepotManager;

public static class ExecutableDeployment
{
	private static readonly int[] ReplacementRetryDelaysMilliseconds = [50, 100, 200, 400, 800, 1000];

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
		try { MoveStagedExecutable(staged, depotPath); }
		catch
		{
			if (File.Exists(staged)) File.Delete(staged);
			throw;
		}
	}

	public static void InstallManagerCopy(string sourceManagerPath, string installedManagerPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceManagerPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(installedManagerPath);

		var source = Path.GetFullPath(sourceManagerPath);
		var target = Path.GetFullPath(installedManagerPath);
		if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return;
		if (!File.Exists(source)) throw new FileNotFoundException("The running Depot Manager executable could not be found.", source);

		var targetDirectory = Path.GetDirectoryName(target)
			?? throw new InvalidOperationException("The Depot Manager installation directory is invalid.");
		Directory.CreateDirectory(targetDirectory);
		Replace(source, target);

		if (!File.Exists(target))
			throw new IOException("Depot Manager could not be copied into the Depot installation directory.");
	}

	private static void MoveStagedExecutable(string staged, string depotPath)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				File.Move(staged, depotPath, true);
				return;
			}
			catch (Exception ex) when (IsRetryableReplacementFailure(ex) && attempt < ReplacementRetryDelaysMilliseconds.Length)
			{
				Thread.Sleep(ReplacementRetryDelaysMilliseconds[attempt]);
			}
		}
	}

	private static bool IsRetryableReplacementFailure(Exception exception) =>
		exception is IOException or UnauthorizedAccessException;
}
