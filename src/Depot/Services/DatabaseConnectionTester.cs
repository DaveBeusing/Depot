// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Diagnostics;
using Depot.Models;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Depot.Services;

public sealed class DatabaseConnectionTester
{
	public void Test(DatabaseConnectionSettings settings)
	{
		try
		{
			var factory = DatabaseProviderFactory.CreateConnectionFactory(settings);
			using var connection = factory.CreateConnection();
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT 1;";
			if (Convert.ToInt32(command.ExecuteScalar()) != 1)
			{
				throw new InvalidOperationException("The database did not return a valid response.");
			}
		}
		catch (DatabaseConnectionException exception) when (
			exception.InnerException is SqlException { Number: 4060 } &&
			settings.Provider == DatabaseProvider.SqlServer &&
			CanCreateDatabase(settings))
		{
			return;
		}
		catch (DatabaseConnectionException exception) when (
			exception.InnerException is MySqlException { Number: 1049 } &&
			settings.Provider == DatabaseProvider.MySql &&
			CanReachMySqlServer(settings))
		{
			return;
		}
		catch (DatabaseConnectionException)
		{
			throw;
		}
		catch (SqlException exception)
		{
			if (exception.Number == 4060 &&
				settings.Provider == DatabaseProvider.SqlServer &&
				CanCreateDatabase(settings))
			{
				return;
			}

			throw new InvalidOperationException(DatabaseErrorMessages.GetUserMessage(exception), exception);
		}
		catch (SqliteException exception)
		{
			throw new InvalidOperationException(DatabaseErrorMessages.GetUserMessage(exception), exception);
		}
		catch (MySqlException exception)
		{
			if (exception.Number == 1049 &&
				settings.Provider == DatabaseProvider.MySql &&
				CanReachMySqlServer(settings))
			{
				return;
			}

			throw new InvalidOperationException(DatabaseErrorMessages.GetUserMessage(exception), exception);
		}
	}

	private static bool CanReachMySqlServer(DatabaseConnectionSettings settings)
	{
		try
		{
			var factory = new MySqlConnectionFactory(settings);
			using var connection = factory.CreateServerConnection();
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT 1;";
			return Convert.ToInt32(command.ExecuteScalar()) == 1;
		}
		catch (Exception exception) when (exception is MySqlException or DatabaseConnectionException)
		{
			return false;
		}
	}

	private static bool CanCreateDatabase(DatabaseConnectionSettings settings)
	{
		try
		{
			var factory = new SqlServerConnectionFactory(settings);
			using var connection = factory.CreateMasterConnection();
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE');";
			return Convert.ToInt32(command.ExecuteScalar()) == 1;
		}
		catch (Exception exception) when (exception is SqlException or DatabaseConnectionException)
		{
			return false;
		}
	}

}
