// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class CategoryInventoryReport
{
	public IReadOnlyList<CategoryInventoryReportItem> Items { get; init; }
		= Array.Empty<CategoryInventoryReportItem>();

	public int TotalInventoryRows { get; init; }

	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal TotalInventoryValue { get; init; }
}
