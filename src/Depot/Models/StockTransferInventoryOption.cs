// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class StockTransferInventoryOption
{
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string WarehouseName { get; set; } = string.Empty;
	public string StorageLocationName { get; set; } = string.Empty;
	public string PurposeName { get; set; } = string.Empty;
	public long CurrentStock { get; set; }
	public string DisplayName => $"{PartNumber} — {StorageLocationName} / {PurposeName} ({CurrentStock:N0} available)";
}
