// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

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
		catch (SqlException exception)
		{
			if (exception.Number == 4060 &&
				settings.Provider == DatabaseProvider.SqlServer &&
				CanCreateDatabase(settings))
			{
				return;
			}

			throw new InvalidOperationException(GetSqlServerMessage(exception), exception);
		}
		catch (SqliteException exception)
		{
			throw new InvalidOperationException("The local SQLite database could not be opened.", exception);
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
		catch (SqlException)
		{
			return false;
		}
	}

	private static string GetSqlServerMessage(SqlException exception) =>
		exception.Number switch
		{
			-2 => "The SQL Server connection timed out.",
			53 or 11001 => "The SQL Server host could not be reached.",
			18456 => "SQL Server rejected the supplied credentials.",
			4060 => "The configured SQL Server database is unavailable.",
			_ => "The SQL Server connection could not be established."
		};
}
