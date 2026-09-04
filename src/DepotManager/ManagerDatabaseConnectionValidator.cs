using Depot.Diagnostics;
using Depot.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using System.IO;

namespace DepotManager;

public sealed class ManagerDatabaseConnectionValidator
{
	public async Task ValidateAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(settings);

		try
		{
			switch (settings.Provider)
			{
				case DatabaseProvider.Local:
					await ValidateSqliteAsync(settings, cancellationToken);
					break;
				case DatabaseProvider.SqlServer:
					await ValidateSqlServerAsync(settings, cancellationToken);
					break;
				case DatabaseProvider.MySql:
					await ValidateMySqlAsync(settings, cancellationToken);
					break;
				default:
					throw new NotSupportedException($"Database provider '{settings.Provider}' is not supported.");
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (exception is SqlException or SqliteException or MySqlException)
		{
			throw new InvalidOperationException(DatabaseErrorMessages.GetUserMessage(exception), exception);
		}
	}

	private static async Task ValidateSqliteAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(settings.LocalDatabasePath))
			throw new InvalidOperationException("Choose a local SQLite database file.");

		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = Path.GetFullPath(settings.LocalDatabasePath),
			ForeignKeys = true
		};

		await using var connection = new SqliteConnection(builder.ConnectionString);
		await connection.OpenAsync(cancellationToken);
		await VerifyResponseAsync(connection, cancellationToken);
	}

	private static async Task ValidateSqlServerAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
	{
		ValidateRemoteInputs(settings.SqlServerHost, settings.SqlServerPort, settings.SqlServerDatabase, settings.SqlServerUserName, "SQL Server");
		var builder = CreateSqlServerBuilder(settings, settings.SqlServerDatabase);

		try
		{
			await using var connection = new SqlConnection(builder.ConnectionString);
			await connection.OpenAsync(cancellationToken);
			await VerifyResponseAsync(connection, cancellationToken);
		}
		catch (SqlException exception) when (exception.Number == 4060)
		{
			builder.InitialCatalog = "master";
			await using var master = new SqlConnection(builder.ConnectionString);
			await master.OpenAsync(cancellationToken);
			await using var command = master.CreateCommand();
			command.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE');";
			var canCreate = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
			if (!canCreate) throw;
		}
	}

	private static async Task ValidateMySqlAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken)
	{
		ValidateRemoteInputs(settings.MySqlHost, settings.MySqlPort, settings.MySqlDatabase, settings.MySqlUserName, "MySQL/MariaDB");
		var builder = CreateMySqlBuilder(settings, settings.MySqlDatabase);

		try
		{
			await using var connection = new MySqlConnection(builder.ConnectionString);
			await connection.OpenAsync(cancellationToken);
			await VerifyResponseAsync(connection, cancellationToken);
		}
		catch (MySqlException exception) when (exception.Number == 1049)
		{
			builder.Database = string.Empty;
			await using var server = new MySqlConnection(builder.ConnectionString);
			await server.OpenAsync(cancellationToken);
			await VerifyResponseAsync(server, cancellationToken);
		}
	}

	private static SqlConnectionStringBuilder CreateSqlServerBuilder(DatabaseConnectionSettings settings, string database) =>
		new()
		{
			DataSource = $"{settings.SqlServerHost},{settings.SqlServerPort}",
			InitialCatalog = database,
			UserID = settings.SqlServerUserName,
			Password = settings.SqlServerPassword,
			Encrypt = settings.EncryptSqlServerConnection,
			TrustServerCertificate = settings.TrustSqlServerCertificate,
			ConnectTimeout = 10,
			ConnectRetryCount = 3,
			ConnectRetryInterval = 2,
			Pooling = true,
			ApplicationName = "Depot Manager"
		};

	private static MySqlConnectionStringBuilder CreateMySqlBuilder(DatabaseConnectionSettings settings, string database) =>
		new()
		{
			Server = settings.MySqlHost,
			Port = (uint)settings.MySqlPort,
			Database = database,
			UserID = settings.MySqlUserName,
			Password = settings.MySqlPassword,
			SslMode = settings.UseMySqlTls ? MySqlSslMode.Required : MySqlSslMode.Disabled,
			ConnectionTimeout = 10,
			DefaultCommandTimeout = 30,
			ConnectionReset = true,
			Pooling = true,
			ApplicationName = "Depot Manager"
		};

	private static async Task VerifyResponseAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT 1;";
		if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
			throw new InvalidOperationException("The database did not return a valid response.");
	}

	private static void ValidateRemoteInputs(string host, int port, string database, string userName, string provider)
	{
		if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException($"Enter the {provider} host.");
		if (port is < 1 or > 65535) throw new InvalidOperationException($"Enter a valid {provider} port.");
		if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException($"Enter the {provider} database name.");
		if (string.IsNullOrWhiteSpace(userName)) throw new InvalidOperationException($"Enter the {provider} username.");
	}
}
