// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Models;

namespace Depot.Data;

public interface IDatabaseConnectionFactory
{
	DatabaseProvider Provider { get; }
	DbConnection CreateConnection();
	DbTransaction BeginWriteTransaction(DbConnection connection);
	ValueTask<DbTransaction> BeginWriteTransactionAsync(
		DbConnection connection,
		CancellationToken cancellationToken) =>
		ValueTask.FromResult(BeginWriteTransaction(connection));
	string GetInventoryLockSql();
	string GetInventoryBatchLockSql(string parameterList);
	string GetPurchaseOrderLockSql();
	string GetStockTransferLockSql();
	string GetInventoryCountLockSql();
	string GetInventoryCountInventoryLockSql();
	string GetPagingClause();
	string CastToInt64(string expression);
}
