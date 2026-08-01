// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class MaterialReturnLine
{
	public long Id { get; set; }
	public long MaterialReturnId { get; set; }
	public int LineNumber { get; set; }
	public long InventoryId { get; set; }
	public int Quantity { get; set; }
	public long ReasonCodeId { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; } = 1;
	public string PartNumber { get; set; } = string.Empty;
	public string ItemDescription { get; set; } = string.Empty;
	public string WarehouseName { get; set; } = string.Empty;
	public string StorageLocationName { get; set; } = string.Empty;
	public string PurposeName { get; set; } = string.Empty;
	public string ReasonCodeName { get; set; } = string.Empty;
	public long CurrentStock { get; set; }
}
