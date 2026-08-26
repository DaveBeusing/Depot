// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class TrackingAllocationInput
{
	public string Code { get; set; } = string.Empty;
	public int Quantity { get; set; } = 1;
	public DateTime? ExpiryDate { get; set; }
}

public sealed class MovementTrackingAllocation
{
	public long TrackingUnitId { get; set; }
	public string Code { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public bool IsBlocked { get; set; }
	public string? BlockReason { get; set; }
	public long Version { get; set; }
}

public sealed class ItemTraceabilityBalance
{
	public long TrackingUnitId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public ItemTrackingMode TrackingMode { get; set; }
	public string Code { get; set; } = string.Empty;
	public DateTime? ExpiryDate { get; set; }
	public bool IsBlocked { get; set; }
	public string? BlockReason { get; set; }
	public long InventoryId { get; set; }
	public string Warehouse { get; set; } = string.Empty;
	public string StorageLocation { get; set; } = string.Empty;
	public string Purpose { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public long Version { get; set; }
}

public sealed class ItemTraceabilityHistoryEntry
{
	public long MovementId { get; set; }
	public DateTime TimestampUtc { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public ItemTrackingMode TrackingMode { get; set; }
	public string Code { get; set; } = string.Empty;
	public long InventoryId { get; set; }
	public string Warehouse { get; set; } = string.Empty;
	public string StorageLocation { get; set; } = string.Empty;
	public StockMovementType MovementType { get; set; }
	public int Quantity { get; set; }
	public string? Reference { get; set; }
}

public sealed class InventoryItemPolicy
{
	public long InventoryId { get; set; }
	public long ItemId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public ItemType ItemType { get; set; }
	public ItemLifecycleStatus LifecycleStatus { get; set; }
	public ItemTrackingMode TrackingMode { get; set; }
	public DateTime? LastBuyDate { get; set; }
	public DateTime? EndOfSupportDate { get; set; }
	public long? ReplacementItemId { get; set; }
	public string? ReplacementPartNumber { get; set; }
}
