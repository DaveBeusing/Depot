// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class SalesOrderRepository : DatabaseRepository
{
	private const string Columns = "so.Id, so.OrderNumber, so.CustomerId, c.Name, so.BillingAddress, so.ShippingAddress, so.OrderDate, so.RequestedDeliveryDate, so.Currency, so.CustomerReference, so.Notes, so.Status, so.CreatedByUserId, so.SubmittedByUserId, so.SubmittedAtUtc, so.ApprovalDecisionByUserId, so.ApprovalDecisionAtUtc, so.ApprovalComment, so.ReleasedByUserId, so.ReleasedAtUtc, so.CancelledByUserId, so.CancelledAtUtc, so.CancelReason, so.Version";
	private const string From = "FROM SalesOrders so INNER JOIN Customers c ON c.Id = so.CustomerId";
	public SalesOrderRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<SalesOrder>> SearchAsync(string? searchText, SalesOrderStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>(); var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText)) { filters.Add("(so.OrderNumber LIKE $Search OR c.Name LIKE $Search OR so.CustomerReference LIKE $Search)"); parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%")); }
		if (status is not null) { filters.Add("so.Status=$Status"); parameters.Add(Parameter("$Status", (int)status.Value)); }
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync($"SELECT {Columns} {From} {where} ORDER BY so.OrderDate DESC, so.Id DESC", $"SELECT COUNT(*) {From} {where}", ReadOrder, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public async Task<SalesOrder?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var order = await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE so.Id=$Id;", ReadOrder, cancellationToken, Parameter("$Id", id));
		if (order is null) return null;
		order.Lines = await ListLinesAsync(id, cancellationToken);
		return order;
	}

	public async Task<SalesOrder?> GetByIdAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		var rows = await transaction.Session.QueryAsync($"SELECT {Columns} {From} WHERE so.Id=$Id;", ReadOrder, cancellationToken, Parameter("$Id", id));
		if (rows.Count == 0) return null;
		var order = rows[0]; order.Lines = await ListLinesAsync(transaction, id, cancellationToken); return order;
	}

	public Task<IReadOnlyList<SalesOrderLine>> ListLinesAsync(long id, CancellationToken cancellationToken) => Database.QueryAsync(LineSql + " WHERE sol.SalesOrderId=$Id ORDER BY sol.LineNumber;", ReadLine, cancellationToken, Parameter("$Id", id));
	public Task<IReadOnlyList<SalesOrderLine>> ListLinesAsync(DatabaseTransactionContext tx, long id, CancellationToken token) => tx.Session.QueryAsync(LineSql + " WHERE sol.SalesOrderId=$Id ORDER BY sol.LineNumber;", ReadLine, token, Parameter("$Id", id));

	public Task<SalesOrder> SaveDraftAsync(SalesOrder order, CancellationToken cancellationToken) => Database.ExecuteInWriteTransactionAsync(async (session, token) =>
	{
		var tx = new DatabaseTransactionContext(session);
		if (order.Id == 0)
		{
			order.OrderNumber = $"PENDING-{Guid.NewGuid():N}";
			order.Id = await session.InsertAsync("INSERT INTO SalesOrders (OrderNumber, CustomerId, BillingAddress, ShippingAddress, OrderDate, RequestedDeliveryDate, Currency, CustomerReference, Notes, Status, CreatedByUserId) VALUES ($OrderNumber,$CustomerId,$BillingAddress,$ShippingAddress,$OrderDate,$RequestedDeliveryDate,$Currency,$CustomerReference,$Notes,$Status,$CreatedByUserId);", token, OrderParameters(order));
			order.OrderNumber = $"SO-{order.Id:000000}";
			await session.ExecuteAsync("UPDATE SalesOrders SET OrderNumber=$OrderNumber WHERE Id=$Id;", token, Parameter("$OrderNumber", order.OrderNumber), Parameter("$Id", order.Id));
		}
		else
		{
			var updated = await session.ExecuteAsync("UPDATE SalesOrders SET CustomerId=$CustomerId, BillingAddress=$BillingAddress, ShippingAddress=$ShippingAddress, OrderDate=$OrderDate, RequestedDeliveryDate=$RequestedDeliveryDate, Currency=$Currency, CustomerReference=$CustomerReference, Notes=$Notes, Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$Draft;", token,
				[.. OrderParameters(order), Parameter("$Id", order.Id), Parameter("$Version", order.Version), Parameter("$Draft", (int)SalesOrderStatus.Draft)]);
			if (updated != 1) throw new Services.ConcurrencyConflictException("sales order"); order.Version++;
		}
		var existing = await session.QueryAsync("SELECT Id FROM SalesOrderLines WHERE SalesOrderId=$Id;", r => r.GetInt64(0), token, Parameter("$Id", order.Id));
		foreach (var id in existing.Where(id => order.Lines.All(line => line.Id != id))) await session.ExecuteAsync("DELETE FROM SalesOrderLines WHERE Id=$Id AND SalesOrderId=$OrderId AND ReservedQuantity=0 AND ShippedQuantity=0 AND InvoicedQuantity=0;", token, Parameter("$Id", id), Parameter("$OrderId", order.Id));
		var number = 1;
		foreach (var line in order.Lines)
		{
			line.SalesOrderId = order.Id; line.LineNumber = number++;
			if (line.Id == 0) line.Id = await session.InsertAsync("INSERT INTO SalesOrderLines (SalesOrderId,LineNumber,ItemId,PartNumber,Description,Quantity,UnitPrice,DiscountPercent,TaxRate) VALUES ($SalesOrderId,$LineNumber,$ItemId,$PartNumber,$Description,$Quantity,$UnitPrice,$DiscountPercent,$TaxRate);", token, LineParameters(line));
			else
			{
				var updated = await session.ExecuteAsync("UPDATE SalesOrderLines SET LineNumber=$LineNumber,ItemId=$ItemId,PartNumber=$PartNumber,Description=$Description,Quantity=$Quantity,UnitPrice=$UnitPrice,DiscountPercent=$DiscountPercent,TaxRate=$TaxRate,Version=Version+1 WHERE Id=$Id AND SalesOrderId=$SalesOrderId AND Version=$Version AND ReservedQuantity=0 AND ShippedQuantity=0;", token, [.. LineParameters(line), Parameter("$Id", line.Id), Parameter("$Version", line.Version)]);
				if (updated != 1) throw new Services.ConcurrencyConflictException("sales order line"); line.Version++;
			}
		}
		return await GetByIdAsync(tx, order.Id, token) ?? order;
	}, cancellationToken);

	public async Task<bool> SetStatusAsync(DatabaseTransactionContext tx, SalesOrder order, long expectedVersion, SalesOrderStatus expectedStatus, CancellationToken token)
	{
		var updated = await tx.Session.ExecuteAsync("UPDATE SalesOrders SET Status=$Status, SubmittedByUserId=$SubmittedByUserId, SubmittedAtUtc=$SubmittedAtUtc, ApprovalDecisionByUserId=$ApprovalDecisionByUserId, ApprovalDecisionAtUtc=$ApprovalDecisionAtUtc, ApprovalComment=$ApprovalComment, ReleasedByUserId=$ReleasedByUserId, ReleasedAtUtc=$ReleasedAtUtc, CancelledByUserId=$CancelledByUserId, CancelledAtUtc=$CancelledAtUtc, CancelReason=$CancelReason, Version=Version+1 WHERE Id=$Id AND Version=$Version AND Status=$ExpectedStatus;", token,
			Parameter("$Status", (int)order.Status), Parameter("$SubmittedByUserId", order.SubmittedByUserId), Parameter("$SubmittedAtUtc", Utc(order.SubmittedAtUtc)), Parameter("$ApprovalDecisionByUserId", order.ApprovalDecisionByUserId), Parameter("$ApprovalDecisionAtUtc", Utc(order.ApprovalDecisionAtUtc)), Parameter("$ApprovalComment", order.ApprovalComment), Parameter("$ReleasedByUserId", order.ReleasedByUserId), Parameter("$ReleasedAtUtc", Utc(order.ReleasedAtUtc)), Parameter("$CancelledByUserId", order.CancelledByUserId), Parameter("$CancelledAtUtc", Utc(order.CancelledAtUtc)), Parameter("$CancelReason", order.CancelReason), Parameter("$Id", order.Id), Parameter("$Version", expectedVersion), Parameter("$ExpectedStatus", (int)expectedStatus));
		return updated == 1;
	}

	public async Task UpdateLineQuantitiesAsync(DatabaseTransactionContext tx, long lineId, int reserved, int shipped, int invoiced, CancellationToken token) =>
		await tx.Session.ExecuteAsync("UPDATE SalesOrderLines SET ReservedQuantity=$Reserved, ShippedQuantity=$Shipped, InvoicedQuantity=$Invoiced, Version=Version+1 WHERE Id=$Id;", token, Parameter("$Reserved", reserved), Parameter("$Shipped", shipped), Parameter("$Invoiced", invoiced), Parameter("$Id", lineId));

	private static DatabaseParameter[] OrderParameters(SalesOrder o) =>
	[
		new("$OrderNumber", o.OrderNumber), new("$CustomerId", o.CustomerId), new("$BillingAddress", o.BillingAddress), new("$ShippingAddress", o.ShippingAddress), new("$OrderDate", Date(o.OrderDate)), new("$RequestedDeliveryDate", Date(o.RequestedDeliveryDate)), new("$Currency", o.Currency), new("$CustomerReference", o.CustomerReference), new("$Notes", o.Notes), new("$Status", (int)o.Status), new("$CreatedByUserId", o.CreatedByUserId)
	];
	private static DatabaseParameter[] LineParameters(SalesOrderLine l) =>
	[
		new("$SalesOrderId", l.SalesOrderId), new("$LineNumber", l.LineNumber), new("$ItemId", l.ItemId), new("$PartNumber", l.PartNumber), new("$Description", l.Description), new("$Quantity", l.Quantity), new("$UnitPrice", l.UnitPrice), new("$DiscountPercent", l.DiscountPercent), new("$TaxRate", l.TaxRate)
	];
	private const string LineSql = "SELECT sol.Id,sol.SalesOrderId,sol.LineNumber,sol.ItemId,sol.PartNumber,sol.Description,sol.Quantity,sol.UnitPrice,sol.DiscountPercent,sol.TaxRate,sol.ReservedQuantity,sol.ShippedQuantity,sol.InvoicedQuantity,sol.Version FROM SalesOrderLines sol";
	private static SalesOrder ReadOrder(DbDataReader r) => new() { Id=r.GetInt64(0), OrderNumber=r.GetString(1), CustomerId=r.GetInt64(2), CustomerName=r.GetString(3), BillingAddress=r.IsDBNull(4)?null:r.GetString(4), ShippingAddress=r.IsDBNull(5)?null:r.GetString(5), OrderDate=ParseDate(r.GetValue(6)), RequestedDeliveryDate=r.IsDBNull(7)?null:ParseDate(r.GetValue(7)), Currency=r.GetString(8), CustomerReference=r.IsDBNull(9)?null:r.GetString(9), Notes=r.IsDBNull(10)?null:r.GetString(10), Status=(SalesOrderStatus)r.GetInt32(11), CreatedByUserId=r.IsDBNull(12)?null:r.GetInt64(12), SubmittedByUserId=r.IsDBNull(13)?null:r.GetInt64(13), SubmittedAtUtc=ParseUtc(r,14), ApprovalDecisionByUserId=r.IsDBNull(15)?null:r.GetInt64(15), ApprovalDecisionAtUtc=ParseUtc(r,16), ApprovalComment=r.IsDBNull(17)?null:r.GetString(17), ReleasedByUserId=r.IsDBNull(18)?null:r.GetInt64(18), ReleasedAtUtc=ParseUtc(r,19), CancelledByUserId=r.IsDBNull(20)?null:r.GetInt64(20), CancelledAtUtc=ParseUtc(r,21), CancelReason=r.IsDBNull(22)?null:r.GetString(22), Version=r.GetInt64(23) };
	private static SalesOrderLine ReadLine(DbDataReader r) => new() { Id=r.GetInt64(0), SalesOrderId=r.GetInt64(1), LineNumber=r.GetInt32(2), ItemId=r.GetInt64(3), PartNumber=r.GetString(4), Description=r.GetString(5), Quantity=r.GetInt32(6), UnitPrice=r.GetDecimal(7), DiscountPercent=r.GetDecimal(8), TaxRate=r.GetDecimal(9), ReservedQuantity=r.GetInt32(10), ShippedQuantity=r.GetInt32(11), InvoicedQuantity=r.GetInt32(12), Version=r.GetInt64(13) };
	private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static object? Date(DateTime? value) => value is null ? null : Date(value.Value);
	private static object? Utc(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ParseDate(object value) => Convert.ToDateTime(value, CultureInfo.InvariantCulture);
	private static DateTime? ParseUtc(DbDataReader r,int i) => r.IsDBNull(i)?null:DateTime.Parse(r.GetString(i), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
