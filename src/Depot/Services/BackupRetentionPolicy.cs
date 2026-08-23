// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Services;

public static class BackupRetentionPolicy
{
	public const int MinimumAutomaticBackupsToKeep = 10;
	public static readonly TimeSpan MaximumAutomaticBackupAge = TimeSpan.FromDays(30);

	public static IReadOnlyList<string> Prune(string directory, DateTime utcNow)
	{
		if (!Directory.Exists(directory)) return [];
		var files = new DirectoryInfo(directory)
			.GetFiles("Depot-Auto-*.depotbackup", SearchOption.TopDirectoryOnly)
			.OrderByDescending(file => file.LastWriteTimeUtc)
			.ToArray();
		var deleted = new List<string>();
		foreach (var file in files.Skip(MinimumAutomaticBackupsToKeep))
		{
			if (utcNow - file.LastWriteTimeUtc <= MaximumAutomaticBackupAge) continue;
			file.Delete();
			deleted.Add(file.FullName);
		}
		return deleted;
	}
}
