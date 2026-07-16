// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed class DatabaseSettingsViewModel : BaseViewModel
{
	private readonly SettingsService _settingsService;
	private readonly ConnectionStatusService _connectionStatusService;
	private readonly DatabaseConnectionTester _databaseConnectionTester;
	private DatabaseProvider _provider;
	private string _localDatabasePath = string.Empty;
	private string _sqlServerHost = string.Empty;
	private int _sqlServerPort = 1433;
	private string _sqlServerDatabase = string.Empty;
	private string _sqlServerUserName = string.Empty;
	private string _sqlServerPassword = string.Empty;
	private bool _encryptSqlServerConnection = true;
	private bool _trustSqlServerCertificate;
	private string? _message;
	private bool _hasError;

	public DatabaseSettingsViewModel(
		SettingsService settingsService,
		ConnectionStatusService connectionStatusService,
		DatabaseConnectionTester databaseConnectionTester)
	{
		_settingsService = settingsService;
		_connectionStatusService = connectionStatusService;
		_databaseConnectionTester = databaseConnectionTester;
		SaveCommand = new RelayCommand(Save);
		TestConnectionCommand = new RelayCommand(TestConnection);
		Load(settingsService.CurrentSettings);
	}

	public bool UseLocalDatabase
	{
		get => Provider == DatabaseProvider.Local;
		set
		{
			if (value)
			{
				Provider = DatabaseProvider.Local;
			}
		}
	}

	public bool UseSqlServer
	{
		get => Provider == DatabaseProvider.SqlServer;
		set
		{
			if (value)
			{
				Provider = DatabaseProvider.SqlServer;
			}
		}
	}

	public DatabaseProvider Provider
	{
		get => _provider;
		private set
		{
			if (_provider == value)
			{
				return;
			}

			_provider = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(UseLocalDatabase));
			OnPropertyChanged(nameof(UseSqlServer));
			OnPropertyChanged(nameof(IsSqlServerSelected));
			ClearMessage();
		}
	}

	public bool IsSqlServerSelected => Provider == DatabaseProvider.SqlServer;

	public string LocalDatabasePath
	{
		get => _localDatabasePath;
		set => SetField(ref _localDatabasePath, value);
	}

	public string SqlServerHost
	{
		get => _sqlServerHost;
		set => SetField(ref _sqlServerHost, value);
	}

	public int SqlServerPort
	{
		get => _sqlServerPort;
		set => SetField(ref _sqlServerPort, value);
	}

	public string SqlServerDatabase
	{
		get => _sqlServerDatabase;
		set => SetField(ref _sqlServerDatabase, value);
	}

	public string SqlServerUserName
	{
		get => _sqlServerUserName;
		set => SetField(ref _sqlServerUserName, value);
	}

	public string SqlServerPassword
	{
		get => _sqlServerPassword;
		set => SetField(ref _sqlServerPassword, value);
	}

	public bool EncryptSqlServerConnection
	{
		get => _encryptSqlServerConnection;
		set => SetField(ref _encryptSqlServerConnection, value);
	}

	public bool TrustSqlServerCertificate
	{
		get => _trustSqlServerCertificate;
		set => SetField(ref _trustSqlServerCertificate, value);
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

	public ConnectionStatusService ConnectionStatus => _connectionStatusService;

	public RelayCommand SaveCommand { get; }
	public RelayCommand TestConnectionCommand { get; }

	private void Save()
	{
		ClearMessage();

		try
		{
			var candidate = _settingsService.Validate(BuildSettings());
			_databaseConnectionTester.Test(candidate);
			var providerChanged = candidate.Provider != _settingsService.CurrentSettings.Provider;
			var settings = _settingsService.Save(candidate);
			if (!providerChanged)
			{
				_connectionStatusService.SetConnected(settings);
			}
			Load(settings);
			Message = providerChanged
				? "Connection verified and settings saved. Restart Depot to activate the selected provider."
				: "Connection verified and settings saved.";
		}
		catch (Exception exception)
		{
			HasError = true;
			Message = exception.Message;
		}
	}

	private void TestConnection()
	{
		ClearMessage();
		try
		{
			_databaseConnectionTester.Test(_settingsService.Validate(BuildSettings()));
			Message = "Connection successful.";
		}
		catch (Exception exception)
		{
			HasError = true;
			Message = exception.Message;
		}
	}

	private DatabaseConnectionSettings BuildSettings() =>
		new()
		{
			Provider = Provider,
			LocalDatabasePath = LocalDatabasePath,
			SqlServerHost = SqlServerHost,
			SqlServerPort = SqlServerPort,
			SqlServerDatabase = SqlServerDatabase,
			SqlServerUserName = SqlServerUserName,
			SqlServerPassword = SqlServerPassword,
			EncryptSqlServerConnection = EncryptSqlServerConnection,
			TrustSqlServerCertificate = TrustSqlServerCertificate
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
	}

	private void ClearMessage()
	{
		HasError = false;
		Message = null;
	}

	private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return;
		}

		field = value;
		OnPropertyChanged(propertyName);
		ClearMessage();
	}
}
