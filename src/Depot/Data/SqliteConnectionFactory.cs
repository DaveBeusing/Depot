// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Depot.Data;

public sealed class SqliteConnectionFactory
{
	private readonly string _connectionString;

	public SqliteConnectionFactory(string databasePath)
	{
		_connectionString = $"Data Source={databasePath}";
	}

	public SqliteConnection CreateConnection()
	{
		return new SqliteConnection(_connectionString);
	}

}