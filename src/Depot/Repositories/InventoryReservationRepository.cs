// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class InventoryReservationRepository : DatabaseRepository
{
	public InventoryReservationRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<InventoryReservation>> ListByOrderAsync(long salesOrderId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"SELECT r.Id,r.SalesOrderLineId,r.InventoryId,r.Quantity,r.Status,r.CreatedAtUtc,r.CreatedByUserId,r.ReleasedAtUtc,r.ReleasedByUserId,r.Version,i.PartNumber,w.Name,sl.Name FROM InventoryReservations r INNER JOIN SalesOrderLines sol ON sol.Id=r.SalesOrderLineId INNER JOIN Inventories inv ON inv.Id=r.InventoryId INNER JOIN Items i ON i.Id=inv.ItemId INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId INNER JOIN Warehouses w ON w.Id=sl.WarehouseId WHERE sol.SalesOrderId=$OrderId ORDER BY sol.LineNumber,r.Id;",
			Read,
			cancellationToken,
			Parameter("$OrderId", salesOrderId));

	public Task<IReadOnlyList<InventoryReservation>> ListActiveByOrderAsync(DatabaseTransactionContext tx, long salesOrderId, CancellationToken token) =>
		tx.Session.QueryAsync(
			"SELECT r.Id,r.SalesOrderLineId,r.InventoryId,r.Quantity,r.Status,r.CreatedAtUtc,r.CreatedByUserId,r.ReleasedAtUtc,r.ReleasedByUserId,r.Version,i.PartNumber,w.Name,sl.Name FROM InventoryReservations r INNER JOIN SalesOrderLines sol ON sol.Id=r.SalesOrderLineId INNER JOIN Inventories inv ON inv.Id=r.InventoryId INNER JOIN Items i ON i.Id=inv.ItemId INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId INNER JOIN Warehouses w ON w.Id=sl.WarehouseId WHERE sol.SalesOrderId=$OrderId AND r.Status=$Status ORDER BY sol.LineNumber,r.Id;",
			Read, token, Parameter("$OrderId", salesOrderId), Parameter("$Status", (int)InventoryReservationStatus.Active));

	public Task<IReadOnlyList<SalesInventoryAvailability>> SearchAvailabilityAsync(long itemId, string? searchText, CancellationToken cancellationToken)
	{
		var filter = string.IsNullOrWhiteSpace(searchText) ? string.Empty : "AND (w.Name LIKE $Search OR sl.Name LIKE $Search OR p.Name LIKE $Search)";
		var parameters = new List<DatabaseParameter> { Parameter("$ItemId", itemId), Parameter("$ActiveStatus", (int)InventoryReservationStatus.Active) };
		if (!string.IsNullOrWhiteSpace(searchText)) parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		return Database.QuerySliceAsync(
			$"SELECT inv.Id,inv.ItemId,i.PartNumber,i.Description,w.Name,sl.Name,p.Name,COALESCE((SELECT SUM(sm.Quantity) FROM StockMovements sm WHERE sm.InventoryId=inv.Id),0),COALESCE((SELECT SUM(r.Quantity) FROM InventoryReservations r WHERE r.InventoryId=inv.Id AND r.Status=$ActiveStatus),0) FROM Inventories inv INNER JOIN Items i ON i.Id=inv.ItemId INNER JOIN Purposes p ON p.Id=inv.PurposeId INNER JOIN StorageLocations sl ON sl.Id=inv.StorageLocationId INNER JOIN Warehouses w ON w.Id=sl.WarehouseId WHERE inv.ItemId=$ItemId AND inv.IsActive=1 AND sl.IsActive=1 AND w.IsActive=1 {filter} ORDER BY w.Name,sl.Name,p.Name,inv.Id",
			ReadAvailability,0,200,cancellationToken,parameters.ToArray());
	}

	public async Task<long> CreateAsync(DatabaseTransactionContext tx, InventoryReservation reservation, CancellationToken token) =>
		await tx.Session.InsertAsync("INSERT INTO InventoryReservations (SalesOrderLineId,InventoryId,Quantity,Status,CreatedAtUtc,CreatedByUserId) VALUES ($SalesOrderLineId,$InventoryId,$Quantity,$Status,$CreatedAtUtc,$CreatedByUserId);", token,
			Parameter("$SalesOrderLineId", reservation.SalesOrderLineId), Parameter("$InventoryId", reservation.InventoryId), Parameter("$Quantity", reservation.Quantity), Parameter("$Status", (int)reservation.Status), Parameter("$CreatedAtUtc", reservation.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)), Parameter("$CreatedByUserId", reservation.CreatedByUserId));

	public async Task ReleaseOrderAsync(DatabaseTransactionContext tx, long salesOrderId, long userId, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE InventoryReservations SET Status=$Released,ReleasedAtUtc=$ReleasedAtUtc,ReleasedByUserId=$UserId,Version=Version+1 WHERE Status=$Active AND SalesOrderLineId IN (SELECT Id FROM SalesOrderLines WHERE SalesOrderId=$OrderId);", token,
			Parameter("$Released", (int)InventoryReservationStatus.Released), Parameter("$ReleasedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)), Parameter("$UserId", userId), Parameter("$Active", (int)InventoryReservationStatus.Active), Parameter("$OrderId", salesOrderId));

	public async Task ConsumeAsync(DatabaseTransactionContext tx, long reservationId, int quantity, long userId, CancellationToken token)
	{
		var rows = await tx.Session.QueryAsync("SELECT Quantity,Status,Version FROM InventoryReservations WHERE Id=$Id;", r => new { Quantity=r.GetInt32(0), Status=(InventoryReservationStatus)r.GetInt32(1), Version=r.GetInt64(2) }, token, Parameter("$Id", reservationId));
		if (rows.Count == 0 || rows[0].Status != InventoryReservationStatus.Active || quantity <= 0 || quantity > rows[0].Quantity) throw new InvalidOperationException("The inventory reservation cannot be consumed.");
		if (quantity == rows[0].Quantity)
		{
			var updated = await tx.Session.ExecuteAsync("UPDATE InventoryReservations SET Status=$Consumed,ReleasedAtUtc=$At,ReleasedByUserId=$UserId,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Active;", token, Parameter("$Consumed",(int)InventoryReservationStatus.Consumed),Parameter("$At",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)),Parameter("$UserId",userId),Parameter("$Id",reservationId),Parameter("$Version",rows[0].Version),Parameter("$Active",(int)InventoryReservationStatus.Active));
			if(updated!=1) throw new Services.ConcurrencyConflictException("inventory reservation");
		}
		else
		{
			var updated = await tx.Session.ExecuteAsync("UPDATE InventoryReservations SET Quantity=Quantity-$Quantity,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Active;", token, Parameter("$Quantity",quantity),Parameter("$Id",reservationId),Parameter("$Version",rows[0].Version),Parameter("$Active",(int)InventoryReservationStatus.Active));
			if(updated!=1) throw new Services.ConcurrencyConflictException("inventory reservation");
		}
	}

	private static InventoryReservation Read(DbDataReader r) => new() { Id=r.GetInt64(0), SalesOrderLineId=r.GetInt64(1), InventoryId=r.GetInt64(2), Quantity=r.GetInt32(3), Status=(InventoryReservationStatus)r.GetInt32(4), CreatedAtUtc=DateTime.Parse(r.GetString(5),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), CreatedByUserId=r.GetInt64(6), ReleasedAtUtc=r.IsDBNull(7)?null:DateTime.Parse(r.GetString(7),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), ReleasedByUserId=r.IsDBNull(8)?null:r.GetInt64(8), Version=r.GetInt64(9), InventoryDisplay=$"{r.GetString(10)} · {r.GetString(11)} / {r.GetString(12)}" };
	private static SalesInventoryAvailability ReadAvailability(DbDataReader r) => new() { InventoryId=r.GetInt64(0), ItemId=r.GetInt64(1), PartNumber=r.GetString(2), Description=r.GetString(3), WarehouseName=r.GetString(4), StorageLocationName=r.GetString(5), PurposeName=r.GetString(6), OnHand=Convert.ToInt64(r.GetValue(7),CultureInfo.InvariantCulture), Reserved=Convert.ToInt64(r.GetValue(8),CultureInfo.InvariantCulture) };
}
