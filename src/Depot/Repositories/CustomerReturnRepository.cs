// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class CustomerReturnRepository : DatabaseRepository
{
	private const string Columns = "cr.Id,cr.ReturnNumber,cr.ShipmentId,cr.SalesOrderId,cr.CustomerId,cr.ReturnDate,cr.Status,cr.Reason,cr.CreatedByUserId,cr.PostedByUserId,cr.PostedAtUtc,cr.Version";
	public CustomerReturnRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<CustomerReturn>> SearchAsync(string? searchText, CustomerReturnStatus? status, int pageNumber, int pageSize, CancellationToken token)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(cr.ReturnNumber LIKE $Search OR sh.ShipmentNumber LIKE $Search OR so.OrderNumber LIKE $Search OR c.Name LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null) { filters.Add("cr.Status=$Status"); parameters.Add(Parameter("$Status", (int)status.Value)); }
		var from = "FROM CustomerReturns cr INNER JOIN Shipments sh ON sh.Id=cr.ShipmentId INNER JOIN SalesOrders so ON so.Id=cr.SalesOrderId INNER JOIN Customers c ON c.Id=cr.CustomerId";
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {from} {where} ORDER BY cr.ReturnDate DESC,cr.Id DESC", $"SELECT COUNT(*) {from} {where}", Read, pageNumber, pageSize, token, parameters.ToArray());
	}

	public async Task<CustomerReturn?> GetByIdAsync(long id, CancellationToken token)
	{
		var value = await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} FROM CustomerReturns cr WHERE cr.Id=$Id;", Read, token, Parameter("$Id", id));
		if (value is null) return null;
		value.Lines = await ListLinesAsync(id, token);
		return value;
	}

	public async Task<CustomerReturn?> GetByIdAsync(DatabaseTransactionContext tx, long id, CancellationToken token)
	{
		var rows = await tx.Session.QueryAsync($"SELECT {Columns} FROM CustomerReturns cr WHERE cr.Id=$Id;", Read, token, Parameter("$Id", id));
		if (rows.Count == 0) return null;
		var value = rows[0];
		value.Lines = await tx.Session.QueryAsync(LineSql + " WHERE crl.CustomerReturnId=$Id ORDER BY crl.Id;", ReadLine, token, Parameter("$Id", id));
		return value;
	}

	public async Task<long> CreateAsync(DatabaseTransactionContext tx, CustomerReturn value, CancellationToken token)
	{
		value.ReturnNumber = $"PENDING-{Guid.NewGuid():N}";
		value.Id = await tx.Session.InsertAsync("INSERT INTO CustomerReturns (ReturnNumber,ShipmentId,SalesOrderId,CustomerId,ReturnDate,Status,Reason,CreatedByUserId) VALUES ($Number,$ShipmentId,$OrderId,$CustomerId,$Date,$Status,$Reason,$UserId);", token,
			Parameter("$Number", value.ReturnNumber), Parameter("$ShipmentId", value.ShipmentId), Parameter("$OrderId", value.SalesOrderId), Parameter("$CustomerId", value.CustomerId), Parameter("$Date", value.ReturnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Status", (int)value.Status), Parameter("$Reason", value.Reason), Parameter("$UserId", value.CreatedByUserId));
		value.ReturnNumber = $"CR-{value.Id:000000}";
		await tx.Session.ExecuteAsync("UPDATE CustomerReturns SET ReturnNumber=$Number WHERE Id=$Id;", token, Parameter("$Number", value.ReturnNumber), Parameter("$Id", value.Id));
		foreach (var line in value.Lines)
		{
			line.CustomerReturnId = value.Id;
			line.Id = await tx.Session.InsertAsync("INSERT INTO CustomerReturnLines (CustomerReturnId,ShipmentLineId,InventoryId,Quantity) VALUES ($ReturnId,$ShipmentLineId,$InventoryId,$Quantity);", token, Parameter("$ReturnId", value.Id), Parameter("$ShipmentLineId", line.ShipmentLineId), Parameter("$InventoryId", line.InventoryId), Parameter("$Quantity", line.Quantity));
		}
		return value.Id;
	}

	public async Task<bool> PostAsync(DatabaseTransactionContext tx, long id, long version, long userId, DateTime postedAtUtc, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE CustomerReturns SET Status=$Posted,PostedByUserId=$UserId,PostedAtUtc=$At,Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;", token,
			Parameter("$Posted", (int)CustomerReturnStatus.Posted), Parameter("$UserId", userId), Parameter("$At", postedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Draft", (int)CustomerReturnStatus.Draft)) == 1;

	private Task<IReadOnlyList<CustomerReturnLine>> ListLinesAsync(long id, CancellationToken token) => Database.QueryAsync(LineSql + " WHERE crl.CustomerReturnId=$Id ORDER BY crl.Id;", ReadLine, token, Parameter("$Id", id));
	private const string LineSql = "SELECT crl.Id,crl.CustomerReturnId,crl.ShipmentLineId,crl.InventoryId,sol.ItemId,sol.PartNumber,sol.Description,crl.Quantity,crl.Version FROM CustomerReturnLines crl INNER JOIN ShipmentLines sl ON sl.Id=crl.ShipmentLineId INNER JOIN SalesOrderLines sol ON sol.Id=sl.SalesOrderLineId";
	private static CustomerReturn Read(DbDataReader r) => new() { Id=r.GetInt64(0), ReturnNumber=r.GetString(1), ShipmentId=r.GetInt64(2), SalesOrderId=r.GetInt64(3), CustomerId=r.GetInt64(4), ReturnDate=Convert.ToDateTime(r.GetValue(5),CultureInfo.InvariantCulture), Status=(CustomerReturnStatus)r.GetInt32(6), Reason=r.GetString(7), CreatedByUserId=r.GetInt64(8), PostedByUserId=r.IsDBNull(9)?null:r.GetInt64(9), PostedAtUtc=r.IsDBNull(10)?null:DateTime.Parse(r.GetString(10),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), Version=r.GetInt64(11) };
	private static CustomerReturnLine ReadLine(DbDataReader r) => new() { Id=r.GetInt64(0), CustomerReturnId=r.GetInt64(1), ShipmentLineId=r.GetInt64(2), InventoryId=r.GetInt64(3), ItemId=r.GetInt64(4), PartNumber=r.GetString(5), Description=r.GetString(6), Quantity=r.GetInt32(7), Version=r.GetInt64(8) };
}
