// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

internal static class FinanceInventoryCostingRepositoryExtensions
{
	private const string Columns = "Id,InventoryId,ReasonCodeId,MovementType,TimestampUtc,Quantity,UnitPrice,Reference,Notes,ReversalOfMovementId,ReversalReason,ReversedAtUtc,ReversedByUserId";

	internal static Task<IReadOnlyList<StockMovement>> GetInventoryCountMovementsAsync(this FinanceInventoryCostingRepository _, DatabaseTransactionContext transaction, string reference, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync($"SELECT {Columns} FROM StockMovements WHERE Reference=$Reference AND (MovementType=$Correction OR ReversalOfMovementId IN (SELECT Id FROM StockMovements WHERE Reference=$Reference AND MovementType=$Correction)) ORDER BY TimestampUtc,Id;", ReadMovement, cancellationToken, new DatabaseParameter("$Reference", reference), new DatabaseParameter("$Correction", (int)StockMovementType.Correction));

	private static StockMovement ReadMovement(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		InventoryId = reader.GetInt64(1),
		ReasonCodeId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
		MovementType = (StockMovementType)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		TimestampUtc = Convert.ToDateTime(reader.GetValue(4), CultureInfo.InvariantCulture),
		Quantity = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
		UnitPrice = reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
		Reference = reader.IsDBNull(7) ? null : reader.GetString(7),
		Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
		ReversalOfMovementId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
		ReversalReason = reader.IsDBNull(10) ? null : reader.GetString(10),
		ReversedAtUtc = reader.IsDBNull(11) ? null : Convert.ToDateTime(reader.GetValue(11), CultureInfo.InvariantCulture),
		ReversedByUserId = reader.IsDBNull(12) ? null : reader.GetInt64(12)
	};
}
