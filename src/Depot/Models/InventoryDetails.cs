// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventoryDetails
{
	public long ItemId { get; init; }

	public string PartNumber { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string? Manufacturer { get; init; }

	public string? Category { get; init; }

	public int CurrentStock { get; init; }

	public decimal AverageCost { get; init; }

	public decimal InventoryValue { get; init; }

	public IReadOnlyList<StockMovement> RecentMovements { get; init; }
		= [];
}