// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryCountOverviewItem
{
	public long Id { get; set; }
	public string CountNumber { get; set; } = string.Empty;
	public long WarehouseId { get; set; }
	public string WarehouseName { get; set; } = string.Empty;
	public InventoryCountStatus Status { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? StartedAtUtc { get; set; }
	public string CreatedByUserName { get; set; } = string.Empty;
	public int TotalLineCount { get; set; }
	public int CountedLineCount { get; set; }
	public int DifferenceLineCount { get; set; }
	public string? Notes { get; set; }
	public long Version { get; set; }
	public string StatusDisplayName => Status.ToString();
	public string ProgressDisplay => TotalLineCount == 0
		? "Not started"
		: $"{CountedLineCount:N0} / {TotalLineCount:N0} counted";
}
