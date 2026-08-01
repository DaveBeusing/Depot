// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierReturnableLine
{
	public long GoodsReceiptLineId { get; init; }
	public long InventoryId { get; init; }
	public long ItemId { get; init; }
	public string PartNumber { get; init; } = string.Empty;
	public string ItemDescription { get; init; } = string.Empty;
	public string InventoryDisplay { get; init; } = string.Empty;
	public int ReceivedQuantity { get; init; }
	public int AlreadyReturnedQuantity { get; init; }
	public int ReturnableQuantity => ReceivedQuantity - AlreadyReturnedQuantity;
	public long AvailableStock { get; init; }
	public decimal UnitCost { get; init; }
}
