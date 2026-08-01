// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class SupplierReturnLine
{
	public long Id { get; set; }
	public long SupplierReturnId { get; set; }
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public int Quantity { get; set; }
	public decimal UnitCost { get; set; }
	public long ReasonCodeId { get; set; }
	public long GoodsReceiptLineId { get; set; }
	public long Version { get; set; } = 1;
	public string PartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public string InventoryDisplay { get; set; } = string.Empty;
	public string ReasonCodeName { get; set; } = string.Empty;
	public int ReceivedQuantity { get; set; }
	public int AlreadyReturnedQuantity { get; set; }
	public long AvailableStock { get; set; }
	public int ReturnableQuantity => ReceivedQuantity - AlreadyReturnedQuantity;
}
