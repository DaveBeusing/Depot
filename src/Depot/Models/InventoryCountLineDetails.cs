// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryCountLineDetails
{
	public long Id { get; set; }
	public long InventoryCountId { get; set; }
	public long InventoryId { get; set; }
	public string PartNumber { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string StorageLocationName { get; set; } = string.Empty;
	public string PurposeName { get; set; } = string.Empty;
	public long ExpectedQuantity { get; set; }
	public long? CountedQuantity { get; set; }
	public long? CountedByUserId { get; set; }
	public string? CountedByUserName { get; set; }
	public DateTime? CountedAtUtc { get; set; }
	public long Version { get; set; }
	public bool IsCounted => CountedQuantity is not null;
	public long? Difference => CountedQuantity - ExpectedQuantity;
}
