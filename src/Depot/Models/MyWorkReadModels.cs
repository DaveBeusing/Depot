// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed record PurchaseOrderMyWorkRow(
	long Id,
	string OrderNumber,
	string SupplierName,
	PurchaseOrderStatus Status,
	long? CreatedByUserId,
	DateTime? ExpectedDeliveryDate,
	DateTime? ClosedAtUtc,
	decimal TotalAmount,
	decimal OpenQuantity)
{
	public string StatusDisplayName => Status switch
	{
		PurchaseOrderStatus.PartiallyReceived => "Partially Received",
		PurchaseOrderStatus.PendingApproval => "Pending Approval",
		_ => Status.ToString()
	};
}

public sealed record SalesOrderMyWorkRow(
	long Id,
	string OrderNumber,
	string CustomerName,
	SalesOrderStatus Status,
	long? CreatedByUserId,
	DateTime? SubmittedAtUtc,
	DateTime? RequestedDeliveryDate,
	decimal GrossAmount,
	int OpenQuantity,
	int BackorderedQuantity);

public sealed record ShipmentMyWorkRow(
	long Id,
	string ShipmentNumber,
	string CustomerName,
	ShipmentStatus Status,
	ShipmentPackingStatus PackingStatus,
	long CreatedByUserId,
	DateTime ShipmentDate,
	DateTime? PostedAtUtc,
	int TotalQuantity);

public sealed record InventoryCountMyWorkRow(
	long Id,
	string CountNumber,
	string WarehouseName,
	InventoryCountStatus Status,
	long CreatedByUserId,
	DateTime? CompletedAtUtc,
	int TotalLineCount,
	int DifferenceLineCount)
{
	public string StatusDisplayName => Status.ToString();
}
