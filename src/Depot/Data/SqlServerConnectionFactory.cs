// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;

using Depot.Models;

using Microsoft.Data.SqlClient;

namespace Depot.Data;

public sealed class SqlServerConnectionFactory : IDatabaseConnectionFactory
{
	private readonly string _connectionString;
	private readonly string _masterConnectionString;

	public string DatabaseName { get; }

	public SqlServerConnectionFactory(DatabaseConnectionSettings settings)
	{
		var builder = new SqlConnectionStringBuilder
		{
			DataSource = $"{settings.SqlServerHost},{settings.SqlServerPort}",
			InitialCatalog = settings.SqlServerDatabase,
			UserID = settings.SqlServerUserName,
			Password = settings.SqlServerPassword,
			Encrypt = settings.EncryptSqlServerConnection,
			TrustServerCertificate = settings.TrustSqlServerCertificate,
			ConnectTimeout = 10,
			ConnectRetryCount = 3,
			ConnectRetryInterval = 2,
			Pooling = true,
			ApplicationName = "Depot"
		};

		DatabaseName = settings.SqlServerDatabase;
		_connectionString = builder.ConnectionString;
		builder.InitialCatalog = "master";
		_masterConnectionString = builder.ConnectionString;
	}

	public DatabaseProvider Provider => DatabaseProvider.SqlServer;

	public DbConnection CreateConnection() =>
		new NormalizingSqlConnection(
			new SqlConnection(_connectionString),
			Provider,
			$"{DatabaseName}@SQL Server",
			NormalizeSql);

	internal DbConnection CreateMasterConnection() =>
		new NormalizingSqlConnection(
			new SqlConnection(_masterConnectionString),
			Provider,
			"master@SQL Server",
			NormalizeSql);

	public DbTransaction BeginWriteTransaction(DbConnection connection) =>
		connection.BeginTransaction(IsolationLevel.Serializable);

	public string GetInventoryLockSql() =>
		"SELECT Id FROM Inventories WITH (UPDLOCK, HOLDLOCK) WHERE Id = $InventoryId;";

	public string GetPurchaseOrderLockSql() =>
		"SELECT Id FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE Id = $PurchaseOrderId;";

	public string GetPagingClause() => "OFFSET $Offset ROWS FETCH NEXT $PageSize ROWS ONLY";

	private static string NormalizeSql(string sql) =>
		sql
			.Replace("$", "@", StringComparison.Ordinal)
			.Replace("SELECT last_insert_rowid();", "SELECT CAST(SCOPE_IDENTITY() AS bigint);", StringComparison.OrdinalIgnoreCase)
			.Replace(" COLLATE NOCASE", string.Empty, StringComparison.OrdinalIgnoreCase);
}
