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
	private const string Columns = "po.Id, po.OrderNumber, po.SupplierId, s.Name, po.OrderDate, po.ExpectedDeliveryDate, po.Notes, po.Status, po.CreatedByUserId, po.SubmittedByUserId, po.SubmittedAtUtc, po.ApprovalDecisionByUserId, po.ApprovalDecisionAtUtc, po.ApprovalComment, po.ClosedByUserId, po.ClosedAtUtc, po.CloseReason, createdUser.DisplayName, submittedUser.DisplayName, decisionUser.DisplayName, closedUser.DisplayName, po.Version";
	private const string From = "FROM PurchaseOrders po INNER JOIN Suppliers s ON s.Id = po.SupplierId LEFT JOIN Users createdUser ON createdUser.Id = po.CreatedByUserId LEFT JOIN Users submittedUser ON submittedUser.Id = po.SubmittedByUserId LEFT JOIN Users decisionUser ON decisionUser.Id = po.ApprovalDecisionByUserId LEFT JOIN Users closedUser ON closedUser.Id = po.ClosedByUserId";
	private const string LineColumns = "pol.Id, pol.PurchaseOrderId, pol.LineNumber, pol.ItemId, i.PartNumber, i.Description, pol.Quantity, pol.UnitPrice, pol.ReceivedQuantity, pol.Version";

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

	public Task<PageResult<PurchaseOrderApprovalWorkItem>> SearchPendingApprovalsAsync(
		PurchaseOrderApprovalFilter filter,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var (where, parameters) = BuildApprovalFilter(filter);
		const string approvalFrom =
			"FROM PurchaseOrders po INNER JOIN Suppliers s ON s.Id = po.SupplierId LEFT JOIN Users creator ON creator.Id = po.CreatedByUserId";
		return Database.QueryPageAsync(
			$"SELECT po.Id, po.OrderNumber, po.SupplierId, s.Name, po.OrderDate, po.ExpectedDeliveryDate, po.Notes, po.CreatedByUserId, creator.DisplayName, po.SubmittedAtUtc, (SELECT COALESCE(SUM(pol.Quantity * pol.UnitPrice), 0) FROM PurchaseOrderLines pol WHERE pol.PurchaseOrderId = po.Id), po.Version {approvalFrom} {where} ORDER BY po.SubmittedAtUtc, po.Id",
			$"SELECT COUNT(*) {approvalFrom} {where};",
			ReadApprovalWorkItem,
			pageNumber,
			pageSize,
			cancellationToken,
			parameters);
	}

	public Task<PurchaseOrderApprovalSummary?> GetPendingApprovalSummaryAsync(
		PurchaseOrderApprovalFilter filter,
		CancellationToken cancellationToken)
	{
		var (where, parameters) = BuildApprovalFilter(filter);
		return Database.QuerySingleOrDefaultAsync(
			$"SELECT COUNT(*), MIN(po.SubmittedAtUtc), COALESCE(SUM(COALESCE(orderTotals.TotalAmount, 0)), 0) FROM PurchaseOrders po INNER JOIN Suppliers s ON s.Id = po.SupplierId LEFT JOIN Users creator ON creator.Id = po.CreatedByUserId LEFT JOIN (SELECT PurchaseOrderId, SUM(Quantity * UnitPrice) AS TotalAmount FROM PurchaseOrderLines GROUP BY PurchaseOrderId) orderTotals ON orderTotals.PurchaseOrderId = po.Id {where};",
			ReadApprovalSummary,
			cancellationToken,
			parameters);
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
			$"SELECT {LineColumns} FROM PurchaseOrderLines pol INNER JOIN Items i ON i.Id = pol.ItemId WHERE pol.PurchaseOrderId = $PurchaseOrderId ORDER BY pol.LineNumber;",
			ReadLine, cancellationToken, Parameter("$PurchaseOrderId", purchaseOrderId));

	public async Task<PurchaseOrder?> GetForReceiptUpdateAsync(
		DatabaseTransactionContext transaction,
		long id,
		CancellationToken cancellationToken)
	{
		if (await transaction.Session.ExecuteScalarAsync(
			Database.PurchaseOrderLockSql,
			cancellationToken,
			Parameter("$PurchaseOrderId", id)) is null)
		{
			return null;
		}

		var rows = await transaction.Session.QueryAsync(
			$"SELECT {Columns}, {LineColumns} {From} INNER JOIN PurchaseOrderLines pol ON pol.PurchaseOrderId = po.Id INNER JOIN Items i ON i.Id = pol.ItemId WHERE po.Id = $Id ORDER BY pol.LineNumber;",
			reader => new ReceiptOrderRow(ReadOrder(reader), ReadLine(reader, 22)),
			cancellationToken,
			Parameter("$Id", id));
		if (rows.Count == 0)
		{
			return null;
		}

		var order = rows[0].Order;
		order.Lines = rows.Select(row => row.Line).ToArray();
		return order;
	}

	public async Task<bool> UpdateReceivedQuantityAsync(
		DatabaseTransactionContext transaction,
		long lineId,
		long version,
		int receivedQuantity,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE PurchaseOrderLines SET ReceivedQuantity = $ReceivedQuantity, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$ReceivedQuantity", receivedQuantity),
			Parameter("$Id", lineId),
			Parameter("$Version", version)) == 1;

	public async Task<bool> UpdateStatusAsync(
		DatabaseTransactionContext transaction,
		long id,
		long version,
		PurchaseOrderStatus status,
		CancellationToken cancellationToken) =>
		await transaction.Session.ExecuteAsync(
			"UPDATE PurchaseOrders SET Status = $Status, Version = Version + 1 WHERE Id = $Id AND Version = $Version;",
			cancellationToken,
			Parameter("$Status", (int)status),
			Parameter("$Id", id),
			Parameter("$Version", version)) == 1;

	public Task<PurchaseOrder> SaveDraftAsync(
		PurchaseOrder order,
		Func<PurchaseOrder, AuditEntry> createAuditEntry,
		CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			if (order.Id == 0)
			{
				var temporaryNumber = $"PENDING-{Guid.NewGuid():N}";
				order.Id = await session.InsertAsync(
					"INSERT INTO PurchaseOrders (OrderNumber, SupplierId, OrderDate, ExpectedDeliveryDate, Notes, Status, CreatedByUserId) VALUES ($OrderNumber, $SupplierId, $OrderDate, $ExpectedDeliveryDate, $Notes, $Status, $CreatedByUserId);",
					token, Parameter("$OrderNumber", temporaryNumber), Parameter("$SupplierId", order.SupplierId), Parameter("$OrderDate", Date(order.OrderDate)), Parameter("$ExpectedDeliveryDate", NullableDate(order.ExpectedDeliveryDate)), Parameter("$Notes", order.Notes), Parameter("$Status", (int)PurchaseOrderStatus.Draft), Parameter("$CreatedByUserId", order.CreatedByUserId));
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
			var removedIds = existingIds.Where(id => order.Lines.All(line => line.Id != id)).OrderBy(id => id).ToArray();
			if (removedIds.Length > 0)
			{
				var deleteParameters = removedIds.Select((id, index) => Parameter($"$RemovedId{index}", id)).ToArray();
				var parameterList = string.Join(", ", deleteParameters.Select(parameter => parameter.Name));
				await session.ExecuteAsync(
					$"DELETE FROM PurchaseOrderLines WHERE PurchaseOrderId = $PurchaseOrderId AND Id IN ({parameterList}) AND ReceivedQuantity = 0;",
					token,
					[.. deleteParameters, Parameter("$PurchaseOrderId", order.Id)]);
			}

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
			await AuditRepository.CreateAsync(session, createAuditEntry(order), token);
			return order;
		}, cancellationToken);

	public Task<PurchaseOrder> SetStatusAsync(
		long id,
		long version,
		PurchaseOrderStatus expected,
		PurchaseOrderStatus status,
		PurchaseOrder result,
		AuditEntry auditEntry,
		CancellationToken cancellationToken) =>
		Database.ExecuteInWriteTransactionAsync(async (session, token) =>
		{
			var updated = await session.ExecuteAsync(
				"UPDATE PurchaseOrders SET Status = $Status, CreatedByUserId = $CreatedByUserId, SubmittedByUserId = $SubmittedByUserId, SubmittedAtUtc = $SubmittedAtUtc, ApprovalDecisionByUserId = $ApprovalDecisionByUserId, ApprovalDecisionAtUtc = $ApprovalDecisionAtUtc, ApprovalComment = $ApprovalComment, ClosedByUserId = $ClosedByUserId, ClosedAtUtc = $ClosedAtUtc, CloseReason = $CloseReason, Version = Version + 1 WHERE Id = $Id AND Version = $Version AND Status = $Expected;",
				token,
				Parameter("$Status", (int)status),
				Parameter("$Id", id),
				Parameter("$Version", version),
				Parameter("$CreatedByUserId", result.CreatedByUserId),
				Parameter("$SubmittedByUserId", result.SubmittedByUserId),
				Parameter("$SubmittedAtUtc", NullableUtc(result.SubmittedAtUtc)),
				Parameter("$ApprovalDecisionByUserId", result.ApprovalDecisionByUserId),
				Parameter("$ApprovalDecisionAtUtc", NullableUtc(result.ApprovalDecisionAtUtc)),
				Parameter("$ApprovalComment", result.ApprovalComment),
				Parameter("$ClosedByUserId", result.ClosedByUserId),
				Parameter("$ClosedAtUtc", NullableUtc(result.ClosedAtUtc)),
				Parameter("$CloseReason", result.CloseReason),
				Parameter("$Expected", (int)expected));
			if (updated != 1) throw new ConcurrencyConflictException("purchase order");
			await AuditRepository.CreateAsync(session, auditEntry, token);
			return result;
		}, cancellationToken);

	private static async Task<PurchaseOrder?> GetByIdAsync(
		DatabaseSession session,
		long id,
		CancellationToken cancellationToken)
	{
		var order = await session.QuerySingleOrDefaultAsync(
			$"SELECT {Columns} {From} WHERE po.Id = $Id;",
			ReadOrder,
			cancellationToken,
			Parameter("$Id", id));
		if (order is null) return null;
		order.Lines = await session.QueryAsync(
			"SELECT pol.Id, pol.PurchaseOrderId, pol.LineNumber, pol.ItemId, i.PartNumber, i.Description, pol.Quantity, pol.UnitPrice, pol.ReceivedQuantity, pol.Version FROM PurchaseOrderLines pol INNER JOIN Items i ON i.Id = pol.ItemId WHERE pol.PurchaseOrderId = $PurchaseOrderId ORDER BY pol.LineNumber;",
			ReadLine,
			cancellationToken,
			Parameter("$PurchaseOrderId", id));
		return order;
	}

	private static PurchaseOrder ReadOrder(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), OrderNumber = reader.GetString(1), SupplierId = reader.GetInt64(2), SupplierName = reader.GetString(3),
		OrderDate = ParseDate(reader.GetString(4)), ExpectedDeliveryDate = reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)), Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
		Status = (PurchaseOrderStatus)reader.GetInt32(7),
		CreatedByUserId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
		SubmittedByUserId = reader.IsDBNull(9) ? null : reader.GetInt64(9),
		SubmittedAtUtc = reader.IsDBNull(10) ? null : ParseUtc(reader.GetString(10)),
		ApprovalDecisionByUserId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
		ApprovalDecisionAtUtc = reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12)),
		ApprovalComment = reader.IsDBNull(13) ? null : reader.GetString(13),
		ClosedByUserId = reader.IsDBNull(14) ? null : reader.GetInt64(14),
		ClosedAtUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15)),
		CloseReason = reader.IsDBNull(16) ? null : reader.GetString(16),
		CreatedByUserDisplay = reader.IsDBNull(17) ? null : reader.GetString(17),
		SubmittedByUserDisplay = reader.IsDBNull(18) ? null : reader.GetString(18),
		ApprovalDecisionByUserDisplay = reader.IsDBNull(19) ? null : reader.GetString(19),
		ClosedByUserDisplay = reader.IsDBNull(20) ? null : reader.GetString(20),
		Version = reader.GetInt64(21)
	};

	private static PurchaseOrderLine ReadLine(DbDataReader reader) => new()
	{
		Id = reader.GetInt64(0), PurchaseOrderId = reader.GetInt64(1), LineNumber = reader.GetInt32(2), ItemId = reader.GetInt64(3), ItemPartNumber = reader.GetString(4), ItemDescription = reader.GetString(5),
		Quantity = reader.GetInt32(6), UnitPrice = reader.GetDecimal(7), ReceivedQuantity = reader.GetInt32(8), Version = reader.GetInt64(9)
	};

	private static PurchaseOrderLine ReadLine(DbDataReader reader, int offset) => new()
	{
		Id = reader.GetInt64(offset), PurchaseOrderId = reader.GetInt64(offset + 1), LineNumber = reader.GetInt32(offset + 2), ItemId = reader.GetInt64(offset + 3), ItemPartNumber = reader.GetString(offset + 4), ItemDescription = reader.GetString(offset + 5),
		Quantity = reader.GetInt32(offset + 6), UnitPrice = reader.GetDecimal(offset + 7), ReceivedQuantity = reader.GetInt32(offset + 8), Version = reader.GetInt64(offset + 9)
	};

	private static (string WhereClause, DatabaseParameter[] Parameters) BuildApprovalFilter(PurchaseOrderApprovalFilter filter)
	{
		var predicates = new List<string> { "po.Status = $PendingApproval" };
		var parameters = new List<DatabaseParameter> { Parameter("$PendingApproval", (int)PurchaseOrderStatus.PendingApproval) };
		if (!string.IsNullOrWhiteSpace(filter.SearchText))
		{
			predicates.Add("(po.OrderNumber LIKE $Search OR s.Name LIKE $Search OR po.Notes LIKE $Search OR creator.DisplayName LIKE $Search OR creator.Email LIKE $Search)");
			parameters.Add(Parameter("$Search", $"%{filter.SearchText.Trim()}%"));
		}
		AddContainsFilter(predicates, parameters, "s.Name", "$Supplier", filter.SupplierFilter);
		if (!string.IsNullOrWhiteSpace(filter.CreatorFilter))
		{
			predicates.Add("(creator.DisplayName LIKE $Creator OR creator.Email LIKE $Creator)");
			parameters.Add(Parameter("$Creator", $"%{filter.CreatorFilter.Trim()}%"));
		}
		if (filter.SubmittedFromUtc is not null)
		{
			predicates.Add("po.SubmittedAtUtc >= $SubmittedFromUtc");
			parameters.Add(Parameter("$SubmittedFromUtc", NullableUtc(filter.SubmittedFromUtc)));
		}
		if (filter.SubmittedToUtcExclusive is not null)
		{
			predicates.Add("po.SubmittedAtUtc < $SubmittedToUtc");
			parameters.Add(Parameter("$SubmittedToUtc", NullableUtc(filter.SubmittedToUtcExclusive)));
		}
		return ($"WHERE {string.Join(" AND ", predicates)}", parameters.ToArray());
	}

	private static void AddContainsFilter(
		ICollection<string> predicates,
		ICollection<DatabaseParameter> parameters,
		string column,
		string parameter,
		string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return;
		predicates.Add($"{column} LIKE {parameter}");
		parameters.Add(Parameter(parameter, $"%{value.Trim()}%"));
	}

	private static PurchaseOrderApprovalWorkItem ReadApprovalWorkItem(DbDataReader reader) =>
		new(
			reader.GetInt64(0),
			reader.GetString(1),
			reader.GetInt64(2),
			reader.GetString(3),
			ParseDate(reader.GetString(4)),
			reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
			reader.IsDBNull(6) ? null : reader.GetString(6),
			reader.IsDBNull(7) ? null : reader.GetInt64(7),
			reader.IsDBNull(8) ? "Unknown user" : reader.GetString(8),
			ParseUtc(reader.GetString(9)),
			Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
			reader.GetInt64(11));

	private static PurchaseOrderApprovalSummary ReadApprovalSummary(DbDataReader reader) =>
		new(
			Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
			reader.IsDBNull(1) ? null : ParseUtc(reader.GetString(1)),
			Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture));

	private static DatabaseParameter[] LineParameters(PurchaseOrderLine line) => [Parameter("$PurchaseOrderId", line.PurchaseOrderId), Parameter("$LineNumber", line.LineNumber), Parameter("$ItemId", line.ItemId), Parameter("$Quantity", line.Quantity), Parameter("$UnitPrice", line.UnitPrice)];
	private static string Date(DateTime value) => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	private static object? NullableDate(DateTime? value) => value is null ? null : Date(value.Value);
	private static object? NullableUtc(DateTime? value) => value is null ? null : value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
	private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
	private static DateTime ParseUtc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
	private sealed record ReceiptOrderRow(PurchaseOrder Order, PurchaseOrderLine Line);
}
