// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Globalization;

using Depot.Models;

using MySqlConnector;

namespace Depot.Data;

public sealed class MySqlConnectionFactory : IDatabaseConnectionFactory
{
	private static readonly HashSet<string> FinanceDateTimeParameterNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"At",
		"Created",
		"Reversed",
		"Imported",
		"Updated",
		"Approved",
		"Completed",
		"Executed",
		"Posted",
		"Effective",
		"Generated",
		"Cutoff"
	};

	private readonly string _connectionString;
	private readonly string _serverConnectionString;

	public MySqlConnectionFactory(DatabaseConnectionSettings settings)
	{
		DatabaseName = settings.MySqlDatabase;
		var builder = new MySqlConnectionStringBuilder
		{
			Server = settings.MySqlHost,
			Port = (uint)settings.MySqlPort,
			Database = settings.MySqlDatabase,
			UserID = settings.MySqlUserName,
			Password = settings.MySqlPassword,
			SslMode = settings.UseMySqlTls ? MySqlSslMode.Required : MySqlSslMode.Disabled,
			ConnectionTimeout = 10,
			DefaultCommandTimeout = 30,
			ConnectionReset = true,
			Pooling = true,
			ApplicationName = "Depot"
		};

		_connectionString = builder.ConnectionString;
		builder.Database = string.Empty;
		_serverConnectionString = builder.ConnectionString;
	}

	public string DatabaseName { get; }
	public DatabaseProvider Provider => DatabaseProvider.MySql;

	public DbConnection CreateConnection() =>
		new NormalizingSqlConnection(
			new MySqlConnection(_connectionString),
			Provider,
			$"{DatabaseName}@MySQL/MariaDB",
			NormalizeSql,
			NormalizeParameters);

	internal DbConnection CreateServerConnection() =>
		new NormalizingSqlConnection(
			new MySqlConnection(_serverConnectionString),
			Provider,
			"server@MySQL/MariaDB",
			NormalizeSql,
			NormalizeParameters);

	public DbTransaction BeginWriteTransaction(DbConnection connection) =>
		connection.BeginTransaction(IsolationLevel.Serializable);

	public string GetInventoryLockSql() =>
		"SELECT Id FROM Inventories WHERE Id = $InventoryId FOR UPDATE;";

	public string GetInventoryBatchLockSql(string parameterList) =>
		$"SELECT Id FROM Inventories WHERE Id IN ({parameterList}) ORDER BY Id FOR UPDATE;";

	public string GetPurchaseOrderLockSql() =>
		"SELECT Id FROM PurchaseOrders WHERE Id = $PurchaseOrderId FOR UPDATE;";

	public string GetStockTransferLockSql() =>
		"SELECT Id FROM StockTransfers WHERE Id = $StockTransferId FOR UPDATE;";

	public string GetInventoryCountLockSql() =>
		"SELECT Id FROM InventoryCounts WHERE Id = $InventoryCountId FOR UPDATE;";

	public string GetInventoryCountInventoryLockSql() =>
		"SELECT inv.Id FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId WHERE sl.WarehouseId = $WarehouseId AND inv.IsActive = 1 ORDER BY inv.Id FOR UPDATE;";

	public string GetMaterialIssueLockSql() =>
		"SELECT Id FROM MaterialIssues WHERE Id = $MaterialIssueId FOR UPDATE;";

	public string GetMaterialReturnLockSql() =>
		"SELECT Id FROM MaterialReturns WHERE Id = $MaterialReturnId FOR UPDATE;";

	public string GetSupplierReturnLockSql() =>
		"SELECT Id FROM SupplierReturns WHERE Id = $SupplierReturnId FOR UPDATE;";

	public string GetPagingClause() => "LIMIT $PageSize OFFSET $Offset";
	public string CastToInt64(string expression) => $"CAST({expression} AS SIGNED)";

	private static string NormalizeSql(string sql) =>
		sql
			.Replace("$", "@", StringComparison.Ordinal)
			.Replace("SELECT last_insert_rowid();", "SELECT LAST_INSERT_ID();", StringComparison.OrdinalIgnoreCase)
			.Replace(" COLLATE NOCASE", string.Empty, StringComparison.OrdinalIgnoreCase);

	private static void NormalizeParameters(DbCommand command)
	{
		if (!command.CommandText.Contains("Finance", StringComparison.OrdinalIgnoreCase)) return;

		foreach (DbParameter parameter in command.Parameters)
		{
			if (parameter.Value is not string value || !LooksLikeRoundtripTimestamp(value)) continue;
			var name = parameter.ParameterName.TrimStart('@', '$');
			if (!name.EndsWith("Utc", StringComparison.OrdinalIgnoreCase) &&
				!name.EndsWith("At", StringComparison.OrdinalIgnoreCase) &&
				!FinanceDateTimeParameterNames.Contains(name)) continue;
			if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) continue;

			var utc = parsed.Kind switch
			{
				DateTimeKind.Utc => parsed,
				DateTimeKind.Local => parsed.ToUniversalTime(),
				_ => DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
			};
			parameter.Value = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
		}
	}

	private static bool LooksLikeRoundtripTimestamp(string value)
	{
		if (value.Length < 20 || value.IndexOf('T') < 0) return false;
		if (value.EndsWith('Z')) return true;
		if (value.Length < 6) return false;
		var offsetStart = value.Length - 6;
		return (value[offsetStart] == '+' || value[offsetStart] == '-') && value[^3] == ':';
	}
}
