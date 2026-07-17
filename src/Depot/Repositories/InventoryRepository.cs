// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class InventoryRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, ItemId, PurposeId, StorageLocationId, IsActive, Version";

	public InventoryRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<Inventory?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Inventories WHERE Id = $Id;",
			ReadInventory,
			cancellationToken,
			Parameter("$Id", id));

	public Task<Inventory?> GetByContextAsync(
		long itemId,
		long purposeId,
		long storageLocationId,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Inventories WHERE ItemId = $ItemId AND PurposeId = $PurposeId AND StorageLocationId = $StorageLocationId;",
			ReadInventory,
			cancellationToken,
			Parameter("$ItemId", itemId),
			Parameter("$PurposeId", purposeId),
			Parameter("$StorageLocationId", storageLocationId));

	public Task<long> CreateAsync(Inventory inventory, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive) VALUES ($ItemId, $PurposeId, $StorageLocationId, $IsActive);",
			cancellationToken,
			Parameter("$ItemId", inventory.ItemId),
			Parameter("$PurposeId", inventory.PurposeId),
			Parameter("$StorageLocationId", inventory.StorageLocationId),
			Parameter("$IsActive", inventory.IsActive));

	public async Task<bool> DeactivateAsync(long id, long version, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE Inventories SET IsActive = 0, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public Task<PageResult<InventoryOverviewItem>> SearchOverviewPageAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR p.Name LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search)"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name,
			       p.Name, w.Name, sl.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId
			LEFT JOIN Categories c ON c.Id = i.CategoryId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name, p.Name, w.Name, sl.Name
			ORDER BY i.PartNumber, p.Name, w.Name, sl.Name
			""",
			$"""
			SELECT COUNT(*)
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId
			LEFT JOIN Categories c ON c.Id = i.CategoryId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter};
			""",
			ReadOverview,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<InventoryLookupItem>> SearchLookupAsync(
		string? searchText,
		int count,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR p.Name LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search)"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QuerySliceAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, p.Name, w.Name, sl.Name
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			ORDER BY i.PartNumber, p.Name, w.Name, sl.Name
			""",
			ReadLookup,
			0,
			count,
			cancellationToken,
			parameters);
	}

	public Task<DashboardSummary?> GetDashboardSummaryAsync(CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT COUNT(DISTINCT summary.ItemId),
			       COALESCE(SUM(summary.CurrentStock), 0),
			       COALESCE(SUM(summary.CurrentStock * summary.AverageCost), 0),
			       (SELECT COUNT(*) FROM StockMovements)
			FROM
			(
				SELECT inv.Id, inv.ItemId,
				       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
				       COALESCE(
				           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
				           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
				           0) AS AverageCost
				FROM Inventories inv
				INNER JOIN Items i ON i.Id = inv.ItemId
				LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
				WHERE inv.IsActive = 1 AND i.IsActive = 1
				GROUP BY inv.Id, inv.ItemId
			) summary;
			""",
			ReadDashboardSummary,
			cancellationToken);

	public Task<InventoryOverviewItem?> GetOverviewByIdAsync(
		long inventoryId,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name,
			       p.Name, w.Name, sl.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId
			LEFT JOIN Categories c ON c.Id = i.CategoryId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.Id = $InventoryId
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name, p.Name, w.Name, sl.Name;
			""",
			ReadOverview,
			cancellationToken,
			Parameter("$InventoryId", inventoryId));

	public IAsyncEnumerable<InventoryOverviewItem> StreamOverviewAsync(
		string? searchText,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR m.Name LIKE $Search OR c.Name LIKE $Search OR p.Name LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search)"
			: string.Empty;
		IReadOnlyList<DatabaseParameter> parameters = hasSearch
			? [Parameter("$Search", $"%{search}%")]
			: [];
		return Database.StreamAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name,
			       p.Name, w.Name, sl.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Manufacturers m ON m.Id = i.ManufacturerId
			LEFT JOIN Categories c ON c.Id = i.CategoryId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, m.Name, c.Name, p.Name, w.Name, sl.Name
			ORDER BY i.PartNumber, p.Name, w.Name, sl.Name;
			""",
			ReadOverview,
			parameters,
			cancellationToken);
	}

	public IReadOnlyList<Inventory> GetAll() =>
		Database.Query(
			$"SELECT {SelectColumns} FROM Inventories WHERE IsActive = 1 ORDER BY ItemId;",
			ReadInventory);

	public IReadOnlyList<Inventory> GetByItem(long itemId) =>
		Database.Query(
			$"""
			SELECT {SelectColumns}
			FROM Inventories
			WHERE ItemId = $ItemId AND IsActive = 1
			ORDER BY PurposeId, StorageLocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId));

	public Inventory? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Inventories WHERE Id = $Id;",
			ReadInventory,
			Parameter("$Id", id));

	public Inventory? GetByItemPurposeLocation(long itemId, long purposeId, long storageLocationId) =>
		Database.QuerySingleOrDefault(
			$"""
			SELECT {SelectColumns}
			FROM Inventories
			WHERE ItemId = $ItemId
			  AND PurposeId = $PurposeId
			  AND StorageLocationId = $StorageLocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId),
			Parameter("$PurposeId", purposeId),
			Parameter("$StorageLocationId", storageLocationId));

	public long Create(Inventory inventory) =>
		Database.Insert(
			"""
			INSERT INTO Inventories (ItemId, PurposeId, StorageLocationId, IsActive)
			VALUES ($ItemId, $PurposeId, $StorageLocationId, $IsActive);
			""",
			Parameter("$ItemId", inventory.ItemId),
			Parameter("$PurposeId", inventory.PurposeId),
			Parameter("$StorageLocationId", inventory.StorageLocationId),
			Parameter("$IsActive", inventory.IsActive));

	public bool Deactivate(long id, long version) =>
		Database.Execute(
			"""
			UPDATE Inventories
			SET IsActive = 0, Version = Version + 1
			WHERE Id = $Id AND Version = $Version;
			""",
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	private static Inventory ReadInventory(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PurposeId = reader.GetInt64(2),
			StorageLocationId = reader.GetInt64(3),
			IsActive = reader.GetBoolean(4),
			Version = reader.GetInt64(5)
		};

	private static InventoryOverviewItem ReadOverview(DbDataReader reader)
	{
		var currentStock = Convert.ToInt32(reader.GetValue(9), System.Globalization.CultureInfo.InvariantCulture);
		var averageCost = Convert.ToDecimal(reader.GetValue(10), System.Globalization.CultureInfo.InvariantCulture);
		return new InventoryOverviewItem
		{
			InventoryId = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PartNumber = reader.GetString(2),
			Description = reader.GetString(3),
			Manufacturer = reader.IsDBNull(4) ? null : reader.GetString(4),
			Category = reader.IsDBNull(5) ? null : reader.GetString(5),
			PurposeName = reader.GetString(6),
			WarehouseName = reader.GetString(7),
			LocationName = reader.GetString(8),
			CurrentStock = currentStock,
			AverageCost = averageCost,
			InventoryValue = currentStock * averageCost
		};
	}

	private static InventoryLookupItem ReadLookup(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PartNumber = reader.GetString(2),
			Description = reader.GetString(3),
			PurposeName = reader.GetString(4),
			WarehouseName = reader.GetString(5),
			LocationName = reader.GetString(6)
		};

	private static DashboardSummary ReadDashboardSummary(DbDataReader reader) =>
		new()
		{
			TotalItems = Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture),
			TotalStockQuantity = Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
			TotalInventoryValue = Convert.ToDecimal(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
			TotalMovements = Convert.ToInt32(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture)
		};
}
