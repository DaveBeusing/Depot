// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Services;

using Xunit;

namespace Depot.Tests;

public sealed class BackupRetentionPolicyTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"depot-retention-{Guid.NewGuid():N}");

	[Fact]
	public void PruneKeepsTenNewestAndDoesNotTouchManualOrSafetyBackups()
	{
		Directory.CreateDirectory(_directory);
		var now = DateTime.UtcNow;
		for (var index = 0; index < 12; index++)
		{
			var path = Path.Combine(_directory, $"Depot-Auto-{index:00}.depotbackup");
			File.WriteAllText(path, "backup");
			File.SetLastWriteTimeUtc(path, now.AddDays(-40).AddMinutes(index));
		}
		var manual = Path.Combine(_directory, "Depot-Manual.depotbackup");
		var safety = Path.Combine(_directory, "Depot-Safety-20260101-000000.depotbackup");
		File.WriteAllText(manual, "backup");
		File.WriteAllText(safety, "backup");

		var deleted = BackupRetentionPolicy.Prune(_directory, now);

		Assert.Equal(2, deleted.Count);
		Assert.Equal(10, Directory.GetFiles(_directory, "Depot-Auto-*.depotbackup").Length);
		Assert.True(File.Exists(manual));
		Assert.True(File.Exists(safety));
	}

	public void Dispose()
	{
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}
}
