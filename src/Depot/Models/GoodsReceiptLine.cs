// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class GoodsReceiptLine
{
	public long Id { get; set; }
	public long GoodsReceiptId { get; set; }
	public long PurchaseOrderLineId { get; set; }
	public long InventoryId { get; set; }
	public int Quantity { get; set; }
}

public sealed class ReceiptInventoryOption
{
	public long InventoryId { get; init; }
	public long ItemId { get; init; }
	public string DisplayName { get; init; } = string.Empty;
}
