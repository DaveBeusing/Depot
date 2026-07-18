// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;
using Depot.Services;

namespace Depot.Repositories;

public sealed class PurchaseOrderRepository : DatabaseRepository
{
	private const string Columns = "po.Id, po.OrderNumber, po.SupplierId, s.Name, po.OrderDate, po.ExpectedDeliveryDate, po.Notes, po.Status, po.Version";
	private const string From = "FROM PurchaseOrders po INNER JOIN Suppliers s ON s.Id = po.SupplierId";

	public PurchaseOrderRepository(DatabaseAccess database) : base(database) { }

	public Task<PageResult<PurchaseOrder>> SearchAsync(string? searchText, PurchaseOrderStatus? status, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		var filters = new List<string>();
		var parameters = new List<DatabaseParameter>();
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			filters.Add("(po.OrderNumber LIKE $Search OR s.Name LIKE $Search OR po.Notes LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{searchText.Trim()}%"));
		}
		if (status is not null)
		{
			filters.Add("po.Status = $Status");
			parameters.Add(Parameter("$Status", (int)status.Value));
		}
		var where = filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
		return Database.QueryPageAsync(
			$"SELECT {Columns} {From} {where} ORDER BY po.OrderDate DESC, po.Id DESC",
			$"SELECT COUNT(*) {From} {where}", ReadOrder, pageNumber, pageSize, cancellationToken, parameters.ToArray());
	}

	public async Task<PurchaseOrder?> GetByIdAsync(long id, CancellationToken cancellationToken)
	{
		var order = await Database.QuerySingleOrDefaultAsync($"SELECT {Columns} {From} WHERE po.Id = $Id;", ReadOrder, cancellationToken, Parameter("$Id", id));
		if (order is null) return null;
		order.Lines = await ListLinesAsync(id, cancellationToken);
		return order;
	}

	public Task<IReadOnlyList<PurchaseOrderLine>> ListLinesAsync(long purchaseOrderId, CancellationToken cancellationToken) =>
		Database.QueryAsync(
			"SELECT pol.Id, pol.PurchaseOrderId, pol.LineNumber, pol.ItemId, i.PartNumber, i.Description, pol.Quantity, pol.UnitPrice, pol.ReceivedQuantity, pol.Version FROM PurchaseOrderLines pol INNER JOIN Items i ON i.Id = pol.ItemId WHERE pol.PurchaseOrderId = $PurchaseOrderId ORDER BY pol.LineNumber;",
			ReadLine, cancellationToken, Parameter("$PurchaseOrderId", purchaseOrderId));

	public Task<PurchaseOrder> SaveDraftAsync(PurchaseOrder order, CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			if (order.Id == 0)
			{
				var temporaryNumber = $"PENDING-{Guid.NewGuid():N}";
				order.Id = await session.InsertAsync(
					"INSERT INTO PurchaseOrders (OrderNumber, SupplierId, OrderDate, ExpectedDeliveryDate, Notes, Status) VALUES ($OrderNumber, $SupplierId, $OrderDate, $ExpectedDeliveryDate, $Notes, $Status);",
					token, Parameter("$OrderNumber", temporaryNumber), Parameter("$SupplierId", order.SupplierId), Parameter("$OrderDate", Date(order.OrderDate)), Parameter("$ExpectedDeliveryDate", NullableDate(order.ExpectedDeliveryDate)), Parameter("$Notes", order.Notes), Parameter("$Status", (int)PurchaseOrderStatus.Draft));
				order.OrderNumber = $"PO-{order.Id:000000}";
				await session.ExecuteAsync("UPDATE PurchaseOrders SET OrderNumber = $OrderNumber WHERE Id = $Id;", token, Parameter("$OrderNumber", order.OrderNumber), Parameter("$Id", order.Id));
			}
			else
			{
				var updated = await session.ExecuteAsync(
					"UPDATE PurchaseOrders SET SupplierId = $SupplierId, OrderDate = $OrderDate, ExpectedDeliveryDate = $ExpectedDeliveryDate, Notes = $Notes, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Draft;",
					token, Parameter("$SupplierId", order.SupplierId), Parameter("$OrderDate", Date(order.OrderDate)), Parameter("$ExpectedDeliveryDate", NullableDate(order.ExpectedDeliveryDate)), Parameter("$Notes", order.Notes), Parameter("$Id", order.Id), Parameter("$Version", order.Version), Parameter("$Draft", (int)PurchaseOrderStatus.Draft));
				if (updated != 1) throw new ConcurrencyConflictException("purchase order");
				order.Version++;
			}

			var existingIds = await session.QueryAsync("SELECT Id FROM PurchaseOrderLines WHERE PurchaseOrderId = $PurchaseOrderId;", reader => reader.GetInt64(0), token, Parameter("$PurchaseOrderId", order.Id));
			foreach (var id in existingIds.Where(id => order.Lines.All(line => line.Id != id)))
				await session.ExecuteAsync("DELETE FROM PurchaseOrderLines WHERE Id = $Id AND ReceivedQuantity = 0;", token, Parameter("$Id", id));

			var lineNumber = 1;
			foreach (var line in order.Lines)
			{
				line.PurchaseOrderId = order.Id;
				line.LineNumber = lineNumber++;
				if (line.Id == 0)
					line.Id = await session.InsertAsync("INSERT INTO PurchaseOrderLines (PurchaseOrderId, LineNumber, ItemId, Quantity, UnitPrice) VALUES ($PurchaseOrderId, $LineNumber, $ItemId, $Quantity, $UnitPrice);", token, LineParameters(line));
				else
				{
					var updated = await session.ExecuteAsync("UPDATE PurchaseOrderLines SET LineNumber = $LineNumber, ItemId = $ItemId, Quantity = $Quantity, UnitPrice = $UnitPrice, Version = Version + 1 WHERE Id = $Id AND PurchaseOrderId = $PurchaseOrderId AND Version = $Version AND ReceivedQuantity = 0;", token, [.. LineParameters(line), Parameter("$Id", line.Id), Parameter("$Version", line.Version)]);
					if (updated != 1) throw new ConcurrencyConflictException("purchase order line");
					line.Version++;
				}
			}
			return order;
		}, cancellationToken);

	public async Task<bool> SetStatusAsync(long id, long version, PurchaseOrderStatus expected, PurchaseOrderStatus status, CancellationToken cancellationToken) =>
		await Database.ExecuteAsync("UPDATE PurchaseOrders SET Status = $Status, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Expected;", cancellationToken, Parameter("$Status", (int)status), Parameter("$Id", id), Parameter("$Version", version), Parameter("$Expected", (int)expected)) == 1;

	private static PurchaseOrder ReadOrder(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), OrderNumber = reader.GetString(1), SupplierId = reader.GetInt64(2), SupplierName = reader.GetString(3),
		OrderDate = ParseDate(reader.GetString(4)), ExpectedDeliveryDate = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)), Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
		Status = (PurchaseOrderStatus)reader.GetInt32(7), Version = reader.GetInt64(8)
	};

	private static PurchaseOrderLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), PurchaseOrderId = reader.GetInt64(1), LineNumber = reader.GetInt32(2), ItemId = reader.GetInt64(3), ItemPartNumber = reader.GetString(4), ItemDescription = reader.GetString(5),
		Quantity = reader.GetInt32(6), UnitPrice = reader.GetDecimal(7), ReceivedQuantity = reader.GetInt32(8), Version = reader.GetInt64(9)
	};

	private static DatabaseParameter[] LineParameters(PurchaseOrderLine line) => [Parameter("$PurchaseOrderId", line.PurchaseOrderId), Parameter("$LineNumber", line.LineNumber), Parameter("$ItemId", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$UnitPrice", line.UnitPrice)];
	private static string Date(DateTime value) => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static object? NullableDate(DateTime? value) => value is null ? null : Date(value.Value);
	private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
}
