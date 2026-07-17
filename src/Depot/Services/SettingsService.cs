// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SettingsService
{
	private readonly SettingsRepository _settingsRepository;

	public SettingsService(SettingsRepository settingsRepository)
	{
		_settingsRepository = settingsRepository;
	}

	public DatabaseConnectionSettings CurrentSettings { get; private set; } = new();

	public DatabaseConnectionSettings LoadOrCreate()
	{
		if (!_settingsRepository.Exists())
		{
			CurrentSettings = new DatabaseConnectionSettings();
			_settingsRepository.Save(CurrentSettings);
			return Clone(CurrentSettings);
		}

		CurrentSettings = NormalizeAndValidate(_settingsRepository.Load());
		return Clone(CurrentSettings);
	}

	public DatabaseConnectionSettings Save(DatabaseConnectionSettings settings)
	{
		CurrentSettings = NormalizeAndValidate(settings);
		_settingsRepository.Save(CurrentSettings);
		return Clone(CurrentSettings);
	}

	public DatabaseConnectionSettings Validate(DatabaseConnectionSettings settings) =>
		NormalizeAndValidate(settings);

	public DatabaseConnectionSettings RecordSuccessfulBackup(DateTime completedUtc)
	{
		var settings = Clone(CurrentSettings);
		settings.LastSuccessfulBackupUtc = completedUtc;
		return Save(settings);
	}

	private static DatabaseConnectionSettings NormalizeAndValidate(
		DatabaseConnectionSettings settings)
	{
		settings.LocalDatabasePath = settings.LocalDatabasePath.Trim();
		settings.SqlServerHost = settings.SqlServerHost.Trim();
		settings.SqlServerDatabase = settings.SqlServerDatabase.Trim();
		settings.SqlServerUserName = settings.SqlServerUserName.Trim();
		settings.MySqlHost = settings.MySqlHost.Trim();
		settings.MySqlDatabase = settings.MySqlDatabase.Trim();
		settings.MySqlUserName = settings.MySqlUserName.Trim();
		settings.BackupDirectory = settings.BackupDirectory.Trim();

		if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
		{
			throw new ArgumentException("A backup directory is required.");
		}

		if (settings.BackupIntervalDays is < 1 or > 365)
		{
			throw new ArgumentOutOfRangeException(
				nameof(settings.BackupIntervalDays),
				"The backup interval must be between 1 and 365 days.");
		}

		if (string.IsNullOrWhiteSpace(settings.LocalDatabasePath))
		{
			throw new ArgumentException("A local database path is required.");
		}

		if (settings.Provider == DatabaseProvider.MySql)
		{
			if (string.IsNullOrWhiteSpace(settings.MySqlHost) ||
				string.IsNullOrWhiteSpace(settings.MySqlDatabase) ||
				string.IsNullOrWhiteSpace(settings.MySqlUserName) ||
				string.IsNullOrEmpty(settings.MySqlPassword))
			{
				throw new ArgumentException("Host, database, user name, and password are required for MySQL/MariaDB.");
			}

			if (settings.MySqlPort is < 1 or > 65535)
			{
				throw new ArgumentOutOfRangeException(nameof(settings.MySqlPort), "The MySQL/MariaDB port must be between 1 and 65535.");
			}
		}

		if (settings.Provider == DatabaseProvider.SqlServer)
		{
			if (string.IsNullOrWhiteSpace(settings.SqlServerHost) ||
				string.IsNullOrWhiteSpace(settings.SqlServerDatabase) ||
				string.IsNullOrWhiteSpace(settings.SqlServerUserName) ||
				string.IsNullOrEmpty(settings.SqlServerPassword))
			{
				throw new ArgumentException(
					"Server, database, user name, and password are required for SQL Server.");
			}

			if (settings.SqlServerPort is < 1 or > 65535)
			{
				throw new ArgumentOutOfRangeException(
					nameof(settings.SqlServerPort),
					"The SQL Server port must be between 1 and 65535.");
			}
		}

		return Clone(settings);
	}

	private static DatabaseConnectionSettings Clone(DatabaseConnectionSettings settings)
	{
		return new DatabaseConnectionSettings
		{
			Provider = settings.Provider,
			LocalDatabasePath = settings.LocalDatabasePath,
			SqlServerHost = settings.SqlServerHost,
			SqlServerPort = settings.SqlServerPort,
			SqlServerDatabase = settings.SqlServerDatabase,
			SqlServerUserName = settings.SqlServerUserName,
			SqlServerPassword = settings.SqlServerPassword,
			EncryptSqlServerConnection = settings.EncryptSqlServerConnection,
			TrustSqlServerCertificate = settings.TrustSqlServerCertificate,
			MySqlHost = settings.MySqlHost,
			MySqlPort = settings.MySqlPort,
			MySqlDatabase = settings.MySqlDatabase,
			MySqlUserName = settings.MySqlUserName,
			MySqlPassword = settings.MySqlPassword,
			UseMySqlTls = settings.UseMySqlTls,
			AutomaticBackupsEnabled = settings.AutomaticBackupsEnabled,
			BackupDirectory = settings.BackupDirectory,
			BackupIntervalDays = settings.BackupIntervalDays,
			LastSuccessfulBackupUtc = settings.LastSuccessfulBackupUtc
		};
	}
}
