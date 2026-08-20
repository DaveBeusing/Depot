// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class ShipmentRepository : DatabaseRepository
{
	private const string Columns = "sh.Id,sh.ShipmentNumber,sh.SalesOrderId,so.OrderNumber,sh.CustomerId,c.Name,sh.ShipmentDate,sh.Status,sh.PackingStatus,sh.PackedAtUtc,sh.PackedByUserId,sh.Carrier,sh.TrackingNumber,sh.ShippingAddress,sh.Notes,sh.CreatedByUserId,sh.PostedByUserId,sh.PostedAtUtc,sh.ReversedAtUtc,sh.ReversedByUserId,sh.ReversalReason,sh.Version";
	private const string From = "FROM Shipments sh INNER JOIN SalesOrders so ON so.Id=sh.SalesOrderId INNER JOIN Customers c ON c.Id=sh.CustomerId";
	public ShipmentRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<Shipment>> SearchAsync(string? searchText, ShipmentStatus? status, int pageNumber, int pageSize, CancellationToken token)
	{
		var filters=new List<string>(); var parameters=new List<DatabaseParameter>();
		if(!string.IsNullOrWhiteSpace(searchText)){filters.Add("(sh.ShipmentNumber LIKE $Search OR so.OrderNumber LIKE $Search OR c.Name LIKE $Search OR sh.TrackingNumber LIKE $Search)");parameters.Add(Parameter("$Search",$"%{searchText.Trim()}%"));}
		if(status is not null){filters.Add("sh.Status=$Status");parameters.Add(Parameter("$Status",(int)status.Value));}
		var where=filters.Count==0?string.Empty:$"WHERE {string.Join(" AND ",filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {From} {where} ORDER BY sh.ShipmentDate DESC,sh.Id DESC",$"SELECT COUNT(*) {From} {where}",Read,pageNumber,pageSize,token,parameters.ToArray());
	}

	public async Task<Shipment?> GetByIdAsync(long id,CancellationToken token)
	{
		var shipment=await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE sh.Id=$Id;",Read,token,Parameter("$Id",id));
		if(shipment is null)return null; shipment.Lines=await ListLinesAsync(id,token); return shipment;
	}
	public async Task<Shipment?> GetByIdAsync(DatabaseTransactionContext tx,long id,CancellationToken token)
	{
		var rows=await tx.Session.QueryAsync($"SELECT {Columns} {From} WHERE sh.Id=$Id;",Read,token,Parameter("$Id",id));
		if(rows.Count==0)return null; var shipment=rows[0]; shipment.Lines=await ListLinesAsync(tx,id,token); return shipment;
	}
	public Task<IReadOnlyList<ShipmentLine>> ListLinesAsync(long id,CancellationToken token)=>Database.QueryAsync(LineSql+" WHERE sl.ShipmentId=$Id ORDER BY sl.Id;",ReadLine,token,Parameter("$Id",id));
	public Task<IReadOnlyList<ShipmentLine>> ListLinesAsync(DatabaseTransactionContext tx,long id,CancellationToken token)=>tx.Session.QueryAsync(LineSql+" WHERE sl.ShipmentId=$Id ORDER BY sl.Id;",ReadLine,token,Parameter("$Id",id));

	public async Task<long> CreateAsync(DatabaseTransactionContext tx,Shipment shipment,CancellationToken token)
	{
		shipment.ShipmentNumber=$"PENDING-{Guid.NewGuid():N}";
		shipment.Id=await tx.Session.InsertAsync("INSERT INTO Shipments (ShipmentNumber,SalesOrderId,CustomerId,ShipmentDate,Status,PackingStatus,Carrier,TrackingNumber,ShippingAddress,Notes,CreatedByUserId) VALUES ($Number,$OrderId,$CustomerId,$Date,$Status,$PackingStatus,$Carrier,$Tracking,$Address,$Notes,$UserId);",token,Parameters(shipment));
		shipment.ShipmentNumber=$"SH-{shipment.Id:000000}";
		await tx.Session.ExecuteAsync("UPDATE Shipments SET ShipmentNumber=$Number WHERE Id=$Id;",token,Parameter("$Number",shipment.ShipmentNumber),Parameter("$Id",shipment.Id));
		foreach(var line in shipment.Lines)
		{
			line.ShipmentId=shipment.Id;
			line.Id=await tx.Session.InsertAsync("INSERT INTO ShipmentLines (ShipmentId,SalesOrderLineId,InventoryReservationId,InventoryId,Quantity) VALUES ($ShipmentId,$OrderLineId,$ReservationId,$InventoryId,$Quantity);",token,Parameter("$ShipmentId",shipment.Id),Parameter("$OrderLineId",line.SalesOrderLineId),Parameter("$ReservationId",line.InventoryReservationId),Parameter("$InventoryId",line.InventoryId),Parameter("$Quantity",line.Quantity));
		}
		return shipment.Id;
	}

	public async Task<bool> UpdateDraftAsync(DatabaseTransactionContext tx, Shipment shipment, long version, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE Shipments SET ShipmentDate=$Date,Carrier=$Carrier,TrackingNumber=$Tracking,ShippingAddress=$Address,Notes=$Notes,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;", token,
			Parameter("$Date", shipment.ShipmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Carrier", shipment.Carrier), Parameter("$Tracking", shipment.TrackingNumber), Parameter("$Address", shipment.ShippingAddress), Parameter("$Notes", shipment.Notes), Parameter("$Id", shipment.Id), Parameter("$Version", version), Parameter("$Draft", (int)ShipmentStatus.Draft)) == 1;

	public async Task<bool> SetPackingStatusAsync(DatabaseTransactionContext tx,long id,long version,ShipmentPackingStatus status,long? userId,DateTime? packedAtUtc,CancellationToken token)=>
		await tx.Session.ExecuteAsync("UPDATE Shipments SET PackingStatus=$PackingStatus,PackedByUserId=$PackedByUserId,PackedAtUtc=$PackedAtUtc,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;",token,
			Parameter("$PackingStatus",(int)status),Parameter("$PackedByUserId",userId),Parameter("$PackedAtUtc",packedAtUtc?.ToString("O",CultureInfo.InvariantCulture)),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Draft",(int)ShipmentStatus.Draft))==1;

	public async Task<bool> PostAsync(DatabaseTransactionContext tx,long id,long version,long userId,DateTime postedAtUtc,CancellationToken token)=>
		await tx.Session.ExecuteAsync("UPDATE Shipments SET Status=$Posted,PostedByUserId=$UserId,PostedAtUtc=$At,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft AND PackingStatus=$Packed;",token,Parameter("$Posted",(int)ShipmentStatus.Posted),Parameter("$UserId",userId),Parameter("$At",postedAtUtc.ToString("O",CultureInfo.InvariantCulture)),Parameter("$Id",id),Parameter("$Version",version),Parameter("$Draft",(int)ShipmentStatus.Draft),Parameter("$Packed",(int)ShipmentPackingStatus.Packed))==1;

	public async Task<bool> ReverseAsync(DatabaseTransactionContext tx, long id, long version, long userId, DateTime reversedAtUtc, string reason, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE Shipments SET Status=$Cancelled,ReversedAtUtc=$At,ReversedByUserId=$UserId,ReversalReason=$Reason,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Posted AND ReversedAtUtc IS NULL;", token,
			Parameter("$Cancelled", (int)ShipmentStatus.Cancelled), Parameter("$At", reversedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$UserId", userId), Parameter("$Reason", reason), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Posted", (int)ShipmentStatus.Posted)) == 1;

	private static DatabaseParameter[] Parameters(Shipment s)=>[new("$Number",s.ShipmentNumber),new("$OrderId",s.SalesOrderId),new("$CustomerId",s.CustomerId),new("$Date",s.ShipmentDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),new("$Status",(int)s.Status),new("$PackingStatus",(int)s.PackingStatus),new("$Carrier",s.Carrier),new("$Tracking",s.TrackingNumber),new("$Address",s.ShippingAddress),new("$Notes",s.Notes),new("$UserId",s.CreatedByUserId)];
	private const string LineSql="SELECT sl.Id,sl.ShipmentId,sl.SalesOrderLineId,sl.InventoryReservationId,sl.InventoryId,sol.ItemId,sol.PartNumber,sol.Description,sl.Quantity,sl.Version FROM ShipmentLines sl INNER JOIN SalesOrderLines sol ON sol.Id=sl.SalesOrderLineId";
	private static Shipment Read(DbDataReader r)=>new(){Id=r.GetInt64(0),ShipmentNumber=r.GetString(1),SalesOrderId=r.GetInt64(2),SalesOrderNumber=r.GetString(3),CustomerId=r.GetInt64(4),CustomerName=r.GetString(5),ShipmentDate=Convert.ToDateTime(r.GetValue(6),CultureInfo.InvariantCulture),Status=(ShipmentStatus)r.GetInt32(7),PackingStatus=(ShipmentPackingStatus)r.GetInt32(8),PackedAtUtc=r.IsDBNull(9)?null:DateTime.Parse(r.GetString(9),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),PackedByUserId=r.IsDBNull(10)?null:r.GetInt64(10),Carrier=r.IsDBNull(11)?null:r.GetString(11),TrackingNumber=r.IsDBNull(12)?null:r.GetString(12),ShippingAddress=r.IsDBNull(13)?null:r.GetString(13),Notes=r.IsDBNull(14)?null:r.GetString(14),CreatedByUserId=r.GetInt64(15),PostedByUserId=r.IsDBNull(16)?null:r.GetInt64(16),PostedAtUtc=r.IsDBNull(17)?null:DateTime.Parse(r.GetString(17),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),ReversedAtUtc=r.IsDBNull(18)?null:DateTime.Parse(r.GetString(18),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),ReversedByUserId=r.IsDBNull(19)?null:r.GetInt64(19),ReversalReason=r.IsDBNull(20)?null:r.GetString(20),Version=r.GetInt64(21)};
	private static ShipmentLine ReadLine(DbDataReader r)=>new(){Id=r.GetInt64(0),ShipmentId=r.GetInt64(1),SalesOrderLineId=r.GetInt64(2),InventoryReservationId=r.GetInt64(3),InventoryId=r.GetInt64(4),ItemId=r.GetInt64(5),PartNumber=r.GetString(6),Description=r.GetString(7),Quantity=r.GetInt32(8),Version=r.GetInt64(9)};
}
