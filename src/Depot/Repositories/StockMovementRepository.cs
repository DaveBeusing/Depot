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
			LEFT JOIN Locations l ON l.Id = inv.LocationId
			WHERE i.PartNumber LIKE $Search
			   OR i.Description LIKE $Search
			   OR p.Name LIKE $Search
			   OR l.Name LIKE $Search
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
}
