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
		ConnectionStatusService connectionStatusService)
	{
		_settingsService = settingsService;
		_connectionStatusService = connectionStatusService;
		SaveCommand = new RelayCommand(Save);
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

	private void Save()
	{
		ClearMessage();

		try
		{
			var settings = _settingsService.Save(new DatabaseConnectionSettings
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
			});

			_connectionStatusService.Apply(settings);
			Load(settings);
			Message = settings.Provider == DatabaseProvider.Local
				? "Local database settings saved."
				: "SQL Server settings saved securely. The local fallback remains active until SQL Server support is enabled.";
		}
		catch (Exception exception)
		{
			HasError = true;
			Message = exception.Message;
		}
	}

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
