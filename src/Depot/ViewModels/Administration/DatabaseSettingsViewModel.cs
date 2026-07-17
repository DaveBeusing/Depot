// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class DatabaseSettingsViewModel : BaseViewModel, IDisposable
{
	private readonly SettingsService _settingsService;
	private readonly ConnectionStatusService _connectionStatusService;
	private readonly DatabaseConnectionTester _connectionTester;
	private readonly DatabaseManagementService _databaseManagementService;
	private readonly IFileDialogService _dialogService;
	private DatabaseProvider _provider;
	private string _localDatabasePath = string.Empty;
	private string _sqlServerHost = string.Empty;
	private int _sqlServerPort = 1433;
	private string _sqlServerDatabase = string.Empty;
	private string _sqlServerUserName = string.Empty;
	private string _sqlServerPassword = string.Empty;
	private bool _encryptSqlServerConnection = true;
	private bool _trustSqlServerCertificate;
	private string _mySqlHost = string.Empty;
	private int _mySqlPort = 3306;
	private string _mySqlDatabase = string.Empty;
	private string _mySqlUserName = string.Empty;
	private string _mySqlPassword = string.Empty;
	private bool _useMySqlTls = true;
	private bool _automaticBackupsEnabled;
	private string _backupDirectory = string.Empty;
	private int _backupIntervalDays = 1;
	private string? _message;
	private bool _hasError;
	private int _operationProgress;
	private string _providerDisplay = "—";
	private string _connectionDisplay = "—";
	private string _schemaVersionDisplay = "—";
	private string _databaseSizeDisplay = "—";
	private string _lastBackupDisplay = "Never";
	private string _nextBackupDisplay = "Not scheduled";
	private DatabaseBackupInfo? _selectedBackup;

	public DatabaseSettingsViewModel(
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester connectionTester,
		DatabaseManagementService databaseManagementService,
		IFileDialogService dialogService)
	{
		_settingsService = settingsService;
		_connectionStatusService = connectionStatusService;
		_connectionTester = connectionTester;
		_databaseManagementService = databaseManagementService;
		_dialogService = dialogService;
		SaveCommand = new AsyncRelayCommand(SaveAsync, CanRunOperation);
		TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanRunOperation);
		CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, CanRunOperation);
		RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, CanRunOperation);
		CompactCommand = new AsyncRelayCommand(CompactAsync, () => CanRunOperation() && Provider == DatabaseProvider.Local);
		CheckIntegrityCommand = new AsyncRelayCommand(CheckIntegrityAsync, CanRunOperation);
		ValidateBackupCommand = new AsyncRelayCommand(ValidateBackupAsync, CanRunOperation);
		RefreshCommand = new AsyncRelayCommand(LoadAsync, CanRunOperation);
		Load(settingsService.CurrentSettings);
	}

	public ObservableCollection<DatabaseBackupInfo> Backups { get; } = new();
	public ConnectionStatusService ConnectionStatus => _connectionStatusService;
	public AsyncRelayCommand SaveCommand { get; }
	public AsyncRelayCommand TestConnectionCommand { get; }
	public AsyncRelayCommand CreateBackupCommand { get; }
	public AsyncRelayCommand RestoreBackupCommand { get; }
	public AsyncRelayCommand CompactCommand { get; }
	public AsyncRelayCommand CheckIntegrityCommand { get; }
	public AsyncRelayCommand ValidateBackupCommand { get; }
	public AsyncRelayCommand RefreshCommand { get; }

	public bool UseLocalDatabase { get => Provider == DatabaseProvider.Local; set { if (value) Provider = DatabaseProvider.Local; } }
	public bool UseSqlServer { get => Provider == DatabaseProvider.SqlServer; set { if (value) Provider = DatabaseProvider.SqlServer; } }
	public bool UseMySql { get => Provider == DatabaseProvider.MySql; set { if (value) Provider = DatabaseProvider.MySql; } }
	public bool IsSqlServerSelected => Provider == DatabaseProvider.SqlServer;
	public bool IsMySqlSelected => Provider == DatabaseProvider.MySql;
	public bool CanCompact => Provider == DatabaseProvider.Local;

	public DatabaseProvider Provider
	{
		get => _provider;
		private set
		{
			if (_provider == value) return;
			_provider = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(UseLocalDatabase));
			OnPropertyChanged(nameof(UseSqlServer));
			OnPropertyChanged(nameof(UseMySql));
			OnPropertyChanged(nameof(IsSqlServerSelected));
			OnPropertyChanged(nameof(IsMySqlSelected));
			OnPropertyChanged(nameof(CanCompact));
			CompactCommand.RaiseCanExecuteChanged();
			ClearMessage();
		}
	}

	public string LocalDatabasePath { get => _localDatabasePath; set => SetField(ref _localDatabasePath, value); }
	public string SqlServerHost { get => _sqlServerHost; set => SetField(ref _sqlServerHost, value); }
	public int SqlServerPort { get => _sqlServerPort; set => SetField(ref _sqlServerPort, value); }
	public string SqlServerDatabase { get => _sqlServerDatabase; set => SetField(ref _sqlServerDatabase, value); }
	public string SqlServerUserName { get => _sqlServerUserName; set => SetField(ref _sqlServerUserName, value); }
	public string SqlServerPassword { get => _sqlServerPassword; set => SetField(ref _sqlServerPassword, value); }
	public bool EncryptSqlServerConnection { get => _encryptSqlServerConnection; set => SetField(ref _encryptSqlServerConnection, value); }
	public bool TrustSqlServerCertificate { get => _trustSqlServerCertificate; set => SetField(ref _trustSqlServerCertificate, value); }
	public string MySqlHost { get => _mySqlHost; set => SetField(ref _mySqlHost, value); }
	public int MySqlPort { get => _mySqlPort; set => SetField(ref _mySqlPort, value); }
	public string MySqlDatabase { get => _mySqlDatabase; set => SetField(ref _mySqlDatabase, value); }
	public string MySqlUserName { get => _mySqlUserName; set => SetField(ref _mySqlUserName, value); }
	public string MySqlPassword { get => _mySqlPassword; set => SetField(ref _mySqlPassword, value); }
	public bool UseMySqlTls { get => _useMySqlTls; set => SetField(ref _useMySqlTls, value); }
	public bool AutomaticBackupsEnabled { get => _automaticBackupsEnabled; set { SetField(ref _automaticBackupsEnabled, value); UpdateNextBackupDisplay(); } }
	public string BackupDirectory { get => _backupDirectory; set => SetField(ref _backupDirectory, value); }
	public int BackupIntervalDays { get => _backupIntervalDays; set { SetField(ref _backupIntervalDays, value); UpdateNextBackupDisplay(); } }
	public int OperationProgress { get => _operationProgress; private set => SetField(ref _operationProgress, value, clearMessage: false); }
	public string ProviderDisplay { get => _providerDisplay; private set => SetField(ref _providerDisplay, value, clearMessage: false); }
	public string ConnectionDisplay { get => _connectionDisplay; private set => SetField(ref _connectionDisplay, value, clearMessage: false); }
	public string SchemaVersionDisplay { get => _schemaVersionDisplay; private set => SetField(ref _schemaVersionDisplay, value, clearMessage: false); }
	public string DatabaseSizeDisplay { get => _databaseSizeDisplay; private set => SetField(ref _databaseSizeDisplay, value, clearMessage: false); }
	public string LastBackupDisplay { get => _lastBackupDisplay; private set => SetField(ref _lastBackupDisplay, value, clearMessage: false); }
	public string NextBackupDisplay { get => _nextBackupDisplay; private set => SetField(ref _nextBackupDisplay, value, clearMessage: false); }

	public DatabaseBackupInfo? SelectedBackup
	{
		get => _selectedBackup;
		set => SetField(ref _selectedBackup, value, clearMessage: false);
	}

	public string? Message
	{
		get => _message;
		private set
		{
			_message = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasMessage));
			OnPropertyChanged(nameof(HasSuccessMessage));
		}
	}

	public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
	public bool HasSuccessMessage => HasMessage && !HasError;
	public bool HasError
	{
		get => _hasError;
		private set
		{
			_hasError = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasSuccessMessage));
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		StartOperation("Loading database information...");
		try
		{
			var overviewTask = _databaseManagementService.GetOverviewAsync(cancellationToken);
			var backupsTask = _databaseManagementService.GetBackupsAsync(cancellationToken);
			await Task.WhenAll(overviewTask, backupsTask);
			ApplyOverview(await overviewTask);
			ReplaceBackups(await backupsTask);
			CompleteOperation(Backups.Count == 0, "Database information loaded.");
			RaiseOperationCommands();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(statusText: "Loading cancelled.");
			RaiseOperationCommands();
		}
		catch (Exception exception) { Fail(exception, "Database information could not be loaded."); }
	}

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		StartOperation("Verifying and saving database settings...");
		try
		{
			var candidate = _settingsService.Validate(BuildSettings());
			await Task.Run(() => _connectionTester.Test(candidate), cancellationToken);
			var providerChanged = candidate.Provider != _settingsService.CurrentSettings.Provider;
			var settings = _settingsService.Save(candidate);
			if (!providerChanged) _connectionStatusService.SetConnected(settings);
			Load(settings);
			Succeed(providerChanged
				? "Connection verified and settings saved. Restart Depot to activate the selected provider."
				: "Connection and backup settings saved.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Settings could not be saved."); }
	}

	private async Task TestConnectionAsync(CancellationToken cancellationToken)
	{
		StartOperation("Testing database connection...");
		try
		{
			var candidate = _settingsService.Validate(BuildSettings());
			await Task.Run(() => _connectionTester.Test(candidate), cancellationToken);
			Succeed("Connection successful.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Connection test failed."); }
	}

	private async Task CreateBackupAsync(CancellationToken cancellationToken)
	{
		var filePath = _dialogService.ShowSaveFile(new SaveFileDialogRequest(
			"Create database backup",
			"Depot Backups (*.depotbackup)|*.depotbackup",
			".depotbackup",
			$"Depot-{DateTime.Now:yyyyMMdd-HHmmss}.depotbackup"));
		if (filePath is null) return;
		await RunMaintenanceAsync(
			"Creating database backup...",
			progress => _databaseManagementService.CreateBackupAsync(filePath, progress, cancellationToken),
			backup => $"Backup created successfully: {backup.FilePath}",
			cancellationToken);
	}

	private async Task RestoreBackupAsync(CancellationToken cancellationToken)
	{
		var filePath = SelectedBackup?.FilePath ?? _dialogService.ShowOpenFile(new OpenFileDialogRequest(
			"Select database backup",
			"Depot Backups (*.depotbackup)|*.depotbackup"));
		if (filePath is null) return;
		StartOperation("Validating backup before restore...");
		try
		{
			var validation = await _databaseManagementService.ValidateBackupAsync(filePath, cancellationToken);
			if (!validation.IsValid) throw new InvalidDataException(validation.ValidationMessage);
			var confirmed = _dialogService.Confirm(new ConfirmationDialogRequest(
				"Restore database",
				"All current database records will be replaced by the selected backup. Depot will first create a safety backup of the current database. Continue?",
				IsDestructive: true));
			if (!confirmed)
			{
				CompleteOperation(statusText: "Restore cancelled.");
				RaiseOperationCommands();
				return;
			}
			var progress = CreateProgress();
			var safetyPath = await _databaseManagementService.RestoreBackupAsync(filePath, progress, cancellationToken);
			Succeed($"Database restored successfully. Safety backup: {safetyPath}");
			await RefreshInformationAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Database restore failed."); }
	}

	private async Task CompactAsync(CancellationToken cancellationToken)
	{
		if (!_dialogService.Confirm(new ConfirmationDialogRequest(
			"Compact SQLite database",
			"Compacting rewrites the local database file and may take several minutes. Do not close Depot during this operation. Continue?",
			IsDestructive: true))) return;
		StartOperation("Compacting SQLite database...");
		try
		{
			await _databaseManagementService.CompactAsync(CreateProgress(), cancellationToken);
			Succeed("SQLite database compacted successfully.");
			await RefreshInformationAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Database compaction failed."); }
	}

	private async Task CheckIntegrityAsync(CancellationToken cancellationToken)
	{
		StartOperation("Checking database integrity...");
		try
		{
			var result = await _databaseManagementService.CheckIntegrityAsync(cancellationToken);
			if (!result.IsValid) throw new InvalidDataException(result.Message);
			Succeed(result.Message);
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Integrity check failed."); }
	}

	private async Task ValidateBackupAsync(CancellationToken cancellationToken)
	{
		var filePath = SelectedBackup?.FilePath ?? _dialogService.ShowOpenFile(new OpenFileDialogRequest(
			"Validate database backup",
			"Depot Backups (*.depotbackup)|*.depotbackup"));
		if (filePath is null) return;
		StartOperation("Validating backup... ");
		try
		{
			var result = await _databaseManagementService.ValidateBackupAsync(filePath, cancellationToken);
			if (!result.IsValid) throw new InvalidDataException(result.ValidationMessage);
			Succeed($"Backup is valid: {result.CreatedDisplay}, {result.SizeDisplay}.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, "Backup validation failed."); }
	}

	private async Task RunMaintenanceAsync<T>(
		string status,
		Func<IProgress<DatabaseOperationProgress>, Task<T>> operation,
		Func<T, string> successMessage,
		CancellationToken cancellationToken)
	{
		StartOperation(status);
		try
		{
			var result = await operation(CreateProgress());
			Succeed(successMessage(result));
			await RefreshInformationAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, status); }
	}

	private Progress<DatabaseOperationProgress> CreateProgress() => new(progress =>
	{
		OperationProgress = progress.Percentage;
		UpdateOperationStatus(progress.Message);
	});

	private async Task RefreshInformationAsync(CancellationToken cancellationToken)
	{
		var overview = await _databaseManagementService.GetOverviewAsync(cancellationToken);
		var backups = await _databaseManagementService.GetBackupsAsync(cancellationToken);
		ApplyOverview(overview);
		ReplaceBackups(backups);
	}

	private void ApplyOverview(DatabaseOverview overview)
	{
		ProviderDisplay = overview.ProviderDisplayName;
		ConnectionDisplay = overview.ConnectionDisplayName;
		SchemaVersionDisplay = overview.SchemaVersion.ToString(CultureInfo.CurrentCulture);
		DatabaseSizeDisplay = FormatSize(overview.SizeBytes);
		LastBackupDisplay = overview.LastSuccessfulBackupUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Never";
		UpdateNextBackupDisplay();
	}

	private void ReplaceBackups(IEnumerable<DatabaseBackupInfo> backups)
	{
		Backups.Clear();
		foreach (var backup in backups) Backups.Add(backup);
	}

	private void UpdateNextBackupDisplay()
	{
		var lastBackup = _settingsService.CurrentSettings.LastSuccessfulBackupUtc;
		NextBackupDisplay = !AutomaticBackupsEnabled
			? "Not scheduled"
			: (lastBackup?.AddDays(Math.Max(1, BackupIntervalDays)) ?? DateTime.UtcNow).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
	}

	private DatabaseConnectionSettings BuildSettings() => new()
	{
		Provider = Provider,
		LocalDatabasePath = LocalDatabasePath,
		SqlServerHost = SqlServerHost,
		SqlServerPort = SqlServerPort,
		SqlServerDatabase = SqlServerDatabase,
		SqlServerUserName = SqlServerUserName,
		SqlServerPassword = SqlServerPassword,
		EncryptSqlServerConnection = EncryptSqlServerConnection,
		TrustSqlServerCertificate = TrustSqlServerCertificate,
		MySqlHost = MySqlHost,
		MySqlPort = MySqlPort,
		MySqlDatabase = MySqlDatabase,
		MySqlUserName = MySqlUserName,
		MySqlPassword = MySqlPassword,
		UseMySqlTls = UseMySqlTls,
		AutomaticBackupsEnabled = AutomaticBackupsEnabled,
		BackupDirectory = BackupDirectory,
		BackupIntervalDays = BackupIntervalDays,
		LastSuccessfulBackupUtc = _settingsService.CurrentSettings.LastSuccessfulBackupUtc
	};

	private void Load(DatabaseConnectionSettings settings)
	{
		Provider = settings.Provider;
		LocalDatabasePath = settings.LocalDatabasePath;
		SqlServerHost = settings.SqlServerHost;
		SqlServerPort = settings.SqlServerPort;
		SqlServerDatabase = settings.SqlServerDatabase;
		SqlServerUserName = settings.SqlServerUserName;
		SqlServerPassword = settings.SqlServerPassword;
		EncryptSqlServerConnection = settings.EncryptSqlServerConnection;
		TrustSqlServerCertificate = settings.TrustSqlServerCertificate;
		MySqlHost = settings.MySqlHost;
		MySqlPort = settings.MySqlPort;
		MySqlDatabase = settings.MySqlDatabase;
		MySqlUserName = settings.MySqlUserName;
		MySqlPassword = settings.MySqlPassword;
		UseMySqlTls = settings.UseMySqlTls;
		AutomaticBackupsEnabled = settings.AutomaticBackupsEnabled;
		BackupDirectory = settings.BackupDirectory;
		BackupIntervalDays = settings.BackupIntervalDays;
	}

	private void Succeed(string message)
	{
		HasError = false;
		Message = message;
		OperationProgress = 100;
		CompleteOperation(statusText: message);
		RaiseOperationCommands();
	}

	private void Fail(Exception exception, string status)
	{
		HasError = true;
		Message = exception.Message;
		FailOperation(exception, status);
		RaiseOperationCommands();
	}

	private bool CanRunOperation() => !IsBusy;

	private void StartOperation(string status)
	{
		BeginOperation(status);
		OperationProgress = 0;
		RaiseOperationCommands();
	}

	private void RaiseOperationCommands()
	{
		SaveCommand.RaiseCanExecuteChanged();
		TestConnectionCommand.RaiseCanExecuteChanged();
		CreateBackupCommand.RaiseCanExecuteChanged();
		RestoreBackupCommand.RaiseCanExecuteChanged();
		CompactCommand.RaiseCanExecuteChanged();
		CheckIntegrityCommand.RaiseCanExecuteChanged();
		ValidateBackupCommand.RaiseCanExecuteChanged();
		RefreshCommand.RaiseCanExecuteChanged();
	}

	private void ClearMessage()
	{
		HasError = false;
		Message = null;
	}

	private void SetField<T>(
		ref T field,
		T value,
		bool clearMessage = true,
		[System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		OnPropertyChanged(propertyName);
		if (clearMessage) ClearMessage();
	}

	private static string FormatSize(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		var size = (double)Math.Max(0, bytes);
		var unit = 0;
		while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
		return $"{size:0.##} {units[unit]}";
	}

	public void Dispose()
	{
		SaveCommand.Dispose();
		TestConnectionCommand.Dispose();
		CreateBackupCommand.Dispose();
		RestoreBackupCommand.Dispose();
		CompactCommand.Dispose();
		CheckIntegrityCommand.Dispose();
		ValidateBackupCommand.Dispose();
		RefreshCommand.Dispose();
	}
}
