// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

namespace Depot.Diagnostics;

public static class DatabaseErrorMessages
{
	public static string GetUserMessage(Exception exception)
	{
		var databaseException = FindDatabaseException(exception);
		return databaseException switch
		{
			SqlException sql => GetSqlServerMessage(sql),
			SqliteException sqlite => GetSqliteMessage(sqlite),
			MySqlException mySql => GetMySqlMessage(mySql),
			_ => exception.Message
		};
	}

	private static Exception? FindDatabaseException(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is SqlException or SqliteException or MySqlException)
			{
				return current;
			}
		}

		return null;
	}

	private static string GetSqlServerMessage(SqlException exception) =>
		exception.Number switch
		{
			-2 => "The SQL Server connection timed out.",
			53 or 11001 => "The SQL Server host could not be reached.",
			18456 => "SQL Server rejected the supplied credentials.",
			4060 => "The configured SQL Server database is unavailable.",
			1205 => "The SQL Server operation was selected as a deadlock victim. Please retry.",
			3960 => "The SQL Server data was changed by another user. Reload and retry.",
			_ => $"The SQL Server operation failed (error {exception.Number})."
		};

	private static string GetSqliteMessage(SqliteException exception) =>
		exception.SqliteErrorCode switch
		{
			5 or 6 => "The local SQLite database is currently locked. Please retry.",
			8 => "The local SQLite database is read-only.",
			10 or 14 => "The local SQLite database file or its directory is unavailable.",
			19 => "The local SQLite operation violates a data constraint.",
			26 => "The configured local file is not a valid SQLite database.",
			_ => $"The local SQLite operation failed (error {exception.SqliteErrorCode})."
		};

	private static string GetMySqlMessage(MySqlException exception) =>
		exception.Number switch
		{
			0 or 1042 or 2002 or 2003 or 2005 or 2006 => "The MySQL/MariaDB server could not be reached.",
			1040 => "The MySQL/MariaDB server has reached its connection limit.",
			1044 => "The MySQL/MariaDB account has no access to the configured database.",
			1045 => "MySQL/MariaDB rejected the supplied credentials.",
			1049 => "The configured MySQL/MariaDB database is unavailable.",
			1062 => "The MySQL/MariaDB operation would create a duplicate value.",
			1129 or 1130 => "This computer is not permitted to connect to the MySQL/MariaDB server.",
			1205 => "The MySQL/MariaDB operation exceeded the lock wait timeout. Please retry.",
			1213 => "The MySQL/MariaDB operation encountered a deadlock. Please retry.",
			2026 => "The MySQL/MariaDB TLS connection could not be established.",
			_ => $"The MySQL/MariaDB operation failed (error {exception.Number})."
		};
}
