// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class WarehouseLayoutReadRepository : DatabaseRepository
{
	public WarehouseLayoutReadRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<IReadOnlyList<WarehouseLayoutStockFact>> ListStockFactsAsync(
		long warehouseId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"""
			SELECT sl.Id,
			       COUNT(inv.Id) AS InventoryContextCount,
			       COALESCE(SUM(CASE WHEN COALESCE(stock.CurrentStock, 0) <> 0 THEN 1 ELSE 0 END), 0) AS StockedInventoryContextCount,
			       COALESCE(SUM(COALESCE(stock.CurrentStock, 0)), 0) AS QuantityOnHand
			FROM StorageLocations sl
			LEFT JOIN Inventories inv
			       ON inv.StorageLocationId = sl.Id
			      AND inv.IsActive = 1
			LEFT JOIN
			(
				SELECT InventoryId, SUM(Quantity) AS CurrentStock
				FROM StockMovements
				GROUP BY InventoryId
			) stock ON stock.InventoryId = inv.Id
			WHERE sl.WarehouseId = $WarehouseId
			GROUP BY sl.Id
			ORDER BY sl.Id;
			""",
			ReadStockFact,
			cancellationToken,
			Parameter("$WarehouseId", warehouseId));

	public Task<IReadOnlyList<WarehouseLayoutAttentionFact>> ListDraftTransferAttentionAsync(
		long warehouseId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"""
			SELECT attention.LocationId, COUNT(DISTINCT attention.TransferId)
			FROM
			(
				SELECT sourceInventory.StorageLocationId AS LocationId, transfer.Id AS TransferId
				FROM StockTransfers transfer
				INNER JOIN StockTransferLines line ON line.StockTransferId = transfer.Id
				INNER JOIN Inventories sourceInventory ON sourceInventory.Id = line.SourceInventoryId
				WHERE transfer.Status = $DraftStatus
				  AND transfer.SourceWarehouseId = $WarehouseId
				UNION ALL
				SELECT destinationInventory.StorageLocationId AS LocationId, transfer.Id AS TransferId
				FROM StockTransfers transfer
				INNER JOIN StockTransferLines line ON line.StockTransferId = transfer.Id
				INNER JOIN Inventories destinationInventory ON destinationInventory.Id = line.DestinationInventoryId
				WHERE transfer.Status = $DraftStatus
				  AND transfer.DestinationWarehouseId = $WarehouseId
			) attention
			GROUP BY attention.LocationId
			ORDER BY attention.LocationId;
			""",
			ReadAttentionFact,
			cancellationToken,
			Parameter("$DraftStatus", (int)StockTransferStatus.Draft),
			Parameter("$WarehouseId", warehouseId));

	public Task<IReadOnlyList<WarehouseLayoutAttentionFact>> ListInventoryCountAttentionAsync(
		long warehouseId,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"""
			SELECT inventory.StorageLocationId, COUNT(DISTINCT inventoryCount.Id)
			FROM InventoryCounts inventoryCount
			INNER JOIN InventoryCountLines countLine ON countLine.InventoryCountId = inventoryCount.Id
			INNER JOIN Inventories inventory ON inventory.Id = countLine.InventoryId
			WHERE inventoryCount.WarehouseId = $WarehouseId
			  AND (inventoryCount.Status = $CountingStatus OR inventoryCount.Status = $ReviewStatus)
			GROUP BY inventory.StorageLocationId
			ORDER BY inventory.StorageLocationId;
			""",
			ReadAttentionFact,
			cancellationToken,
			Parameter("$WarehouseId", warehouseId),
			Parameter("$CountingStatus", (int)InventoryCountStatus.Counting),
			Parameter("$ReviewStatus", (int)InventoryCountStatus.Review));

	public async Task<WarehouseLayoutWarehouseAttention> GetWarehouseAttentionAsync(
		long warehouseId,
		bool includeTransfers,
		bool includeInventoryCounts,
		CancellationToken cancellationToken)
	{
		var draftTransfers = 0;
		var activeCounts = 0;

		if (includeTransfers)
		{
			draftTransfers = Convert.ToInt32(
				await Database.ExecuteScalarAsync(
					"""
					SELECT COUNT(*)
					FROM StockTransfers
					WHERE Status = $DraftStatus
					  AND (SourceWarehouseId = $WarehouseId OR DestinationWarehouseId = $WarehouseId);
					""",
					cancellationToken,
					Parameter("$DraftStatus", (int)StockTransferStatus.Draft),
					Parameter("$WarehouseId", warehouseId)),
				System.Globalization.CultureInfo.InvariantCulture);
		}

		if (includeInventoryCounts)
		{
			activeCounts = Convert.ToInt32(
				await Database.ExecuteScalarAsync(
					"""
					SELECT COUNT(*)
					FROM InventoryCounts
					WHERE WarehouseId = $WarehouseId
					  AND (Status = $DraftStatus OR Status = $CountingStatus OR Status = $ReviewStatus);
					""",
					cancellationToken,
					Parameter("$WarehouseId", warehouseId),
					Parameter("$DraftStatus", (int)InventoryCountStatus.Draft),
					Parameter("$CountingStatus", (int)InventoryCountStatus.Counting),
					Parameter("$ReviewStatus", (int)InventoryCountStatus.Review)),
				System.Globalization.CultureInfo.InvariantCulture);
		}

		return new WarehouseLayoutWarehouseAttention(draftTransfers, activeCounts);
	}

	private static WarehouseLayoutStockFact ReadStockFact(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
			Convert.ToInt32(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
			Convert.ToInt64(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture));

	private static WarehouseLayoutAttentionFact ReadAttentionFact(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture));
}
