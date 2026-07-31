// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryCountLine
{
	public long Id { get; set; }
	public long InventoryCountId { get; set; }
	public long InventoryId { get; set; }
	public long ExpectedQuantity { get; set; }
	public long? CountedQuantity { get; set; }
	public long? CountedByUserId { get; set; }
	public DateTime? CountedAtUtc { get; set; }
	public long Version { get; set; } = 1;
}
