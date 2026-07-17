// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class InventoryRepository : DatabaseRepository
{
	private const string SelectColumns = "Id, ItemId, PurposeId, LocationId, IsActive, Version";

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
		long locationId,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM Inventories WHERE ItemId = $ItemId AND PurposeId = $PurposeId AND LocationId = $LocationId;",
			ReadInventory,
			cancellationToken,
			Parameter("$ItemId", itemId),
			Parameter("$PurposeId", purposeId),
			Parameter("$LocationId", locationId));

	public Task<long> CreateAsync(Inventory inventory, CancellationToken cancellationToken) =>
		Database.InsertAsync(
			"INSERT INTO Inventories (ItemId, PurposeId, LocationId, IsActive) VALUES ($ItemId, $PurposeId, $LocationId, $IsActive);",
			cancellationToken,
			Parameter("$ItemId", inventory.ItemId),
			Parameter("$PurposeId", inventory.PurposeId),
			Parameter("$LocationId", inventory.LocationId),
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
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR i.Manufacturer LIKE $Search OR i.Category LIKE $Search OR p.Name LIKE $Search OR l.Name LIKE $Search)"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category,
			       p.Name, l.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN Locations l ON l.Id = inv.LocationId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category, p.Name, l.Name
			ORDER BY i.PartNumber, p.Name, l.Name
			""",
			$"""
			SELECT COUNT(*)
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN Locations l ON l.Id = inv.LocationId
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
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR p.Name LIKE $Search OR l.Name LIKE $Search)"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QuerySliceAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, p.Name, l.Name
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN Locations l ON l.Id = inv.LocationId
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			ORDER BY i.PartNumber, p.Name, l.Name
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
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category,
			       p.Name, l.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN Locations l ON l.Id = inv.LocationId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.Id = $InventoryId
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category, p.Name, l.Name;
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
			? "AND (i.PartNumber LIKE $Search OR i.Description LIKE $Search OR i.Manufacturer LIKE $Search OR i.Category LIKE $Search OR p.Name LIKE $Search OR l.Name LIKE $Search)"
			: string.Empty;
		IReadOnlyList<DatabaseParameter> parameters = hasSearch
			? [Parameter("$Search", $"%{search}%")]
			: [];
		return Database.StreamAsync(
			$"""
			SELECT inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category,
			       p.Name, l.Name,
			       COALESCE(SUM(sm.Quantity), 0) AS CurrentStock,
			       COALESCE(
			           SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity * sm.UnitPrice ELSE 0 END)
			           / NULLIF(SUM(CASE WHEN sm.Quantity > 0 AND sm.UnitPrice IS NOT NULL THEN sm.Quantity ELSE 0 END), 0),
			           0) AS AverageCost
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN Locations l ON l.Id = inv.LocationId
			LEFT JOIN StockMovements sm ON sm.InventoryId = inv.Id
			WHERE inv.IsActive = 1 AND i.IsActive = 1 {filter}
			GROUP BY inv.Id, i.Id, i.PartNumber, i.Description, i.Manufacturer, i.Category, p.Name, l.Name
			ORDER BY i.PartNumber, p.Name, l.Name;
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
			ORDER BY PurposeId, LocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId));

	public Inventory? GetById(long id) =>
		Database.QuerySingleOrDefault(
			$"SELECT {SelectColumns} FROM Inventories WHERE Id = $Id;",
			ReadInventory,
			Parameter("$Id", id));

	public Inventory? GetByItemPurposeLocation(long itemId, long purposeId, long locationId) =>
		Database.QuerySingleOrDefault(
			$"""
			SELECT {SelectColumns}
			FROM Inventories
			WHERE ItemId = $ItemId
			  AND PurposeId = $PurposeId
			  AND LocationId = $LocationId;
			""",
			ReadInventory,
			Parameter("$ItemId", itemId),
			Parameter("$PurposeId", purposeId),
			Parameter("$LocationId", locationId));

	public long Create(Inventory inventory) =>
		Database.Insert(
			"""
			INSERT INTO Inventories (ItemId, PurposeId, LocationId, IsActive)
			VALUES ($ItemId, $PurposeId, $LocationId, $IsActive);
			""",
			Parameter("$ItemId", inventory.ItemId),
			Parameter("$PurposeId", inventory.PurposeId),
			Parameter("$LocationId", inventory.LocationId),
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
			LocationId = reader.GetInt64(3),
			IsActive = reader.GetBoolean(4),
			Version = reader.GetInt64(5)
		};

	private static InventoryOverviewItem ReadOverview(DbDataReader reader)
	{
		var currentStock = Convert.ToInt32(reader.GetValue(8), System.Globalization.CultureInfo.InvariantCulture);
		var averageCost = Convert.ToDecimal(reader.GetValue(9), System.Globalization.CultureInfo.InvariantCulture);
		return new InventoryOverviewItem
		{
			InventoryId = reader.GetInt64(0),
			ItemId = reader.GetInt64(1),
			PartNumber = reader.GetString(2),
			Description = reader.GetString(3),
			Manufacturer = reader.IsDBNull(4) ? null : reader.GetString(4),
			Category = reader.IsDBNull(5) ? null : reader.GetString(5),
			PurposeName = reader.GetString(6),
			LocationName = reader.GetString(7),
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
			LocationName = reader.GetString(5)
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
