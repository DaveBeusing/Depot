// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryValueReportItem
{
	public long InventoryId { get; init; }

	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string? Manufacturer { get; init; }

	public string? Category { get; init; }

	public string PurposeName { get; init; } = string.Empty;

	public string WarehouseName { get; init; } = string.Empty;

	public string LocationName { get; init; } = string.Empty;

	public int CurrentStock { get; init; }

	public decimal AverageCost { get; init; }

	public decimal InventoryValue { get; init; }
}
