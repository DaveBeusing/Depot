// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class PurchaseOrder
{
	public long Id { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public long SupplierId { get; set; }
	public string SupplierName { get; set; } = string.Empty;
	public DateTime OrderDate { get; set; } = DateTime.Today;
	public DateTime? ExpectedDeliveryDate { get; set; }
	public string? Notes { get; set; }
	public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
	public string StatusDisplayName => Status switch
	{
		PurchaseOrderStatus.PartiallyReceived => "Partially Received",
		_ => Status.ToString()
	};
	public long Version { get; set; } = 1;
	public IReadOnlyList<PurchaseOrderLine> Lines { get; set; } = [];
}
