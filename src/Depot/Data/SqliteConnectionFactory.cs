// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;

namespace Depot.Data;

public sealed class SqliteConnectionFactory : IDatabaseConnectionFactory
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

	System.Data.Common.DbConnection IDatabaseConnectionFactory.CreateConnection() =>
		new NormalizingSqlConnection(
			CreateConnection(),
			Provider,
			"local SQLite");

	public Models.DatabaseProvider Provider => Models.DatabaseProvider.Local;

	public System.Data.Common.DbTransaction BeginWriteTransaction(
		System.Data.Common.DbConnection connection) =>
		((NormalizingSqlConnection)connection).BeginImmediateTransaction();

	public string GetInventoryLockSql() =>
		"SELECT Id FROM Inventories WHERE Id = $InventoryId;";

	public string GetPagingClause() => "LIMIT $PageSize OFFSET $Offset";

}
