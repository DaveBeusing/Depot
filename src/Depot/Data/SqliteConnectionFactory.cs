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

	public string GetInventoryBatchLockSql(string parameterList) =>
		$"SELECT Id FROM Inventories WHERE Id IN ({parameterList}) ORDER BY Id;";

	public string GetPurchaseOrderLockSql() =>
		"SELECT Id FROM PurchaseOrders WHERE Id = $PurchaseOrderId;";

	public string GetStockTransferLockSql() =>
		"SELECT Id FROM StockTransfers WHERE Id = $StockTransferId;";

	public string GetInventoryCountLockSql() =>
		"SELECT Id FROM InventoryCounts WHERE Id = $InventoryCountId;";

	public string GetInventoryCountInventoryLockSql() =>
		"SELECT inv.Id FROM Inventories inv INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId WHERE sl.WarehouseId = $WarehouseId AND inv.IsActive = 1 ORDER BY inv.Id;";

	public string GetMaterialIssueLockSql() =>
		"SELECT Id FROM MaterialIssues WHERE Id = $MaterialIssueId;";

	public string GetMaterialReturnLockSql() =>
		"SELECT Id FROM MaterialReturns WHERE Id = $MaterialReturnId;";

	public string GetSupplierReturnLockSql() =>
		"SELECT Id FROM SupplierReturns WHERE Id = $SupplierReturnId;";

	public string GetPagingClause() => "LIMIT $PageSize OFFSET $Offset";
	public string CastToInt64(string expression) => $"CAST({expression} AS INTEGER)";

}
