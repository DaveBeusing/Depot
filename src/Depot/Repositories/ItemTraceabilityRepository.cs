// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ItemTraceabilityRepository : DatabaseRepository
{
	public ItemTraceabilityRepository(DatabaseAccess database)
		: base(database)
	{
	}

	public Task<InventoryItemPolicy?> GetInventoryPolicyAsync(DatabaseTransactionContext transaction, long inventoryId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"""
			SELECT inv.Id, i.Id, i.PartNumber, i.ItemType, i.LifecycleStatus, i.TrackingMode,
			       i.LastBuyDate, i.EndOfSupportDate, i.ReplacementItemId, replacement.PartNumber
			FROM Inventories inv
			INNER JOIN Items i ON i.Id = inv.ItemId
			LEFT JOIN Items replacement ON replacement.Id = i.ReplacementItemId
			WHERE inv.Id = $InventoryId;
			""",
			ReadPolicy,
			cancellationToken,
			Parameter("$InventoryId", inventoryId));

	public Task<MovementTrackingAllocation?> GetUnitByCodeAsync(DatabaseTransactionContext transaction, long itemId, ItemTrackingMode trackingMode, string code, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync(
			"SELECT Id, Code, 0, ExpiryDate, IsBlocked, BlockReason, Version FROM ItemTrackingUnits WHERE ItemId = $ItemId AND TrackingMode = $TrackingMode AND Code = $Code;",
			ReadAllocation,
			cancellationToken,
			Parameter("$ItemId", itemId),
			Parameter("$TrackingMode", (int)trackingMode),
			Parameter("$Code", code));

	public Task<long> CreateUnitAsync(DatabaseTransactionContext transaction, long itemId, ItemTrackingMode trackingMode, string code, DateTime? expiryDate, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync(
			"INSERT INTO ItemTrackingUnits (ItemId, TrackingMode, Code, ExpiryDate, IsBlocked, CreatedAtUtc) VALUES ($ItemId, $TrackingMode, $Code, $ExpiryDate, 0, $CreatedAtUtc);",
			cancellationToken,
			Parameter("$ItemId", itemId),
			Parameter("$TrackingMode", (int)trackingMode),
			Parameter("$Code", code),
			Parameter("$ExpiryDate", expiryDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
			Parameter("$CreatedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

	public Task<int> SetExpiryIfMissingAsync(DatabaseTransactionContext transaction, long trackingUnitId, DateTime expiryDate, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"UPDATE ItemTrackingUnits SET ExpiryDate = $ExpiryDate, Version = Version + 1 WHERE Id = $Id AND ExpiryDate IS NULL;",
			cancellationToken,
			Parameter("$ExpiryDate", expiryDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
			Parameter("$Id", trackingUnitId));

	public Task<int> AddMovementAllocationAsync(DatabaseTransactionContext transaction, long movementId, long trackingUnitId, int quantity, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync(
			"INSERT INTO StockMovementTracking (StockMovementId, TrackingUnitId, Quantity) VALUES ($MovementId, $TrackingUnitId, $Quantity);",
			cancellationToken,
			Parameter("$MovementId", movementId),
			Parameter("$TrackingUnitId", trackingUnitId),
			Parameter("$Quantity", quantity));

	public Task<IReadOnlyList<MovementTrackingAllocation>> ListMovementAllocationsAsync(DatabaseTransactionContext transaction, long movementId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync(
			"""
			SELECT u.Id, u.Code, mt.Quantity, u.ExpiryDate, u.IsBlocked, u.BlockReason, u.Version
			FROM StockMovementTracking mt
			INNER JOIN ItemTrackingUnits u ON u.Id = mt.TrackingUnitId
			WHERE mt.StockMovementId = $MovementId
			ORDER BY u.Code, u.Id;
			""",
			ReadAllocation,
			cancellationToken,
			Parameter("$MovementId", movementId));

	public async Task<int> GetInventoryBalanceAsync(DatabaseTransactionContext transaction, long trackingUnitId, long inventoryId, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync(
			"""
			SELECT COALESCE(SUM(mt.Quantity), 0)
			FROM StockMovementTracking mt
			INNER JOIN StockMovements sm ON sm.Id = mt.StockMovementId
			WHERE mt.TrackingUnitId = $TrackingUnitId AND sm.InventoryId = $InventoryId;
			""",
			cancellationToken,
			Parameter("$TrackingUnitId", trackingUnitId),
			Parameter("$InventoryId", inventoryId));
		return Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	public async Task<int> GetGlobalBalanceAsync(DatabaseTransactionContext transaction, long trackingUnitId, CancellationToken cancellationToken)
	{
		var value = await transaction.Session.ExecuteScalarAsync(
			"SELECT COALESCE(SUM(Quantity), 0) FROM StockMovementTracking WHERE TrackingUnitId = $TrackingUnitId;",
			cancellationToken,
			Parameter("$TrackingUnitId", trackingUnitId));
		return Convert.ToInt32(value, CultureInfo.InvariantCulture);
	}

	public Task<PageResult<ItemTraceabilityBalance>> SearchBalancesAsync(string? searchText, long? itemId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string> { "1 = 1" };
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(i.PartNumber LIKE $Search OR u.Code LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (itemId is not null)
		{
			filters.Add("u.ItemId = $ItemId");
			parameters.Add(Parameter("$ItemId", itemId.Value));
		}
		var filter = string.Join(" AND ", filters);
		const string from = "FROM ItemTrackingUnits u INNER JOIN Items i ON i.Id = u.ItemId INNER JOIN StockMovementTracking mt ON mt.TrackingUnitId = u.Id INNER JOIN StockMovements sm ON sm.Id = mt.StockMovementId INNER JOIN Inventories inv ON inv.Id = sm.InventoryId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId INNER JOIN Purposes p ON p.Id = inv.PurposeId";
		return Database.QueryPageAsync(
			$"SELECT u.Id, u.ItemId, i.PartNumber, u.TrackingMode, u.Code, u.ExpiryDate, u.IsBlocked, u.BlockReason, inv.Id, w.Name, sl.Name, p.Name, SUM(mt.Quantity), u.Version {from} WHERE {filter} GROUP BY u.Id, u.ItemId, i.PartNumber, u.TrackingMode, u.Code, u.ExpiryDate, u.IsBlocked, u.BlockReason, inv.Id, w.Name, sl.Name, p.Name, u.Version HAVING SUM(mt.Quantity) <> 0 ORDER BY i.PartNumber, u.Code, w.Name, sl.Name",
			$"SELECT COUNT(*) FROM (SELECT u.Id, inv.Id AS InventoryId {from} WHERE {filter} GROUP BY u.Id, inv.Id HAVING SUM(mt.Quantity) <> 0) x;",
			ReadBalance,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public Task<PageResult<ItemTraceabilityHistoryEntry>> SearchHistoryAsync(string? searchText, long? trackingUnitId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string> { "1 = 1" };
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(i.PartNumber LIKE $Search OR u.Code LIKE $Search OR sm.Reference LIKE $Search OR w.Name LIKE $Search OR sl.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (trackingUnitId is not null)
		{
			filters.Add("u.Id = $TrackingUnitId");
			parameters.Add(Parameter("$TrackingUnitId", trackingUnitId.Value));
		}
		var filter = string.Join(" AND ", filters);
		const string from = "FROM StockMovementTracking mt INNER JOIN ItemTrackingUnits u ON u.Id = mt.TrackingUnitId INNER JOIN Items i ON i.Id = u.ItemId INNER JOIN StockMovements sm ON sm.Id = mt.StockMovementId INNER JOIN Inventories inv ON inv.Id = sm.InventoryId INNER JOIN StorageLocations sl ON sl.Id = inv.StorageLocationId INNER JOIN Warehouses w ON w.Id = sl.WarehouseId";
		return Database.QueryPageAsync(
			$"SELECT sm.Id, sm.TimestampUtc, u.ItemId, i.PartNumber, u.TrackingMode, u.Code, inv.Id, w.Name, sl.Name, sm.MovementType, mt.Quantity, sm.Reference {from} WHERE {filter} ORDER BY sm.TimestampUtc DESC, sm.Id DESC, u.Code",
			$"SELECT COUNT(*) {from} WHERE {filter};",
			ReadHistory,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters.ToArray());
	}

	public async Task<bool> SetBlockedAsync(long trackingUnitId, long version, bool isBlocked, string? reason, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync(
			"UPDATE ItemTrackingUnits SET IsBlocked = $IsBlocked, BlockReason = $BlockReason, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$IsBlocked", isBlocked),
			Parameter("$BlockReason", isBlocked ? reason : null),
			Parameter("$Id", trackingUnitId),
			Parameter("$Version", version)) == 1;

	private static InventoryItemPolicy ReadPolicy(DbDataReader reader) => new()
	{
		InventoryId = reader.GetInt64(0), ItemId = reader.GetInt64(1), PartNumber = reader.GetString(2),
		ItemType = (ItemType)reader.GetInt32(3), LifecycleStatus = (ItemLifecycleStatus)reader.GetInt32(4), TrackingMode = (ItemTrackingMode)reader.GetInt32(5),
		LastBuyDate = ReadNullableDate(reader, 6), EndOfSupportDate = ReadNullableDate(reader, 7),
		ReplacementItemId = reader.IsDBNull(8) ? null : reader.GetInt64(8), ReplacementPartNumber = reader.IsDBNull(9) ? null : reader.GetString(9)
	};

	private static MovementTrackingAllocation ReadAllocation(DbDataReader reader) => new()
	{
		TrackingUnitId = reader.GetInt64(0), Code = reader.GetString(1), Quantity = reader.GetInt32(2), ExpiryDate = ReadNullableDate(reader, 3),
		IsBlocked = reader.GetBoolean(4), BlockReason = reader.IsDBNull(5) ? null : reader.GetString(5), Version = reader.GetInt64(6)
	};

	private static ItemTraceabilityBalance ReadBalance(DbDataReader reader) => new()
	{
		TrackingUnitId = reader.GetInt64(0), ItemId = reader.GetInt64(1), PartNumber = reader.GetString(2), TrackingMode = (ItemTrackingMode)reader.GetInt32(3), Code = reader.GetString(4),
		ExpiryDate = ReadNullableDate(reader, 5), IsBlocked = reader.GetBoolean(6), BlockReason = reader.IsDBNull(7) ? null : reader.GetString(7), InventoryId = reader.GetInt64(8),
		Warehouse = reader.GetString(9), StorageLocation = reader.GetString(10), Purpose = reader.GetString(11), Quantity = Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture), Version = reader.GetInt64(13)
	};

	private static ItemTraceabilityHistoryEntry ReadHistory(DbDataReader reader) => new()
	{
		MovementId = reader.GetInt64(0), TimestampUtc = ReadDateTime(reader, 1), ItemId = reader.GetInt64(2), PartNumber = reader.GetString(3), TrackingMode = (ItemTrackingMode)reader.GetInt32(4),
		Code = reader.GetString(5), InventoryId = reader.GetInt64(6), Warehouse = reader.GetString(7), StorageLocation = reader.GetString(8), MovementType = (StockMovementType)reader.GetInt32(9),
		Quantity = reader.GetInt32(10), Reference = reader.IsDBNull(11) ? null : reader.GetString(11)
	};

	private static DateTime? ReadNullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ReadDateTime(reader, ordinal).Date;
	private static DateTime ReadDateTime(DbDataReader reader, int ordinal)
	{
		var value = reader.GetValue(ordinal);
		return value is DateTime dateTime ? dateTime : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
	}
}
