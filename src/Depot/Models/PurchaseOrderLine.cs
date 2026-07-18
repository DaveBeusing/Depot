// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class PurchaseOrderLine
{
	public long Id { get; set; }
	public long PurchaseOrderId { get; set; }
	public int LineNumber { get; set; }
	public long ItemId { get; set; }
	public string ItemPartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public int ReceivedQuantity { get; set; }
	public int OpenQuantity => Quantity - ReceivedQuantity;
	public long Version { get; set; } = 1;
}
