// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Depot.Data;

public sealed class SqliteConnectionFactory
{
	private readonly string _connectionString;

	public SqliteConnectionFactory(string databasePath)
	{
		var connectionStringBuilder =
			new SqliteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true
			};

		_connectionString =
			connectionStringBuilder.ToString();
	}

	public SqliteConnection CreateConnection()
	{
		return new SqliteConnection(_connectionString);
	}

}
