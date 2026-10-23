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
		DatabaseProvisioningService.Initialize(factory);
		var authorization = new AuthorizationService();
		authorization.SignIn(new User { Id = 1, Email = "recovery-tests@depot.local", IsActive = true }, [ApplicationPermission.DatabaseManage]);
		_service = new DatabaseManagementService(factory, settingsService, authorization);
	}

	[Fact]
	public async Task CancelledBackupStopsWithoutLeavingTemporaryArchive()
	{
		var target = Path.Combine(_directory, "cancelled.depotbackup");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CreateBackupAsync(target, cancellationToken: cancellation.Token));
		Assert.False(File.Exists(target));
		AssertNoTemporaryArchives();
	}

	[Theory]
	[InlineData(10)]
	[InlineData(80)]
	public async Task CancellationDuringBackupPreservesExistingArchiveAndRemovesTemporaryFiles(int cancelAtPercentage)
	{
		var target = Path.Combine(_directory, "existing.depotbackup");
		var original = new byte[] { 1, 2, 3, 4 };
		await File.WriteAllBytesAsync(target, original);
		using var cancellation = new CancellationTokenSource();
		var progress = new CancellingProgress(cancellation, cancelAtPercentage);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CreateBackupAsync(target, progress, cancellation.Token));

		Assert.Equal(original, await File.ReadAllBytesAsync(target));
		AssertNoTemporaryArchives();
	}

	[Fact]
	public async Task BackupToUnavailableDirectoryFailsClosedWithoutPartialArchive()
	{
		var blockingFile = Path.Combine(_directory, "not-a-directory");
		await File.WriteAllTextAsync(blockingFile, "blocking file");
		var target = Path.Combine(blockingFile, "backup.depotbackup");

		await Assert.ThrowsAnyAsync<Exception>(() => _service.CreateBackupAsync(target));
		Assert.False(File.Exists(target));
		AssertNoTemporaryArchives();
	}

	private void AssertNoTemporaryArchives() =>
		Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp*", SearchOption.AllDirectories));

	private sealed class CancellingProgress(CancellationTokenSource cancellation, int cancelAtPercentage) : IProgress<DatabaseOperationProgress>
	{
		public void Report(DatabaseOperationProgress value)
		{
			if (value.Percentage >= cancelAtPercentage) cancellation.Cancel();
		}
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}
}

