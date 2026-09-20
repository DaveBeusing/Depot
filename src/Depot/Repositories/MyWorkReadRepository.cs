// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class MyWorkReadRepository : DatabaseRepository
{
	public MyWorkReadRepository(DatabaseAccess database) : base(database) { }

	public Task<IReadOnlyList<PurchaseOrderMyWorkRow>> GetPurchaseOrdersAsync(int perStatusLimit, CancellationToken cancellationToken)
	{
		ValidateLimit(perStatusLimit);
		return Database.QueryAsync(
			"""
			WITH Ranked AS (
				SELECT po.Id, po.OrderNumber, s.Name, po.Status, po.CreatedByUserId, po.ExpectedDeliveryDate, po.ClosedAtUtc,
				       COALESCE(SUM(pol.Quantity * pol.UnitPrice), 0) AS TotalAmount,
				       COALESCE(SUM(CASE WHEN pol.Quantity > pol.ReceivedQuantity THEN pol.Quantity - pol.ReceivedQuantity ELSE 0 END), 0) AS OpenQuantity,
				       ROW_NUMBER() OVER (PARTITION BY po.Status ORDER BY po.OrderDate DESC, po.Id DESC) AS RowRank
				FROM PurchaseOrders po
				INNER JOIN Suppliers s ON s.Id = po.SupplierId
				LEFT JOIN PurchaseOrderLines pol ON pol.PurchaseOrderId = po.Id
				WHERE po.Status IN ($Draft, $Ordered, $PartiallyReceived, $Closed)
				GROUP BY po.Id, po.OrderNumber, s.Name, po.Status, po.CreatedByUserId, po.ExpectedDeliveryDate, po.ClosedAtUtc, po.OrderDate
			)
			SELECT Id, OrderNumber, Name, Status, CreatedByUserId, ExpectedDeliveryDate, ClosedAtUtc, TotalAmount, OpenQuantity
			FROM Ranked
			WHERE RowRank <= $Limit
			ORDER BY Status, RowRank;
			""",
			ReadPurchaseOrder,
			cancellationToken,
			Parameter("$Draft", (int)PurchaseOrderStatus.Draft),
			Parameter("$Ordered", (int)PurchaseOrderStatus.Ordered),
			Parameter("$PartiallyReceived", (int)PurchaseOrderStatus.PartiallyReceived),
			Parameter("$Closed", (int)PurchaseOrderStatus.Closed),
			Parameter("$Limit", perStatusLimit));
	}

	public Task<IReadOnlyList<PurchaseOrderApprovalWorkItem>> GetPurchaseApprovalsAsync(int count, CancellationToken cancellationToken)
	{
		ValidateLimit(count);
		return Database.QuerySliceAsync(
			"""
			SELECT po.Id, po.OrderNumber, po.SupplierId, s.Name, po.OrderDate, po.ExpectedDeliveryDate, po.Notes,
			       po.CreatedByUserId, creator.DisplayName, po.SubmittedAtUtc,
			       COALESCE(orderTotals.TotalAmount, 0), po.Version
			FROM PurchaseOrders po
			INNER JOIN Suppliers s ON s.Id = po.SupplierId
			LEFT JOIN Users creator ON creator.Id = po.CreatedByUserId
			LEFT JOIN (
				SELECT PurchaseOrderId, SUM(Quantity * UnitPrice) AS TotalAmount
				FROM PurchaseOrderLines
				GROUP BY PurchaseOrderId
			) orderTotals ON orderTotals.PurchaseOrderId = po.Id
			WHERE po.Status = $PendingApproval
			ORDER BY po.SubmittedAtUtc, po.Id
			""",
			ReadPurchaseApproval,
			0,
			count,
			cancellationToken,
			Parameter("$PendingApproval", (int)PurchaseOrderStatus.PendingApproval));
	}

	public Task<IReadOnlyList<SalesOrderMyWorkRow>> GetSalesOrdersAsync(int perStatusLimit, CancellationToken cancellationToken)
	{
		ValidateLimit(perStatusLimit);
		return Database.QueryAsync(
			"""
			WITH Ranked AS (
				SELECT so.Id, so.OrderNumber, c.Name, so.Status, so.CreatedByUserId, so.SubmittedAtUtc, so.RequestedDeliveryDate,
				       COALESCE(SUM(
				           ROUND(sol.Quantity * sol.UnitPrice * (1 - sol.DiscountPercent / 100.0), 2) +
				           ROUND(ROUND(sol.Quantity * sol.UnitPrice * (1 - sol.DiscountPercent / 100.0), 2) * sol.TaxRate / 100.0, 2)
				       ), 0) AS GrossAmount,
				       COALESCE(SUM(CASE WHEN sol.Quantity > sol.ShippedQuantity THEN sol.Quantity - sol.ShippedQuantity ELSE 0 END), 0) AS OpenQuantity,
				       COALESCE(SUM(CASE WHEN (sol.Quantity - sol.ShippedQuantity) > sol.ReservedQuantity THEN (sol.Quantity - sol.ShippedQuantity) - sol.ReservedQuantity ELSE 0 END), 0) AS BackorderedQuantity,
				       ROW_NUMBER() OVER (PARTITION BY so.Status ORDER BY so.OrderDate DESC, so.Id DESC) AS RowRank
				FROM SalesOrders so
				INNER JOIN Customers c ON c.Id = so.CustomerId
				LEFT JOIN SalesOrderLines sol ON sol.SalesOrderId = so.Id
				WHERE so.Status IN ($Draft, $PendingApproval, $Approved, $Released, $PartiallyShipped)
				GROUP BY so.Id, so.OrderNumber, c.Name, so.Status, so.CreatedByUserId, so.SubmittedAtUtc, so.RequestedDeliveryDate, so.OrderDate
			)
			SELECT Id, OrderNumber, Name, Status, CreatedByUserId, SubmittedAtUtc, RequestedDeliveryDate, GrossAmount, OpenQuantity, BackorderedQuantity
			FROM Ranked
			WHERE RowRank <= $Limit
			ORDER BY Status, RowRank;
			""",
			ReadSalesOrder,
			cancellationToken,
			Parameter("$Draft", (int)SalesOrderStatus.Draft),
			Parameter("$PendingApproval", (int)SalesOrderStatus.PendingApproval),
			Parameter("$Approved", (int)SalesOrderStatus.Approved),
			Parameter("$Released", (int)SalesOrderStatus.Released),
			Parameter("$PartiallyShipped", (int)SalesOrderStatus.PartiallyShipped),
			Parameter("$Limit", perStatusLimit));
	}

	public Task<IReadOnlyList<ShipmentMyWorkRow>> GetShipmentsAsync(int perStatusLimit, CancellationToken cancellationToken)
	{
		ValidateLimit(perStatusLimit);
		return Database.QueryAsync(
			"""
			WITH Ranked AS (
				SELECT sh.Id, sh.ShipmentNumber, c.Name, sh.Status, sh.PackingStatus, sh.CreatedByUserId, sh.ShipmentDate, sh.PostedAtUtc,
				       COALESCE(SUM(sl.Quantity), 0) AS TotalQuantity,
				       ROW_NUMBER() OVER (PARTITION BY sh.Status ORDER BY sh.ShipmentDate DESC, sh.Id DESC) AS RowRank
				FROM Shipments sh
				INNER JOIN Customers c ON c.Id = sh.CustomerId
				LEFT JOIN ShipmentLines sl ON sl.ShipmentId = sh.Id
				WHERE sh.Status IN ($Draft, $Posted)
				GROUP BY sh.Id, sh.ShipmentNumber, c.Name, sh.Status, sh.PackingStatus, sh.CreatedByUserId, sh.ShipmentDate, sh.PostedAtUtc
			)
			SELECT Id, ShipmentNumber, Name, Status, PackingStatus, CreatedByUserId, ShipmentDate, PostedAtUtc, TotalQuantity
			FROM Ranked
			WHERE RowRank <= $Limit
			ORDER BY Status, RowRank;
			""",
			ReadShipment,
			cancellationToken,
			Parameter("$Draft", (int)ShipmentStatus.Draft),
			Parameter("$Posted", (int)ShipmentStatus.Posted),
			Parameter("$Limit", perStatusLimit));
	}

	public Task<IReadOnlyList<InventoryCountMyWorkRow>> GetInventoryCountsAsync(int perStatusLimit, CancellationToken cancellationToken)
	{
		ValidateLimit(perStatusLimit);
		return Database.QueryAsync(
			"""
			WITH Ranked AS (
				SELECT ic.Id, ic.CountNumber, w.Name, ic.Status, ic.CreatedByUserId, ic.CompletedAtUtc,
				       (SELECT COUNT(*) FROM InventoryCountLines totalLines WHERE totalLines.InventoryCountId = ic.Id) AS TotalLineCount,
				       (SELECT COUNT(*) FROM InventoryCountLines differenceLines WHERE differenceLines.InventoryCountId = ic.Id AND differenceLines.CountedQuantity IS NOT NULL AND differenceLines.CountedQuantity <> differenceLines.ExpectedQuantity) AS DifferenceLineCount,
				       ROW_NUMBER() OVER (PARTITION BY ic.Status ORDER BY ic.CreatedAtUtc DESC, ic.Id DESC) AS RowRank
				FROM InventoryCounts ic
				INNER JOIN Warehouses w ON w.Id = ic.WarehouseId
				WHERE ic.Status IN ($Draft, $Counting, $Review, $Posted)
			)
			SELECT Id, CountNumber, Name, Status, CreatedByUserId, CompletedAtUtc, TotalLineCount, DifferenceLineCount
			FROM Ranked
			WHERE RowRank <= $Limit
			ORDER BY Status, RowRank;
			""",
			ReadInventoryCount,
			cancellationToken,
			Parameter("$Draft", (int)InventoryCountStatus.Draft),
			Parameter("$Counting", (int)InventoryCountStatus.Counting),
			Parameter("$Review", (int)InventoryCountStatus.Review),
			Parameter("$Posted", (int)InventoryCountStatus.Posted),
			Parameter("$Limit", perStatusLimit));
	}

	public Task<IReadOnlyList<FinancePayablesMyWorkDocument>> GetPayablesDocumentsAsync(int perStatusLimit, CancellationToken cancellationToken)
	{
		ValidateLimit(perStatusLimit);
		return Database.QueryAsync(
			"""
			WITH Ranked AS (
				SELECT d.Id, d.SupplierDocumentNumber, s.Name, d.Status, d.CreatedByUserId, d.DueDate, d.GrossAmount, d.PostedAtUtc,
				       CASE WHEN EXISTS (
				           SELECT 1 FROM FinanceSupplierDocumentLines line
				           WHERE line.DocumentId = d.Id AND line.MatchStatus = $ExceptionStatus
				       ) THEN 1 ELSE 0 END AS HasMatchExceptions,
				       d.MatchExceptionApproved,
				       ROW_NUMBER() OVER (PARTITION BY d.Status ORDER BY d.DocumentDate DESC, d.Id DESC) AS RowRank
				FROM FinanceSupplierDocuments d
				INNER JOIN Suppliers s ON s.Id = d.SupplierId
				WHERE d.Status IN ($Draft, $PendingApproval, $Approved, $Posted)
			)
			SELECT Id, SupplierDocumentNumber, Name, Status, CreatedByUserId, DueDate, GrossAmount, PostedAtUtc, HasMatchExceptions, MatchExceptionApproved
			FROM Ranked
			WHERE RowRank <= $Limit
			ORDER BY Status, RowRank;
			""",
			ReadPayablesDocument,
			cancellationToken,
			Parameter("$ExceptionStatus", (int)FinancePayableMatchStatus.Exception),
			Parameter("$Draft", (int)FinancePayableDocumentStatus.Draft),
			Parameter("$PendingApproval", (int)FinancePayableDocumentStatus.PendingApproval),
			Parameter("$Approved", (int)FinancePayableDocumentStatus.Approved),
			Parameter("$Posted", (int)FinancePayableDocumentStatus.Posted),
			Parameter("$Limit", perStatusLimit));
	}

	private static PurchaseOrderMyWorkRow ReadPurchaseOrder(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
		(PurchaseOrderStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		reader.IsDBNull(4) ? null : reader.GetInt64(4),
		reader.IsDBNull(5) ? null : ReadDate(reader, 5),
		reader.IsDBNull(6) ? null : ReadUtc(reader, 6),
		Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
		Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture));

	private static PurchaseOrderApprovalWorkItem ReadPurchaseApproval(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3),
		ReadDate(reader, 4), reader.IsDBNull(5) ? null : ReadDate(reader, 5),
		reader.IsDBNull(6) ? null : reader.GetString(6),
		reader.IsDBNull(7) ? null : reader.GetInt64(7),
		reader.IsDBNull(8) ? "Unknown user" : reader.GetString(8),
		ReadUtc(reader, 9),
		Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
		reader.GetInt64(11));

	private static SalesOrderMyWorkRow ReadSalesOrder(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
		(SalesOrderStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		reader.IsDBNull(4) ? null : reader.GetInt64(4),
		reader.IsDBNull(5) ? null : ReadUtc(reader, 5),
		reader.IsDBNull(6) ? null : ReadDate(reader, 6),
		Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
		Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
		Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture));

	private static ShipmentMyWorkRow ReadShipment(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
		(ShipmentStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		(ShipmentPackingStatus)Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
		reader.GetInt64(5), ReadDate(reader, 6),
		reader.IsDBNull(7) ? null : ReadUtc(reader, 7),
		Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture));

	private static InventoryCountMyWorkRow ReadInventoryCount(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
		(InventoryCountStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		reader.GetInt64(4),
		reader.IsDBNull(5) ? null : ReadUtc(reader, 5),
		Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
		Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture));

	private static FinancePayablesMyWorkDocument ReadPayablesDocument(DbDataReader reader) => new(
		reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
		(FinancePayableDocumentStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
		reader.GetInt64(4), DateOnly.FromDateTime(ReadDate(reader, 5)),
		Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
		reader.IsDBNull(7) ? null : ReadUtc(reader, 7),
		Convert.ToBoolean(reader.GetValue(8), CultureInfo.InvariantCulture),
		Convert.ToBoolean(reader.GetValue(9), CultureInfo.InvariantCulture));

	private static DateTime ReadDate(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime value
			? value
			: DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);

	private static DateTime ReadUtc(DbDataReader reader, int ordinal) =>
		reader.GetValue(ordinal) is DateTime value
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: DateTime.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

	private static void ValidateLimit(int perStatusLimit)
	{
		if (perStatusLimit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(perStatusLimit));
	}
}
