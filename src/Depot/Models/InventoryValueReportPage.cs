// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryValueReportPage
{
	public IReadOnlyList<InventoryValueReportItem> Items { get; init; } = [];

	public int PageNumber { get; init; }

	public int PageSize { get; init; }

	public long TotalCount { get; init; }

	public int TotalInventoryRows { get; init; }

	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal TotalInventoryValue { get; init; }
}
