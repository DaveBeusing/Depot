// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class RecoveryFailureTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"depot-recovery-failure-{Guid.NewGuid():N}");
	private readonly DatabaseManagementService _service;

	public RecoveryFailureTests()
	{
		Directory.CreateDirectory(_directory);
		var databasePath = Path.Combine(_directory, "depot.db");
		var settingsService = new SettingsService(new SettingsRepository(Path.Combine(_directory, "depot.settings")));
		settingsService.LoadOrCreate();
		settingsService.Save(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.Local,
			LocalDatabasePath = databasePath,
			BackupDirectory = Path.Combine(_directory, "Backups")
		});
		var factory = new SqliteConnectionFactory(databasePath);
		new DepotDatabase(factory).Initialize();
		_service = new DatabaseManagementService(factory, settingsService);
	}

	[Fact]
	public async Task CancelledBackupStopsWithoutLeavingTemporaryArchive()
	{
		var target = Path.Combine(_directory, "cancelled.depotbackup");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CreateBackupAsync(target, cancellationToken: cancellation.Token));
		Assert.False(File.Exists(target));
		Assert.False(File.Exists(target + ".tmp"));
	}

	[Fact]
	public async Task BackupToUnavailableDirectoryFailsClosedWithoutPartialArchive()
	{
		var blockingFile = Path.Combine(_directory, "not-a-directory");
		await File.WriteAllTextAsync(blockingFile, "blocking file");
		var target = Path.Combine(blockingFile, "backup.depotbackup");

		await Assert.ThrowsAnyAsync<Exception>(() => _service.CreateBackupAsync(target));
		Assert.False(File.Exists(target));
		Assert.False(File.Exists(target + ".tmp"));
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}
}
