// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class GroupedInventoryReport
{
	public IReadOnlyList<GroupedInventoryReportItem> Items { get; init; }
		= Array.Empty<GroupedInventoryReportItem>();

	public int TotalInventoryRows { get; init; }

	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal TotalInventoryValue { get; init; }
}
