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
		new NormalizingSqlConnection(new SqlConnection(_connectionString));

	internal SqlConnection CreateMasterConnection() =>
		new(_masterConnectionString);

	public DbTransaction BeginWriteTransaction(DbConnection connection) =>
		connection.BeginTransaction(IsolationLevel.Serializable);

	public string GetInventoryLockSql() =>
		"SELECT Id FROM Inventories WITH (UPDLOCK, HOLDLOCK) WHERE Id = $InventoryId;";
}
