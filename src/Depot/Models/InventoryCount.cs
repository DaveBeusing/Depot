// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryCount
{
	public long Id { get; set; }
	public string CountNumber { get; set; } = string.Empty;
	public long WarehouseId { get; set; }
	public InventoryCountStatus Status { get; set; } = InventoryCountStatus.Draft;
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? StartedAtUtc { get; set; }
	public DateTime? CompletedAtUtc { get; set; }
	public long CreatedByUserId { get; set; }
	public long? PostedByUserId { get; set; }
	public string? Notes { get; set; }
	public DateTime? ReversedAtUtc { get; set; }
	public long? ReversedByUserId { get; set; }
	public string? ReversalReason { get; set; }
	public bool IsReversed => ReversedAtUtc is not null;
	public long Version { get; set; } = 1;
	public IReadOnlyList<InventoryCountLine> Lines { get; set; } = [];
}
