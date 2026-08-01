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
		"Id, InventoryId, ReasonCodeId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes, ReversalOfMovementId, ReversalReason, ReversedAtUtc, ReversedByUserId";
	private const string QualifiedSelectColumns =
		"sm.Id, sm.InventoryId, sm.ReasonCodeId, sm.MovementType, sm.TimestampUtc, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes, sm.ReversalOfMovementId, sm.ReversalReason, sm.ReversedAtUtc, sm.ReversedByUserId";

	public StockMovementRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<long> CreateAsync(
		DatabaseTransactionContext transaction,
		StockMovement movement,
		CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"""
			INSERT INTO StockMovements
			(InventoryId, ReasonCodeId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes, ReversalOfMovementId, ReversalReason, ReversedAtUtc, ReversedByUserId)
			VALUES
			($InventoryId, $ReasonCodeId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes, $ReversalOfMovementId, $ReversalReason, $ReversedAtUtc, $ReversedByUserId);
			""",
			cancellationToken,
			Parameter("$InventoryId", movement.InventoryId),
			Parameter("$ReasonCodeId", movement.ReasonCodeId),
			Parameter("$MovementType", (int)movement.MovementType),
			Parameter("$TimestampUtc", movement.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
			Parameter("$Quantity", movement.Quantity),
			Parameter("$UnitPrice", movement.UnitPrice),
			Parameter("$Reference", movement.Reference),
			Parameter("$Notes", movement.Notes),
			Parameter("$ReversalOfMovementId", movement.ReversalOfMovementId),
			Parameter("$ReversalReason", movement.ReversalReason),
			Parameter("$ReversedAtUtc", movement.ReversedAtUtc is null ? null : DateTimeValue(movement.ReversedAtUtc.Value)),
			Parameter("$ReversedByUserId", movement.ReversedByUserId));

	public Task<StockMovement?> GetByIdAsync(
		DatabaseTransactionContext transaction,
		long movementId,
		CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			$"SELECT {SelectColumns} FROM StockMovements WHERE Id = $MovementId;",
			ReadMovement,
			cancellationToken,
			Parameter("$MovementId", movementId));

	public Task<IReadOnlyList<StockMovement>> ListOriginalsByReferenceAsync(
		DatabaseTransactionContext transaction,
		string reference,
		CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			$"SELECT {SelectColumns} FROM StockMovements WHERE Reference = $Reference AND ReversalOfMovementId IS NULL ORDER BY Id;",
			ReadMovement,
			cancellationToken,
			Parameter("$Reference", reference));

	public Task<IReadOnlyList<long>> ListReversedOriginalIdsAsync(
		DatabaseTransactionContext transaction,
		IEnumerable<long> movementIds,
		CancellationToken cancellationToken)
	{
		var ids = movementIds.Distinct().OrderBy(id => id).ToArray();
		if (ids.Length == 0) return Task.FromResult<IReadOnlyList<long>>([]);
		var parameters = ids.Select((id, index) => Parameter($"$MovementId{index}", id)).ToArray();
		var parameterList = string.Join(", ", parameters.Select(parameter => parameter.Name));
		return transaction.Session.QueryAsync(
			$"SELECT ReversalOfMovementId FROM StockMovements WHERE ReversalOfMovementId IN ({parameterList}) ORDER BY ReversalOfMovementId;",
			reader => reader.GetInt64(0),
			cancellationToken,
			parameters);
	}

	public Task<IReadOnlyList<InventoryStockQuantity>> GetCurrentQuantitiesAsync(
		DatabaseTransactionContext transaction,
		IEnumerable<long> inventoryIds,
		CancellationToken cancellationToken)
	{
		var ids = inventoryIds.Distinct().OrderBy(id => id).ToArray();
		if (ids.Length == 0)
		{
			return Task.FromResult<IReadOnlyList<InventoryStockQuantity>>([]);
		}

		var parameters = ids
			.Select((id, index) => Parameter($"$InventoryId{index}", id))
			.ToArray();
		var parameterList = string.Join(", ", parameters.Select(parameter => parameter.Name));
		var quantity = Database.CastToInt64("Quantity");
		return transaction.Session.QueryAsync(
			$"SELECT InventoryId, COALESCE(SUM({quantity}), 0) FROM StockMovements WHERE InventoryId IN ({parameterList}) GROUP BY InventoryId ORDER BY InventoryId;",
			reader => new InventoryStockQuantity
			{
				InventoryId = reader.GetInt64(0),
				Quantity = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)
			},
			cancellationToken,
			parameters);
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
					(InventoryId, ReasonCodeId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes, ReversalOfMovementId, ReversalReason, ReversedAtUtc, ReversedByUserId)
					VALUES
					($InventoryId, $ReasonCodeId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes, $ReversalOfMovementId, $ReversalReason, $ReversedAtUtc, $ReversedByUserId);
					""",
					token,
					Parameter("$InventoryId", movement.InventoryId),
					Parameter("$ReasonCodeId", movement.ReasonCodeId),
					Parameter("$MovementType", (int)movement.MovementType),
					Parameter("$TimestampUtc", movement.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$Quantity", movement.Quantity),
					Parameter("$UnitPrice", movement.UnitPrice),
					Parameter("$Reference", movement.Reference),
					Parameter("$Notes", movement.Notes),
					Parameter("$ReversalOfMovementId", movement.ReversalOfMovementId),
					Parameter("$ReversalReason", movement.ReversalReason),
					Parameter("$ReversedAtUtc", movement.ReversedAtUtc is null ? null : DateTimeValue(movement.ReversedAtUtc.Value)),
					Parameter("$ReversedByUserId", movement.ReversedByUserId));

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
			? "WHERE i.PartNumber LIKE $Search OR i.Description LIKE $Search OR p.Name LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search OR rc.Name LIKE $Search OR sm.Reference LIKE $Search OR sm.Notes LIKE $Search"
			: string.Empty;
		var parameters = hasSearch
			? new[] { Parameter("$Search", $"%{search}%") }
			: [];
		return Database.QueryPageAsync(
			$"""
			SELECT sm.Id, sm.TimestampUtc, inv.Id, i.Id, i.PartNumber, i.Description,
			       p.Name, w.Name, sl.Name, rc.Name, sm.MovementType, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes,
			       sm.ReversalOfMovementId, CASE WHEN EXISTS (SELECT 1 FROM StockMovements reversal WHERE reversal.ReversalOfMovementId = sm.Id) THEN 1 ELSE 0 END
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
			{filter}
			ORDER BY sm.TimestampUtc DESC, sm.Id DESC
			""",
			$"""
			SELECT COUNT(*) FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
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
			       p.Name, w.Name, sl.Name, rc.Name, sm.MovementType, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes,
			       sm.ReversalOfMovementId, CASE WHEN EXISTS (SELECT 1 FROM StockMovements reversal WHERE reversal.ReversalOfMovementId = sm.Id) THEN 1 ELSE 0 END
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
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
			$"SELECT {QualifiedSelectColumns}, rc.Name FROM StockMovements sm LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId WHERE sm.InventoryId = $InventoryId ORDER BY sm.TimestampUtc DESC, sm.Id DESC",
			ReadMovementWithReason,
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
			       rc.Name, sm.MovementType, sm.Quantity
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
			ORDER BY sm.TimestampUtc DESC, sm.Id DESC
			""",
			ReadDashboardMovement,
			0,
			count,
			cancellationToken);

	public Task<IReadOnlyList<MovementOverviewItem>> ListByReferenceAsync(
		string reference,
		CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"""
			SELECT sm.Id, sm.TimestampUtc, inv.Id, i.Id, i.PartNumber, i.Description,
			       p.Name, w.Name, sl.Name, rc.Name, sm.MovementType, sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes,
			       sm.ReversalOfMovementId, CASE WHEN EXISTS (SELECT 1 FROM StockMovements reversal WHERE reversal.ReversalOfMovementId = sm.Id) THEN 1 ELSE 0 END
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			INNER JOIN Purposes p ON p.Id = inv.PurposeId
			INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			INNER JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
			WHERE sm.Reference = $Reference
			ORDER BY sm.Id;
			""",
			ReadOverview,
			cancellationToken,
			Parameter("$Reference", reference));

	public long CreateAtomic(StockMovement movement, AuditEntry auditEntry)
		=> CreateAtomicCore(movement, auditEntry);

	public IReadOnlyList<StockMovement> Search(string? searchText)
	{
		if (string.IsNullOrWhiteSpace(searchText))
		{
			return Database.Query(
				"""
				SELECT sm.Id, sm.InventoryId, sm.ReasonCodeId, sm.MovementType, sm.TimestampUtc,
				       sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes, sm.ReversalOfMovementId, sm.ReversalReason, sm.ReversedAtUtc, sm.ReversedByUserId
				FROM StockMovements sm
				INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
				INNER JOIN Items i ON i.Id = inv.ItemId
				ORDER BY sm.TimestampUtc DESC;
				""",
				ReadMovement);
		}

		return Database.Query(
			"""
			SELECT sm.Id, sm.InventoryId, sm.ReasonCodeId, sm.MovementType, sm.TimestampUtc,
			       sm.Quantity, sm.UnitPrice, sm.Reference, sm.Notes, sm.ReversalOfMovementId, sm.ReversalReason, sm.ReversedAtUtc, sm.ReversedByUserId
			FROM StockMovements sm
			INNER JOIN Inventories inv ON inv.Id = sm.InventoryId
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Purposes p ON p.Id = inv.PurposeId
			LEFT JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId
			LEFT JOIN Warehouses w ON w.Id = sl.WarehouseId
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
			WHERE i.PartNumber LIKE $Search
			   OR i.Description LIKE $Search
			   OR p.Name LIKE $Search
			   OR w.Name LIKE $Search
			   OR sl.Name LIKE $Search
			   OR rc.Name LIKE $Search
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
			SELECT {QualifiedSelectColumns}, rc.Name
			FROM StockMovements sm
			LEFT JOIN ReasonCodes rc ON rc.Id = sm.ReasonCodeId
			WHERE sm.InventoryId = $InventoryId
			ORDER BY sm.TimestampUtc;
			""",
			ReadMovementWithReason,
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
					(InventoryId, ReasonCodeId, MovementType, TimestampUtc, Quantity, UnitPrice, Reference, Notes, ReversalOfMovementId, ReversalReason, ReversedAtUtc, ReversedByUserId)
					VALUES
					($InventoryId, $ReasonCodeId, $MovementType, $TimestampUtc, $Quantity, $UnitPrice, $Reference, $Notes, $ReversalOfMovementId, $ReversalReason, $ReversedAtUtc, $ReversedByUserId);
					""",
					Parameter("$InventoryId", movement.InventoryId),
					Parameter("$ReasonCodeId", movement.ReasonCodeId),
					Parameter("$MovementType", (int)movement.MovementType),
					Parameter("$TimestampUtc", movement.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
					Parameter("$Quantity", movement.Quantity),
					Parameter("$UnitPrice", movement.UnitPrice),
					Parameter("$Reference", movement.Reference),
					Parameter("$Notes", movement.Notes),
					Parameter("$ReversalOfMovementId", movement.ReversalOfMovementId),
					Parameter("$ReversalReason", movement.ReversalReason),
					Parameter("$ReversedAtUtc", movement.ReversedAtUtc is null ? null : DateTimeValue(movement.ReversedAtUtc.Value)),
					Parameter("$ReversedByUserId", movement.ReversedByUserId));

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
			ReasonCodeId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
			MovementType = (StockMovementType)reader.GetInt32(3),
			TimestampUtc = DateTime.Parse(
				reader.GetString(4),
				CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind),
			Quantity = reader.GetInt32(5),
			UnitPrice = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
			Reference = reader.IsDBNull(7) ? null : reader.GetString(7),
			Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
			ReversalOfMovementId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
			ReversalReason = reader.IsDBNull(10) ? null : reader.GetString(10),
			ReversedAtUtc = reader.IsDBNull(11) ? null : ParseDateTime(reader.GetString(11)),
			ReversedByUserId = reader.IsDBNull(12) ? null : reader.GetInt64(12)
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
			ReasonCodeName = reader.IsDBNull(9) ? null : reader.GetString(9),
			MovementType = (StockMovementType)reader.GetInt32(10),
			Quantity = reader.GetInt32(11),
			UnitPrice = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
			Reference = reader.IsDBNull(13) ? null : reader.GetString(13),
			Notes = reader.IsDBNull(14) ? null : reader.GetString(14),
			ReversalOfMovementId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
			IsReversed = reader.GetBoolean(16)
		};

	private static StockMovement ReadMovementWithReason(DbDataReader reader)
	{
		var movement = ReadMovement(reader);
		movement.ReasonCodeName = reader.IsDBNull(13) ? null : reader.GetString(13);
		return movement;
	}

	private static string DateTimeValue(DateTime value) =>
		value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static DateTime ParseDateTime(string value) =>
		DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

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
			ReasonCodeName = reader.IsDBNull(7) ? null : reader.GetString(7),
			MovementType = (StockMovementType)reader.GetInt32(8),
			Quantity = reader.GetInt32(9)
		};
}
