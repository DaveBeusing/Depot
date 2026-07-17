// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseManagementTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"depot-management-{Guid.NewGuid():N}");
	private readonly string _databasePath;
	private readonly string _settingsPath;
	private readonly SettingsService _settingsService;
	private readonly DatabaseManagementService _service;
	private readonly ItemRepository _items;

	public DatabaseManagementTests()
	{
		Directory.CreateDirectory(_directory);
		_databasePath = Path.Combine(_directory, "depot.db");
		_settingsPath = Path.Combine(_directory, "depot.settings");
		var settingsRepository = new SettingsRepository(_settingsPath);
		_settingsService = new SettingsService(settingsRepository);
		_settingsService.LoadOrCreate();
		_settingsService.Save(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.Local,
			LocalDatabasePath = _databasePath,
			BackupDirectory = Path.Combine(_directory, "Backups")
		});
		var factory = new SqliteConnectionFactory(_databasePath);
		new DepotDatabase(factory).Initialize();
		var access = new DatabaseAccess(factory);
		_items = new ItemRepository(access);
		_service = new DatabaseManagementService(factory, _settingsService);
	}

	[Fact]
	public async Task BackupIsValidatedAndRestoreReplacesCurrentRecords()
	{
		var preserved = new Item { PartNumber = "BACKUP-1", Description = "Preserved", IsActive = true };
		preserved.Id = await _items.CreateAsync(preserved, CancellationToken.None);
		var backupPath = Path.Combine(_directory, "manual.depotbackup");
		var backup = await _service.CreateBackupAsync(backupPath);

		Assert.True(backup.IsValid);
		Assert.True(File.Exists(backupPath));

		var discarded = new Item { PartNumber = "AFTER-1", Description = "Discarded", IsActive = true };
		discarded.Id = await _items.CreateAsync(discarded, CancellationToken.None);
		var safetyPath = await _service.RestoreBackupAsync(backupPath);

		Assert.NotNull(await _items.GetByPartNumberAsync("BACKUP-1", CancellationToken.None));
		Assert.Null(await _items.GetByPartNumberAsync("AFTER-1", CancellationToken.None));
		Assert.True(File.Exists(safetyPath));
	}

	[Fact]
	public async Task CorruptedBackupIsRejectedBeforeRestore()
	{
		var backupPath = Path.Combine(_directory, "damaged.depotbackup");
		await File.WriteAllTextAsync(backupPath, "not a depot backup");

		var validation = await _service.ValidateBackupAsync(backupPath);

		Assert.False(validation.IsValid);
		Assert.Contains("damaged", validation.ValidationMessage, StringComparison.OrdinalIgnoreCase);
		await Assert.ThrowsAsync<InvalidDataException>(() => _service.RestoreBackupAsync(backupPath));
	}

	[Fact]
	public async Task ScheduledBackupPersistsCompletionAndSurvivesSettingsReload()
	{
		var settings = _settingsService.CurrentSettings;
		settings.AutomaticBackupsEnabled = true;
		settings.BackupIntervalDays = 1;
		settings.LastSuccessfulBackupUtc = null;
		_settingsService.Save(settings);
		using var scheduler = new DatabaseBackupScheduler(_service, _settingsService);

		Assert.True(await scheduler.RunDueBackupAsync());
		Assert.NotNull(_settingsService.CurrentSettings.LastSuccessfulBackupUtc);
		Assert.Single(Directory.EnumerateFiles(settings.BackupDirectory, "*.depotbackup"));

		var reloaded = new SettingsService(new SettingsRepository(_settingsPath));
		var persisted = reloaded.LoadOrCreate();
		Assert.True(persisted.AutomaticBackupsEnabled);
		Assert.Equal(1, persisted.BackupIntervalDays);
		Assert.NotNull(persisted.LastSuccessfulBackupUtc);
	}

	[Fact]
	public async Task OverviewIntegrityAndCompactWorkForSqlite()
	{
		var integrity = await _service.CheckIntegrityAsync();
		await _service.CompactAsync();
		var overview = await _service.GetOverviewAsync();

		Assert.True(integrity.IsValid);
		Assert.Equal(DatabaseProvider.Local, overview.Provider);
		Assert.Equal(DatabaseVersion.CurrentVersion, overview.SchemaVersion);
		Assert.True(overview.SizeBytes > 0);
		Assert.DoesNotContain("Password", overview.ConnectionDisplayName, StringComparison.OrdinalIgnoreCase);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}
}
