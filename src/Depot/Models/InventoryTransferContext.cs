// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryTransferContext
{
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public long WarehouseId { get; set; }
	public bool IsInventoryActive { get; set; }
	public bool IsStorageLocationActive { get; set; }
	public bool IsWarehouseActive { get; set; }
}
