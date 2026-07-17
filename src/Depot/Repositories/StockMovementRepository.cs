// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using System.Text.Json;

using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class StockMovementRepository : DatabaseRepository
{
	private const string SelectColumns =
		"Id, InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes";

	public StockMovementRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAtomicAsync(
		StockMovement movement,
		AuditEntry auditEntry,
		CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(
			async (session, token) =>
			{
				if (await session.ExecuteScalarAsync(
					Database.InventoryLockSql,
					token,
					Parameter("$InventoryId", movement.InventoryId)) is null)
				{
					throw new InvalidOperationException($"Inventory with id '{movement.InventoryId}' was not found.");
				}

				var currentStock = Convert.ToInt64(
					await session.ExecuteScalarAsync(
						"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;",
						token,
						Parameter("$InventoryId", movement.InventoryId)),
					CultureInfo.InvariantCulture);
				if (currentStock + movement.Quantity < 0)
				{
					throw new InsufficientStockException();
				}

				var movementId = await session.InsertAsync(
					"""
					INSERT INTO StockMovements
					(InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes)
					VALUES
					($InventoryId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes);
					""",
					token,
					Parameter("$InventoryId", movement.InventoryId),
					Parameter("$MovementType", (int)movement.MovementType),
					Parameter("$TimestampUtc", movement.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$Quantity", movement.Quantity),
					Parameter("$UnitPrice", movement.UnitPrice),
					Parameter("$Reference", movement.Reference),
					Parameter("$Notes", movement.Notes));

				movement.Id = movementId;
				auditEntry.EntityId = movementId;
				auditEntry.AfterJson = JsonSerializer.Serialize(
					movement,
					new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
				await session.ExecuteAsync(
					"""
					INSERT INTO AuditEntries
					(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
					VALUES
					($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
					""",
					token,
					Parameter("$TimestampUtc", auditEntry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$UserId", auditEntry.UserId),
					Parameter("$UserEmail", auditEntry.UserEmail),
					Parameter("$EntityType", auditEntry.EntityType),
					Parameter("$EntityId", auditEntry.EntityId),
					Parameter("$Action", auditEntry.Action),
					Parameter("$BeforeJson", auditEntry.BeforeJson),
					Parameter("$AfterJson", auditEntry.AfterJson));
				return movementId;
			},
			cancellationToken);

	public Task<PageResult<MovementOverviewItem>> SearchOverviewPageAsync(
		string? searchText,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var search = searchText?.Trim();
		var hasSearch = !string.IsNullOrWhiteSpace(search);
		var filter = hasSearch
			? "WHERE i.PartNumber LIKE $Search OR i.Description LIKE $Search OR p.Name LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search OR sm.Reference LIKE $Search OR sm.Notes LIKE $Search"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"""
			SELECT sm.Id, sm.TimestampUtc, inv.Id, i.Id, i.PartNumber, i.Description,
			       p.Name, w.Name, sl.Name, sm.MovementType, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			{filter}
			ORDER BY sm.TimestampUtc DESC
			""",
			$"""
			SELECT COUNT(*) FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			{filter};
			""",
			ReadOverview,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<MovementOverviewItem?> GetOverviewByIdAsync(
		long movementId,
		CancellationToken cancellationToken) =>
		Database.QuerySingleOrDefaultAsync(
			"""
			SELECT sm.Id, sm.TimestampUtc, inv.Id, i.Id, i.PartNumber, i.Description,
			       p.Name, w.Name, sl.Name, sm.MovementType, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			WHERE sm.Id = $MovementId;
			""",
			ReadOverview,
			cancellationToken,
			Parameter("$MovementId", movementId));

	public Task<IReadOnlyList<StockMovement>> ListRecentForInventoryAsync(
		long inventoryId,
		int count,
		CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			$"SELECT {SelectColumns} FROM StockMovements WHERE InventoryId = $InventoryId ORDER BY TimestampUtc DESC",
			ReadMovement,
			0,
			count,
			cancellationToken,
			Parameter("$InventoryId", inventoryId));

	public Task<IReadOnlyList<DashboardRecentMovement>> ListDashboardRecentAsync(
		int count,
		CancellationToken cancellationToken) =>
		Database.QuerySliceAsync(
			"""
			SELECT sm.TimestampUtc, inv.Id, i.PartNumber, i.Description, p.Name, w.Name, sl.Name,
			       sm.MovementType, sm.Quantity
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			ORDER BY sm.TimestampUtc DESC
			""",
			ReadDashboardMovement,
			0,
			count,
			cancellationToken);

	public long CreateAtomic(StockMovement movement, AuditEntry auditEntry)
		=> CreateAtomicCore(movement, auditEntry);

	public IReadOnlyList<StockMovement> GetAll() => Search(null);

	public IReadOnlyList<StockMovement> Search(string? searchText)
	{
		if (string.IsNullOrWhiteSpace(searchText))
		{
			return Database.Query(
				"""
				SELECT sm.Id, sm.InventoryId, sm.MovementType, sm.TimestampUtc,
				       sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes
				FROM StockMovements sm
				INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
				INNER JOIN Items i ON i.Id = inv.ItemId
				ORDER BY sm.TimestampUtc DESC;
				""",
				ReadMovement);
		}

		return Database.Query(
			"""
			SELECT sm.Id, sm.InventoryId, sm.MovementType, sm.TimestampUtc,
			       sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Purposes p ON p.Id = inv.PurposeId
			LEFT JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			LEFT JOIN Warehouses w ON w.Id = sl.WarehouseId
			WHERE i.PartNumber LIKE $Search
			   OR i.Description LIKE $Search
			   OR p.Name LIKE $Search
			   OR w.Name LIKE $Search
			   OR sl.Name LIKE $Search
			   OR sm.Reference LIKE $Search
			   OR sm.Notes LIKE $Search
			ORDER BY sm.TimestampUtc DESC;
			""",
			ReadMovement,
			Parameter("$Search", $"%{searchText.Trim()}%"));
	}

	public IReadOnlyList<StockMovement> GetByInventoryId(long inventoryId) =>
		Database.Query(
			$"""
			SELECT {SelectColumns}
			FROM StockMovements
			WHERE InventoryId = $InventoryId
			ORDER BY TimestampUtc;
			""",
			ReadMovement,
			Parameter("$InventoryId", inventoryId));

	private long CreateAtomicCore(StockMovement movement, AuditEntry auditEntry) =>
		Database.ExecuteInWriteTransaction(
			session =>
			{
				if (session.ExecuteScalar(
					Database.InventoryLockSql,
					Parameter("$InventoryId", movement.InventoryId)) is null)
				{
					throw new InvalidOperationException(
						$"Inventory with id '{movement.InventoryId}' was not found.");
				}

				var currentStock = Convert.ToInt64(
					session.ExecuteScalar(
						"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovements WHERE InventoryId = $InventoryId;",
						Parameter("$InventoryId", movement.InventoryId)),
					CultureInfo.InvariantCulture);
				if (currentStock + movement.Quantity < 0)
				{
					throw new InsufficientStockException();
				}

				var movementId = session.Insert(
					"""
					INSERT INTO StockMovements
					(InventoryId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes)
					VALUES
					($InventoryId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes);
					""",
					Parameter("$InventoryId", movement.InventoryId),
					Parameter("$MovementType", (int)movement.MovementType),
					Parameter("$TimestampUtc", movement.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$Quantity", movement.Quantity),
					Parameter("$UnitPrice", movement.UnitPrice),
					Parameter("$Reference", movement.Reference),
					Parameter("$Notes", movement.Notes));

				movement.Id = movementId;
				auditEntry.EntityId = movementId;
				auditEntry.AfterJson = JsonSerializer.Serialize(
					movement,
					new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

				session.Execute(
					"""
					INSERT INTO AuditEntries
					(TimestampUtc, UserId, UserEmail, EntityType, EntityId, Action, BeforeJson, AfterJson)
					VALUES
					($TimestampUtc, $UserId, $UserEmail, $EntityType, $EntityId, $Action, $BeforeJson, $AfterJson);
					""",
					Parameter("$TimestampUtc", auditEntry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$UserId", auditEntry.UserId),
					Parameter("$UserEmail", auditEntry.UserEmail),
					Parameter("$EntityType", auditEntry.EntityType),
					Parameter("$EntityId", auditEntry.EntityId),
					Parameter("$Action", auditEntry.Action),
					Parameter("$BeforeJson", auditEntry.BeforeJson),
					Parameter("$AfterJson", auditEntry.AfterJson));

				return movementId;
			});

	private static StockMovement ReadMovement(DbDataReader reader) =>
		new()
		{
			Id = reader.GetInt64(0),
			InventoryId = reader.GetInt64(1),
			MovementType = (StockMovementType)reader.GetInt32(2),
			TimestampUtc = DateTime.Parse(
				reader.GetString(3),
				CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind),
			Quantity = reader.GetInt32(4),
			UnitPrice = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
			Reference = reader.IsDBNull(6) ? null : reader.GetString(6),
			Notes = reader.IsDBNull(7) ? null : reader.GetString(7)
		};

	private static MovementOverviewItem ReadOverview(DbDataReader reader) =>
		new()
		{
			MovementId = reader.GetInt64(0),
			TimestampUtc = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
			InventoryId = reader.GetInt64(2),
			ItemId = reader.GetInt64(3),
			PartNumber = reader.GetString(4),
			Description = reader.GetString(5),
			PurposeName = reader.GetString(6),
			WarehouseName = reader.GetString(7),
			LocationName = reader.GetString(8),
			MovementType = (StockMovementType)reader.GetInt32(9),
			Quantity = reader.GetInt32(10),
			UnitPrice = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
			Reference = reader.IsDBNull(12) ? null : reader.GetString(12),
			Notes = reader.IsDBNull(13) ? null : reader.GetString(13)
		};

	private static DashboardRecentMovement ReadDashboardMovement(DbDataReader reader) =>
		new()
		{
			TimestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
			InventoryId = reader.GetInt64(1),
			PartNumber = reader.GetString(2),
			Description = reader.GetString(3),
			PurposeName = reader.GetString(4),
			WarehouseName = reader.GetString(5),
			LocationName = reader.GetString(6),
			MovementType = (StockMovementType)reader.GetInt32(7),
			Quantity = reader.GetInt32(8)
		};
}
