// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;
using Depot.Services;
using Depot.ViewModels.Administration;

using Microsoft.Data.Sqlite;

using Xunit;

namespace Depot.Tests;

public sealed class DatabaseManagementAuthorizationTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"depot-database-authorization-{Guid.NewGuid():N}");
	private readonly string _databasePath;
	private readonly string _settingsPath;
	private readonly SettingsService _settings;
	private readonly SqliteConnectionFactory _factory;
	private readonly AuthorizationService _authorization = new();
	private readonly DatabaseManagementService _service;

	public DatabaseManagementAuthorizationTests()
	{
		Directory.CreateDirectory(_directory);
		_databasePath = Path.Combine(_directory, "depot.db");
		_settingsPath = Path.Combine(_directory, "depot.settings");
		_settings = new SettingsService(new SettingsRepository(_settingsPath));
		_settings.LoadOrCreate();
		_settings.Save(new DatabaseConnectionSettings
		{
			Provider = DatabaseProvider.Local,
			LocalDatabasePath = _databasePath,
			BackupDirectory = Path.Combine(_directory, "Backups")
		});
		_factory = new SqliteConnectionFactory(_databasePath);
		_service = new DatabaseManagementService(_factory, _settings, _authorization);
	}

	[Theory]
	[InlineData(AccessMode.SignedOut)]
	[InlineData(AccessMode.ViewOnly)]
	[InlineData(AccessMode.UnrelatedPermission)]
	[InlineData(AccessMode.InactiveManager)]
	public async Task ManualMutationsRequireDatabaseManageBeforeAnySideEffect(AccessMode mode)
	{
		SetAccess(mode);
		var originalSettings = await File.ReadAllBytesAsync(_settingsPath);
		var backupDirectory = Path.Combine(_directory, "unauthorized-backup");
		var target = Path.Combine(backupDirectory, "backup.depotbackup");
		var candidate = _settings.CurrentSettings;
		candidate.LocalDatabasePath = Path.Combine(_directory, "unauthorized.db");
		candidate.AutomaticBackupsEnabled = true;
		candidate.BackupDirectory = backupDirectory;

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateBackupAsync(target));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RestoreBackupAsync(target));
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CompactAsync());
		Assert.Throws<UnauthorizedAccessException>(() => _service.SaveSettings(candidate));

		Assert.False(File.Exists(_databasePath));
		Assert.False(Directory.Exists(backupDirectory));
		Assert.False(File.Exists(target + ".tmp"));
		Assert.Equal(originalSettings, await File.ReadAllBytesAsync(_settingsPath));
		Assert.Equal(_databasePath, _settings.CurrentSettings.LocalDatabasePath);
		Assert.False(_settings.CurrentSettings.AutomaticBackupsEnabled);
		Assert.Null(_settings.CurrentSettings.LastSuccessfulBackupUtc);
	}

	[Theory]
	[InlineData(AccessMode.SignedOut)]
	[InlineData(AccessMode.UnrelatedPermission)]
	[InlineData(AccessMode.InactiveManager)]
	public async Task DatabaseReadsRequireViewOrManagePermission(AccessMode mode)
	{
		SetAccess(mode);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetOverviewAsync());
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetBackupsAsync());
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CheckIntegrityAsync());
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ValidateBackupAsync(Path.Combine(_directory, "missing.depotbackup")));

		Assert.False(File.Exists(_databasePath));
		Assert.False(Directory.Exists(_settings.CurrentSettings.BackupDirectory));
	}

	[Fact]
	public async Task ViewOnlyUserCanInspectDatabaseWithoutChangingConfiguration()
	{
		DatabaseProvisioningService.Initialize(_factory);
		SetAccess(AccessMode.ViewOnly);
		var originalSettings = await File.ReadAllBytesAsync(_settingsPath);

		var overview = await _service.GetOverviewAsync();
		var backups = await _service.GetBackupsAsync();
		var integrity = await _service.CheckIntegrityAsync();
		var missingBackup = await _service.ValidateBackupAsync(Path.Combine(_directory, "missing.depotbackup"));

		Assert.True(_service.CanView);
		Assert.False(_service.CanManage);
		Assert.Equal(DatabaseProvider.Local, overview.Provider);
		Assert.True(integrity.IsValid, integrity.Message);
		Assert.Empty(backups);
		Assert.False(missingBackup.IsValid);
		Assert.Equal(originalSettings, await File.ReadAllBytesAsync(_settingsPath));
		Assert.Contains(AdministrationViewModel.CreateNavigationItems(_authorization), item => item.Section is AdministrationSection.Database);
	}

	[Fact]
	public async Task ManagerCanSaveCreateRestoreAndCompactWithoutSeparateViewGrant()
	{
		DatabaseProvisioningService.Initialize(_factory);
		SignIn(ApplicationPermission.DatabaseManage);
		var candidate = _settings.CurrentSettings;
		candidate.BackupIntervalDays = 2;

		var saved = _service.SaveSettings(candidate);
		var backup = await _service.CreateBackupAsync(Path.Combine(_directory, "manual.depotbackup"));
		var safetyBackup = await _service.RestoreBackupAsync(backup.FilePath);
		await _service.CompactAsync();

		Assert.Equal(2, saved.BackupIntervalDays);
		Assert.Equal(2, new SettingsRepository(_settingsPath).Load().BackupIntervalDays);
		Assert.True(backup.IsValid, backup.ValidationMessage);
		Assert.True(File.Exists(safetyBackup));
		Assert.True((await _service.CheckIntegrityAsync()).IsValid);
	}

	[Fact]
	public async Task ViewOnlyCommandsLeaveMutationDialogsAndDraftConnectionUntouched()
	{
		DatabaseProvisioningService.Initialize(_factory);
		SetAccess(AccessMode.ViewOnly);
		var dialogs = new CountingDialogs();
		using var model = CreateViewModel(dialogs);
		var draftPath = Path.Combine(_directory, "unapproved-draft.db");
		model.LocalDatabasePath = draftPath;
		var originalSettings = await File.ReadAllBytesAsync(_settingsPath);

		Assert.False(model.CanEditSettings);
		Assert.False(model.SaveCommand.CanExecute(null));
		Assert.False(model.CreateBackupCommand.CanExecute(null));
		Assert.False(model.RestoreBackupCommand.CanExecute(null));
		Assert.False(model.CompactCommand.CanExecute(null));
		Assert.False(model.TestConnectionCommand.CanExecute(null));
		Assert.True(model.CheckIntegrityCommand.CanExecute(null));
		Assert.True(model.ValidateBackupCommand.CanExecute(null));
		Assert.True(model.RefreshCommand.CanExecute(null));

		await model.SaveCommand.ExecuteAsync();
		await model.CreateBackupCommand.ExecuteAsync();
		await model.RestoreBackupCommand.ExecuteAsync();
		await model.CompactCommand.ExecuteAsync();
		await model.TestConnectionCommand.ExecuteAsync();
		await model.RefreshCommand.ExecuteAsync();

		Assert.Equal(0, dialogs.Calls);
		Assert.False(model.HasError);
		Assert.False(File.Exists(draftPath));
		Assert.Equal(originalSettings, await File.ReadAllBytesAsync(_settingsPath));
	}

	[Fact]
	public void CommandsRefreshWhenAuthorizationChangesAndUnsubscribeOnDispose()
	{
		SetAccess(AccessMode.ViewOnly);
		var model = CreateViewModel(new CountingDialogs());
		var commandChanges = 0;
		var editabilityChanges = 0;
		model.SaveCommand.CanExecuteChanged += (_, _) => commandChanges++;
		model.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(model.CanEditSettings)) editabilityChanges++;
		};

		SignIn(ApplicationPermission.DatabaseManage);
		Assert.True(model.SaveCommand.CanExecute(null));
		Assert.True(model.TestConnectionCommand.CanExecute(null));
		Assert.True(model.CanEditSettings);
		Assert.True(commandChanges > 0);
		Assert.True(editabilityChanges > 0);

		_authorization.SignOut();
		Assert.False(model.SaveCommand.CanExecute(null));
		Assert.False(model.RefreshCommand.CanExecute(null));
		Assert.False(model.CanEditSettings);

		model.Dispose();
		var changesAfterDisposal = commandChanges;
		SignIn(ApplicationPermission.DatabaseManage);
		Assert.Equal(changesAfterDisposal, commandChanges);
		Assert.False(model.SaveCommand.CanExecute(null));
		Assert.False(model.CanEditSettings);
		model.Dispose();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task SessionEndingBeforeConnectionCommandExecutionIsHandledWithoutSideEffects(bool testConnection)
	{
		SignIn(ApplicationPermission.DatabaseManage);
		using var model = CreateViewModel(new CountingDialogs());
		model.LocalDatabasePath = Path.Combine(_directory, "unapproved-draft.db");
		var originalSettings = await File.ReadAllBytesAsync(_settingsPath);
		var command = testConnection ? model.TestConnectionCommand : model.SaveCommand;
		var endSession = true;
		command.CanExecuteChanged += (_, _) =>
		{
			if (!endSession) return;
			endSession = false;
			_authorization.SignOut();
		};

		await command.ExecuteAsync();

		Assert.True(model.HasError);
		Assert.False(model.IsBusy);
		Assert.False(File.Exists(model.LocalDatabasePath));
		Assert.False(File.Exists(_databasePath));
		Assert.Equal(originalSettings, await File.ReadAllBytesAsync(_settingsPath));
	}

	[Fact]
	public void BackgroundAuthorizationChangesRefreshOnTheCapturedUiContext()
	{
		SignIn(ApplicationPermission.DatabaseManage);
		var originalContext = SynchronizationContext.Current;
		var context = new QueuedSynchronizationContext();
		DatabaseSettingsViewModel? model = null;
		try
		{
			SynchronizationContext.SetSynchronizationContext(context);
			model = CreateViewModel(new CountingDialogs());
			var commandChanges = 0;
			model.SaveCommand.CanExecuteChanged += (_, _) => commandChanges++;
			SynchronizationContext.SetSynchronizationContext(null);

			_authorization.SignOut();

			Assert.Equal(0, commandChanges);
			Assert.Single(context.Callbacks);
			Assert.False(model.SaveCommand.CanExecute(null));
			SynchronizationContext.SetSynchronizationContext(context);
			context.RunCallbacks();
			Assert.Equal(1, commandChanges);
		}
		finally
		{
			model?.Dispose();
			SynchronizationContext.SetSynchronizationContext(originalContext);
		}
	}

	[Fact]
	public async Task ConfiguredSchedulerRunsWhileSignedOutWithoutEnablingManualBackup()
	{
		DatabaseProvisioningService.Initialize(_factory);
		SignIn(ApplicationPermission.DatabaseManage);
		var candidate = _settings.CurrentSettings;
		candidate.AutomaticBackupsEnabled = true;
		_service.SaveSettings(candidate);
		_authorization.SignOut();
		using var scheduler = new DatabaseBackupScheduler(_service, _settings);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateBackupAsync(Path.Combine(_directory, "manual.depotbackup")));
		Assert.True(await scheduler.RunDueBackupAsync());
		Assert.Single(Directory.EnumerateFiles(candidate.BackupDirectory, "Depot-Auto-*.depotbackup"));
		Assert.NotNull(_settings.CurrentSettings.LastSuccessfulBackupUtc);
		Assert.False(await scheduler.RunDueBackupAsync());
		Assert.False(File.Exists(Path.Combine(_directory, "manual.depotbackup")));
	}

	[Fact]
	public async Task SettingsSnapshotCannotEnableOrRedirectScheduledBackups()
	{
		var originalSettings = await File.ReadAllBytesAsync(_settingsPath);
		var snapshot = _settings.CurrentSettings;
		snapshot.AutomaticBackupsEnabled = true;
		snapshot.BackupDirectory = Path.Combine(_directory, "unapproved-schedule");
		using var scheduler = new DatabaseBackupScheduler(_service, _settings);

		Assert.False(await scheduler.RunDueBackupAsync());
		Assert.False(_settings.CurrentSettings.AutomaticBackupsEnabled);
		Assert.False(Directory.Exists(snapshot.BackupDirectory));
		Assert.False(File.Exists(_databasePath));
		Assert.Equal(originalSettings, await File.ReadAllBytesAsync(_settingsPath));
	}

	[Fact]
	public void AuthorizationDependencyIsRequired()
	{
		Assert.Throws<ArgumentNullException>(() => new DatabaseManagementService(_factory, _settings, null!));
	}

	private DatabaseSettingsViewModel CreateViewModel(IFileDialogService dialogs) =>
		new(_settings, new ConnectionStatusService(), new DatabaseConnectionTester(), _service, dialogs, _authorization);

	private void SetAccess(AccessMode mode)
	{
		switch (mode)
		{
			case AccessMode.SignedOut:
				_authorization.SignOut();
				break;
			case AccessMode.ViewOnly:
				SignIn(ApplicationPermission.DatabaseView);
				break;
			case AccessMode.UnrelatedPermission:
				SignIn(ApplicationPermission.InventoryView);
				break;
			case AccessMode.InactiveManager:
				_authorization.SignIn(new User { Id = 1, IsActive = false }, [ApplicationPermission.DatabaseManage]);
				break;
		}
	}

	private void SignIn(params ApplicationPermission[] permissions) =>
		_authorization.SignIn(new User { Id = 1, Email = "database-authorization@depot.local", IsActive = true }, permissions);

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
	}

	public enum AccessMode { SignedOut, ViewOnly, UnrelatedPermission, InactiveManager }

	private sealed class CountingDialogs : IFileDialogService
	{
		public int Calls { get; private set; }
		public string? ShowOpenFile(OpenFileDialogRequest request) { Calls++; return null; }
		public string? ShowSaveFile(SaveFileDialogRequest request) { Calls++; return null; }
		public bool Confirm(ConfirmationDialogRequest request) { Calls++; return false; }
	}

	private sealed class QueuedSynchronizationContext : SynchronizationContext
	{
		public Queue<Action> Callbacks { get; } = new();
		public override void Post(SendOrPostCallback callback, object? state) => Callbacks.Enqueue(() => callback(state));
		public void RunCallbacks()
		{
			while (Callbacks.TryDequeue(out var callback)) callback();
		}
	}
}
