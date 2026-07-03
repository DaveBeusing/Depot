// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class LocationInventoryReportItem
{
	public string LocationName { get; init; } = string.Empty;

	public int InventoryRows { get; init; }

	public int TotalItems { get; init; }

	public int TotalStockQuantity { get; init; }

	public decimal InventoryValue { get; init; }
}
