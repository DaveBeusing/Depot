// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Models;

public sealed class InventorySummary
{
	public long ItemId { get; init; }

	public int CurrentStock { get; init; }

	public decimal AverageCost { get; init; }

	public decimal InventoryValue { get; init; }
}