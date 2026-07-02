// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryValueReport
{
	public IReadOnlyList<InventoryValueReportItem> Items { get; init; }
		= Array.Empty<InventoryValueReportItem>();

	public int TotalInventoryRows { get; init; }

	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal TotalInventoryValue { get; init; }
}
